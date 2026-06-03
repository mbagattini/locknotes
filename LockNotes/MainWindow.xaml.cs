using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Security.Cryptography;
using System.Text.Json;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Microsoft.UI.Text;
using Windows.UI;
using WinRT.Interop;

namespace LockNotes;

public sealed partial class MainWindow : Window
{
    internal const string AppTitle = "Lock Notes";
    const int MaxAttempts = 3;

    string? _filePath;
    string? _password;
    bool _dirty;
    bool _allowClose;
    bool _isHighlighting;
    string _lastHighlightedText = string.Empty;
    readonly string? _initialFilePath;
    int _fontSize = 15;

    static readonly Color _colorNormal = Color.FromArgb(0xFF, 0xD8, 0xDE, 0xE9); // #D8DEE9
    static readonly Color _colorMuted  = Color.FromArgb(0xFF, 0x61, 0x6E, 0x88); // #616E88

    public MainWindow(string? filePath)
    {
        InitializeComponent();
        txtContent.AddHandler(UIElement.DoubleTappedEvent, new DoubleTappedEventHandler(OnTextBoxDoubleTapped), true);
        var defaultFormat = txtContent.Document.GetDefaultCharacterFormat();
        defaultFormat.ForegroundColor = _colorNormal;
        txtContent.Document.SetDefaultCharacterFormat(defaultFormat);
        _initialFilePath = filePath;
        UpdateTitle();

        if (MicaController.IsSupported())
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.TitleBar.BackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.InactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonForegroundColor = Microsoft.UI.Colors.White;

        LoadWindowSettings();
        AppWindow.Closing += OnWindowClosing;
        AppWindow.Changed += OnAppWindowChanged;
        ((FrameworkElement)Content).Loaded += OnContentLoaded;
    }

    async void OnContentLoaded(object sender, RoutedEventArgs e)
    {
        ((FrameworkElement)Content).Loaded -= OnContentLoaded;
        await InitializeAsync(_initialFilePath);
    }

    async Task InitializeAsync(string? filePath, bool isNew = false)
    {
        if (filePath == null)
        {
            var (picked, pickedIsNew) = await PickFilePathAsync();
            if (picked == null) { Application.Current.Exit(); return; }
            filePath = picked;
            isNew = pickedIsNew;
        }

        _filePath = filePath;

        if (!isNew && !File.Exists(filePath))
        {
            bool confirm = await ShowYesNoDialogAsync(
                $"Il file '{Path.GetFileName(filePath)}' non esiste. Crearne uno nuovo?",
                "Nuovo file");
            if (!confirm) { Application.Current.Exit(); return; }
            isNew = true;
        }

        bool success = isNew ? await TryCreateNewAsync() : await TryOpenAsync();
        if (!success) { Application.Current.Exit(); return; }

        SetDirty(false);
        UpdateTitle();
        UpdateStatus();
        txtContent.Focus(FocusState.Programmatic);
        txtContent.Document.Selection.SetRange(0, 0);
    }

    async Task<bool> TryOpenAsync()
    {
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            string? pwd = await ShowPasswordDialogAsync($"Password per '{Path.GetFileName(_filePath!)}':");
            if (pwd == null) return false;

            try
            {
                byte[] data = File.ReadAllBytes(_filePath!);
                txtContent.Document.SetText(TextSetOptions.None, CryptoService.Decrypt(data, pwd));
                _password = pwd;
                return true;
            }
            catch (CryptographicException)
            {
                string msg = attempt < MaxAttempts
                    ? $"Password errata o file corrotto. Tentativi rimanenti: {MaxAttempts - attempt}."
                    : "Password errata o file corrotto. Numero massimo di tentativi raggiunto.";
                await ShowInfoDialogAsync(msg, "Avviso");
            }
        }
        return false;
    }

    async Task<bool> TryCreateNewAsync()
    {
        string? pwd = await AskNewPasswordAsync();
        if (pwd == null) return false;
        _password = pwd;
        txtContent.Document.SetText(TextSetOptions.None, string.Empty);
        return true;
    }

    async Task<string?> AskNewPasswordAsync()
    {
        while (true)
        {
            string? pwd1 = await ShowPasswordDialogAsync("Inserisci la password per il nuovo file:");
            if (pwd1 == null) return null;

            string? pwd2 = await ShowPasswordDialogAsync("Conferma la password:");
            if (pwd2 == null) return null;

            if (pwd1 == pwd2) return pwd1;

            await ShowInfoDialogAsync("Le password non coincidono. Riprova.", AppTitle);
        }
    }

    void Save()
    {
        if (_password == null) return;
        try
        {
            txtContent.Document.GetText(TextGetOptions.None, out string plainText);
            byte[] data = CryptoService.Encrypt(plainText.Replace('\r', '\n'), _password);
            string tmp = _filePath! + ".tmp";
            File.WriteAllBytes(tmp, data);
            File.Move(tmp, _filePath!, overwrite: true);
            SetDirty(false);
            UpdateStatus();
        }
        catch (Exception ex)
        {
            _ = ShowInfoDialogAsync($"Errore durante il salvataggio:\n{ex.Message}", AppTitle);
        }
    }

    async Task OpenAnotherAsync()
    {
        if (_dirty)
        {
            bool? answer = await ShowYesNoCancelDialogAsync(
                "Ci sono modifiche non salvate. Salvare prima di aprire un altro file?", AppTitle);
            if (answer == null) return;
            if (answer == true) Save();
        }

        string? path = await PickOpenFileAsync();
        if (path == null) return;

        _filePath = path;
        _password = null;
        txtContent.Document.SetText(TextSetOptions.None, string.Empty);
        SetDirty(false);

        if (await TryOpenAsync())
        {
            SetDirty(false);
            UpdateTitle();
            UpdateStatus();
            txtContent.Focus(FocusState.Programmatic);
            txtContent.Document.Selection.SetRange(0, 0);
        }
    }

    async Task ChangePasswordAsync()
    {
        string? pwd = await AskNewPasswordAsync();
        if (pwd == null) return;
        _password = pwd;
        SetDirty(true);
        await ShowInfoDialogAsync("Password aggiornata. Salva per applicare la modifica.", AppTitle);
    }

    void SetDirty(bool dirty)
    {
        _dirty = dirty;
        UpdateTitle();
    }

    void UpdateTitle()
    {
        if (_filePath == null)
        {
            TitleText.Text = AppTitle;
            Title = AppTitle;
            return;
        }
        string name = Path.GetFileName(_filePath);
        string title = _dirty ? $"{name} * — {AppTitle}" : $"{name} — {AppTitle}";
        TitleText.Text = title;
        Title = title;
    }

    void UpdateStatus()
    {
        lblFilePath.Text = _filePath ?? string.Empty;
        txtContent.Document.GetText(TextGetOptions.None, out string text);
        int lines = text.Length == 0 ? 0 : text.Split('\r').Length;
        lblInfo.Text = $"{lines} righe, {text.Length} caratteri";
    }

    void OnTextChanged(object sender, RoutedEventArgs e)
    {
        if (_isHighlighting) return;
        if (!_dirty)
        {
            // Distingue cambi di testo reali da eventi generati dai soli cambi di colore
            txtContent.Document.GetText(TextGetOptions.None, out string current);
            if (current == _lastHighlightedText) return;
            SetDirty(true);
        }
        UpdateStatus();
        ApplyHighlighting();
    }

    void OnTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
        bool ctrl = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;

        // VK_OEM_PLUS=187, VK_OEM_MINUS=189 (tasti +/- sulla tastiera principale)
        bool isPlus     = e.Key == Windows.System.VirtualKey.Add      || (int)e.Key == 187;
        bool isMinus    = e.Key == Windows.System.VirtualKey.Subtract || (int)e.Key == 189;

        if (ctrl && isPlus)  { SetFontSize(_fontSize + 1); e.Handled = true; return; }
        if (ctrl && isMinus) { SetFontSize(_fontSize - 1); e.Handled = true; return; }

        if (e.Key == Windows.System.VirtualKey.Tab)
        {
            txtContent.Document.Selection.TypeText("\t");
            e.Handled = true;
        }
    }

    async void OnPaste(object sender, TextControlPasteEventArgs e)
    {
        e.Handled = true; // sopprime il paste di default (che porta la formattazione)

        var dataView = Clipboard.GetContent();
        if (!dataView.Contains(StandardDataFormats.Text)) return;

        string text = await dataView.GetTextAsync();
        txtContent.Document.Selection.TypeText(text); // inserisce solo plain text e fa avanzare il cursore
    }

    void OnTextBoxDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // Il RichEditBox applica la sua selezione "per parola" dopo che l'evento si propaga,
        // quindi si differisce l'override alla prossima iterazione del dispatcher.
        DispatcherQueue.TryEnqueue(() =>
        {
            txtContent.Document.GetText(TextGetOptions.None, out string text);
            if (text.Length == 0) return;

            int anchor = txtContent.Document.Selection.StartPosition;

            int tokenStart = anchor;
            while (tokenStart > 0 && !IsTokenBoundary(text[tokenStart - 1]))
                tokenStart--;

            int tokenEnd = anchor;
            while (tokenEnd < text.Length && !IsTokenBoundary(text[tokenEnd]))
                tokenEnd++;

            txtContent.Document.Selection.SetRange(tokenStart, tokenEnd);
        });
    }

    static bool IsTokenBoundary(char c) =>
        c == ' ' || c == '\t' || c == '\r' || c == '\n';

    void ApplyHighlighting()
    {
        if (_isHighlighting) return;
        _isHighlighting = true;
        try
        {
            txtContent.Document.GetText(TextGetOptions.None, out string text);
            if (text == _lastHighlightedText) return; // solo cambi di formato, non di testo
            _lastHighlightedText = text;

            if (text.Length == 0) return;

            txtContent.Document.GetRange(0, text.Length).CharacterFormat.ForegroundColor = _colorNormal;

            int pos = 0;
            foreach (string line in text.Split('\r'))
            {
                int len = line.Length;

                if (len >= 3 && IsSeparatorLine(line))
                {
                    // Riga intera di separatore (===, ---, ###, ecc.)
                    txtContent.Document.GetRange(pos, pos + len).CharacterFormat.ForegroundColor = _colorMuted;
                }
                else
                {
                    // '>' o '-' come capo-linea (anche indentato): solo se seguito da spazio/tab o è da solo
                    int indent = 0;
                    while (indent < len && (line[indent] == ' ' || line[indent] == '\t'))
                        indent++;
                    if (indent < len && (line[indent] == '>' || line[indent] == '-'))
                    {
                        int after = indent + 1;
                        if (after == len || line[after] == ' ' || line[after] == '\t')
                            txtContent.Document.GetRange(pos + indent, pos + indent + 1).CharacterFormat.ForegroundColor = _colorMuted;
                    }

                    // '|' come separatore: solo se circondato da whitespace (o a inizio/fine riga)
                    for (int i = 0; i < len; i++)
                    {
                        if (line[i] != '|') continue;
                        bool leftOk  = i == 0       || line[i - 1] == ' ' || line[i - 1] == '\t';
                        bool rightOk = i == len - 1  || line[i + 1] == ' ' || line[i + 1] == '\t';
                        if (leftOk && rightOk)
                            txtContent.Document.GetRange(pos + i, pos + i + 1).CharacterFormat.ForegroundColor = _colorMuted;
                    }
                }

                pos += len + 1; // +1 per il '\r'
            }
        }
        finally
        {
            _isHighlighting = false;
        }
    }

    static bool IsSeparatorLine(string line)
    {
        char first = line[0];
        if (char.IsLetterOrDigit(first)) return false;
        foreach (char c in line)
            if (c != first) return false;
        return true;
    }

    void OnCtrlS(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Save();
    }

    async void OnCtrlO(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await OpenAnotherAsync();
    }

    async void OnCtrlP(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ChangePasswordAsync();
    }

    void OnMenuSave(object sender, RoutedEventArgs e) => Save();
    async void OnMenuOpen(object sender, RoutedEventArgs e) => await OpenAnotherAsync();
    async void OnMenuChangePassword(object sender, RoutedEventArgs e) => await ChangePasswordAsync();
    async void OnMenuExit(object sender, RoutedEventArgs e) => await ForceCloseAsync();
    async void OnMenuAbout(object sender, RoutedEventArgs e) => await ShowAboutDialogAsync();
    void OnMenuFontSizeUp(object sender, RoutedEventArgs e)   => SetFontSize(_fontSize + 1);
    void OnMenuFontSizeDown(object sender, RoutedEventArgs e) => SetFontSize(_fontSize - 1);

    void SetFontSize(int size)
    {
        _fontSize = Math.Clamp(size, 10, 32);
        txtContent.FontSize = _fontSize;
    }

    async Task ShowAboutDialogAsync()
    {
        string ver = ((System.Reflection.AssemblyInformationalVersionAttribute?)
            Attribute.GetCustomAttribute(
                typeof(MainWindow).Assembly,
                typeof(System.Reflection.AssemblyInformationalVersionAttribute)))
            ?.InformationalVersion ?? "—";
        await ShowInfoDialogAsync(
            $"Lock Notes  v{ver}\n\nGestore di file di testo cifrati.\n\n© 2026 Matteo Bagattini",
            "Informazioni");
    }

    void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized })
            AppWindow.Hide();
    }

    internal void ShowFromTray()
    {
        if (AppWindow.Presenter is OverlappedPresenter p)
            p.Restore();
        AppWindow.Show();
        Activate();
    }

    internal async Task ForceCloseAsync()
    {
        if (_dirty)
        {
            ShowFromTray();
            bool? answer = await ShowYesNoCancelDialogAsync(
                "Ci sono modifiche non salvate. Salvare prima di chiudere?", AppTitle);
            if (answer == null) return;
            if (answer == true) Save();
        }

        ExecuteClose();
    }

    async void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose) return;
        args.Cancel = true;

        if (_dirty)
        {
            bool? answer = await ShowYesNoCancelDialogAsync(
                "Ci sono modifiche non salvate. Salvare prima di chiudere?", AppTitle);
            if (answer == null) return;
            if (answer == true) Save();
        }

        ExecuteClose();
    }

    void ExecuteClose()
    {
        txtContent.Document.SetText(TextSetOptions.None, string.Empty);
        GC.Collect();
        SaveWindowSettings();
        ((App)Application.Current).DisposeTrayIcon();
        _allowClose = true;
        Application.Current.Exit();
    }

    // ---- File Pickers ----

    async Task<(string? path, bool isNew)> PickFilePathAsync()
    {
        var dialog = new ContentDialog
        {
            Title = AppTitle,
            Content = new TextBlock
            {
                Text = "Aprire un file esistente o crearne uno nuovo?",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "Apri esistente",
            SecondaryButtonText = "Crea nuovo",
            CloseButtonText = "Annulla",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
            return (await PickOpenFileAsync(), false);
        if (result == ContentDialogResult.Secondary)
            return (await PickSaveFileAsync(), true);
        return (null, false);
    }

    async Task<string?> PickOpenFileAsync()
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add(".stxt");
        picker.FileTypeFilter.Add("*");
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    async Task<string?> PickSaveFileAsync()
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeChoices.Add("File cifrati", new List<string> { ".stxt" });
        picker.DefaultFileExtension = ".stxt";
        picker.SuggestedFileName = "note";
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    // ---- Dialogs ----

    async Task<string?> ShowPasswordDialogAsync(string prompt)
    {
        var passwordBox = new PasswordBox { PlaceholderText = "Password" };
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(passwordBox);

        var dialog = new ContentDialog
        {
            Title = "Password richiesta",
            Content = content,
            PrimaryButtonText = "OK",
            CloseButtonText = "Annulla",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark
        };
        dialog.Opened += (_, _) => passwordBox.Focus(FocusState.Programmatic);

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? passwordBox.Password : null;
    }

    async Task ShowInfoDialogAsync(string message, string title)
    {
        await new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark
        }.ShowAsync();
    }

    async Task<bool> ShowYesNoDialogAsync(string message, string title)
    {
        var result = await new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "Sì",
            CloseButtonText = "No",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark
        }.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    async Task<bool?> ShowYesNoCancelDialogAsync(string message, string title)
    {
        var result = await new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "Sì",
            SecondaryButtonText = "No",
            CloseButtonText = "Annulla",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark
        }.ShowAsync();
        return result switch
        {
            ContentDialogResult.Primary => true,
            ContentDialogResult.Secondary => false,
            _ => null
        };
    }

    // ---- Window State ----

    record WindowSettings(int Width, int Height, int X, int Y, bool Maximized, string? LastFilePath = null, int FontSize = 15);

    string SettingsPath => Path.Combine(AppContext.BaseDirectory, "locknotes.settings.json");

    void LoadWindowSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var s = JsonSerializer.Deserialize<WindowSettings>(File.ReadAllText(SettingsPath));
            if (s == null) return;
            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(s.X, s.Y, s.Width, s.Height));
            if (s.Maximized && AppWindow.Presenter is OverlappedPresenter p)
                p.Maximize();
            SetFontSize(s.FontSize);
        }
        catch { }
    }

    void SaveWindowSettings()
    {
        try
        {
            bool maximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
            var pos = AppWindow.Position;
            var size = AppWindow.Size;
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(
                new WindowSettings(size.Width, size.Height, pos.X, pos.Y, maximized, _filePath, _fontSize)));
        }
        catch { }
    }
}
