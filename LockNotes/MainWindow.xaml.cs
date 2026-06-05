using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Security.Cryptography;
using System.Text.Json;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace LockNotes;

public sealed partial class MainWindow : Window
{
    internal const string AppTitle = "Lock Notes";
    const int MaxAttempts = 3;

    readonly List<DocumentTab> _tabs = new();
    readonly SemaphoreSlim _dialogGate = new(1, 1); // un solo ContentDialog/picker aperto per volta
    readonly string? _cliPath;
    bool _allowClose;
    bool _suppressAutoUnlock; // evita prompt password durante mutazioni programmatiche dei tab
    int _fontSize = 15;
    List<string> _restoreFiles = new();
    int _restoreActiveIndex;

    DocumentTab? ActiveTab => Tabs.SelectedItem is TabViewItem item
        ? _tabs.FirstOrDefault(t => t.Item == item)
        : null;

    public MainWindow(string? filePath)
    {
        InitializeComponent();
        _cliPath = filePath;
        UpdateTitle();

        if (MicaController.IsSupported())
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };

        // Stile Notepad: la tab strip sta nella title bar; la drag region e' lo
        // spazio vuoto a destra dei tab (TabStripFooter), cosi' tab e pulsante +
        // restano cliccabili.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);

        AppWindow.TitleBar.BackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.InactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonForegroundColor = Microsoft.UI.Colors.White;

        LoadWindowSettings();
        AppWindow.Closing += OnWindowClosing;
        ((FrameworkElement)Content).Loaded += OnContentLoaded;
    }

    async void OnContentLoaded(object sender, RoutedEventArgs e)
    {
        ((FrameworkElement)Content).Loaded -= OnContentLoaded;
        await RestoreSessionAsync();
    }

    // ---- Sessione e gestione tab ----

    async Task RestoreSessionAsync()
    {
        _suppressAutoUnlock = true;

        foreach (string path in _restoreFiles.Where(File.Exists))
            AddTab(path, selectAfter: false);

        if (_cliPath != null)
        {
            DocumentTab? existing = FindTabByPath(_cliPath);
            if (existing != null)
                Tabs.SelectedItem = existing.Item;
            else if (File.Exists(_cliPath))
                AddTab(_cliPath, selectAfter: true);
        }
        else if (_tabs.Count > 0)
        {
            Tabs.SelectedIndex = Math.Clamp(_restoreActiveIndex, 0, _tabs.Count - 1);
        }

        // Come Notepad: senza file da ripristinare ne' da CLI si apre un "Senza titolo"
        if (_tabs.Count == 0)
            AddUntitledTab();

        _suppressAutoUnlock = false;
        UpdateEditorVisibility();
        UpdateTitle();
        UpdateStatus();

        // Sblocco lazy: solo il tab attivo chiede subito la password, gli altri restano bloccati
        if (ActiveTab is { IsUnlocked: false } active)
            await UnlockTabAsync(active);
    }

    DocumentTab AddTab(string? filePath, bool selectAfter)
    {
        var tab = new DocumentTab(
            filePath,
            onChanged: OnTabChanged,
            onFontDelta: delta => SetFontSize(_fontSize + delta),
            onUnlockRequest: t => _ = UnlockTabAsync(t));
        tab.ApplyFontSize(_fontSize, (float)(MeasureCharWidth() * 4));
        _tabs.Add(tab);
        EditorHost.Children.Add(tab.Root);
        Tabs.TabItems.Add(tab.Item);
        if (selectAfter)
            Tabs.SelectedItem = tab.Item;
        return tab;
    }

    // Nuovo documento vuoto in stile Notepad: nessun file su disco e nessuna password
    // finche' l'utente non salva (vedi SaveAsync).
    DocumentTab AddUntitledTab()
    {
        _suppressAutoUnlock = true; // la selezione non deve innescare il prompt password
        var tab = AddTab(null, selectAfter: true);
        tab.MarkUnlocked(null);
        tab.SetContent(string.Empty);
        _suppressAutoUnlock = false;

        UpdateTitle();
        UpdateStatus();
        FocusEditor(tab);
        return tab;
    }

    DocumentTab? FindTabByPath(string path)
    {
        string full = Path.GetFullPath(path);
        return _tabs.FirstOrDefault(t =>
            t.FilePath != null &&
            string.Equals(Path.GetFullPath(t.FilePath), full, StringComparison.OrdinalIgnoreCase));
    }

    void OnTabChanged(DocumentTab tab)
    {
        if (tab != ActiveTab) return;
        UpdateTitle();
        UpdateStatus();
    }

    // Mostra solo l'editor del tab attivo (gli editor sono sovrapposti in EditorHost)
    void UpdateEditorVisibility()
    {
        foreach (var tab in _tabs)
            tab.Root.Visibility = tab == ActiveTab ? Visibility.Visible : Visibility.Collapsed;
    }

    async void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateEditorVisibility();
        UpdateTitle();
        UpdateStatus();
        if (_suppressAutoUnlock) return;
        if (ActiveTab is { IsUnlocked: false, Unlocking: false } tab)
            await UnlockTabAsync(tab);
    }

    // ---- Apertura / creazione / sblocco ----

    async Task OpenFileAsync()
    {
        string? path = await PickOpenFileAsync();
        if (path == null) return;

        DocumentTab? existing = FindTabByPath(path);
        if (existing != null)
        {
            Tabs.SelectedItem = existing.Item;
            return;
        }

        // La selezione del nuovo tab innesca lo sblocco lazy (OnTabSelectionChanged)
        AddTab(path, selectAfter: true);
    }

    async Task<bool> UnlockTabAsync(DocumentTab tab)
    {
        if (tab.IsUnlocked || tab.Unlocking || tab.FilePath == null) return tab.IsUnlocked;
        tab.Unlocking = true;
        try
        {
            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                string? pwd = await ShowPasswordDialogAsync($"Password per '{tab.DisplayName}':");
                if (pwd == null) return false; // annullato: il tab resta bloccato e riprovabile

                try
                {
                    byte[] data = File.ReadAllBytes(tab.FilePath);
                    string plainText = CryptoService.Decrypt(data, pwd);
                    tab.MarkUnlocked(pwd);
                    tab.SetContent(plainText);
                    UpdateTitle();
                    UpdateStatus();
                    FocusEditor(tab);
                    return true;
                }
                catch (CryptographicException)
                {
                    string msg = attempt < MaxAttempts
                        ? $"Password errata o file corrotto. Tentativi rimanenti: {MaxAttempts - attempt}."
                        : "Password errata o file corrotto. Numero massimo di tentativi raggiunto.";
                    await ShowInfoDialogAsync(msg, "Avviso");
                }
                catch (Exception ex)
                {
                    await ShowInfoDialogAsync($"Errore durante l'apertura:\n{ex.Message}", AppTitle);
                    return false;
                }
            }
            return false; // tentativi esauriti: il tab resta bloccato
        }
        finally
        {
            tab.Unlocking = false;
        }
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

    async Task ChangePasswordActiveAsync()
    {
        // Solo per file gia' salvati: per i "Senza titolo" la password si sceglie al salvataggio
        if (ActiveTab is not { IsUnlocked: true, FilePath: not null } tab) return;

        string? pwd = await AskNewPasswordAsync();
        if (pwd == null) return;
        tab.Password = pwd;
        tab.SetDirty(true);
        await ShowInfoDialogAsync("Password aggiornata. Salva per applicare la modifica.", AppTitle);
    }

    // ---- Salvataggio ----

    // Ritorna false se il salvataggio non e' avvenuto (picker o password annullati, errore I/O)
    async Task<bool> SaveAsync(DocumentTab tab)
    {
        if (!tab.IsUnlocked) return false;

        // Primo salvataggio di un "Senza titolo": chiede percorso e password
        if (tab.FilePath == null)
        {
            string? path = await PickSaveFileAsync();
            if (path == null) return false;
            if (FindTabByPath(path) != null)
            {
                await ShowInfoDialogAsync("Il file e' gia' aperto in un'altra scheda.", AppTitle);
                return false;
            }
            string? pwd = await AskNewPasswordAsync();
            if (pwd == null) return false;
            tab.FilePath = path;
            tab.Password = pwd;
        }

        try
        {
            byte[] data = CryptoService.Encrypt(tab.GetContent(), tab.Password!);
            string tmp = tab.FilePath + ".tmp";
            File.WriteAllBytes(tmp, data);
            File.Move(tmp, tab.FilePath, overwrite: true);
            tab.SetDirty(false); // aggiorna anche header, titolo e status bar via _onChanged
            return true;
        }
        catch (Exception ex)
        {
            await ShowInfoDialogAsync($"Errore durante il salvataggio:\n{ex.Message}", AppTitle);
            return false;
        }
    }

    async Task SaveActiveAsync()
    {
        if (ActiveTab is { } tab) await SaveAsync(tab);
    }

    async Task SaveAllAsync()
    {
        foreach (var tab in _tabs.Where(t => t.IsUnlocked && t.Dirty).ToList())
            await SaveAsync(tab);
    }

    // ---- Chiusura tab e applicazione ----

    async Task<bool> CloseTabAsync(DocumentTab tab)
    {
        if (tab.IsUnlocked && tab.Dirty)
        {
            Tabs.SelectedItem = tab.Item; // mostra quale tab si sta chiudendo
            bool? answer = await ShowYesNoCancelDialogAsync(
                $"'{tab.DisplayName}' ha modifiche non salvate. Salvare prima di chiudere?", AppTitle);
            if (answer == null) return false;
            if (answer == true && !await SaveAsync(tab)) return false; // salvataggio annullato: il tab resta aperto
        }

        _tabs.Remove(tab);
        EditorHost.Children.Remove(tab.Root);
        Tabs.TabItems.Remove(tab.Item);
        UpdateEditorVisibility();
        UpdateTitle();
        UpdateStatus();
        return true;
    }

    async Task CloseActiveTabAsync()
    {
        if (ActiveTab is { } tab) await CloseTabAsync(tab);
    }

    async void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs e)
    {
        if (_tabs.FirstOrDefault(t => t.Item == e.Tab) is { } tab)
            await CloseTabAsync(tab);
    }

    internal async Task ForceCloseAsync()
    {
        var dirtyTabs = _tabs.Where(t => t.IsUnlocked && t.Dirty).ToList();
        if (dirtyTabs.Count > 0)
            ShowFromTray();

        foreach (var tab in dirtyTabs)
        {
            Tabs.SelectedItem = tab.Item;
            bool? answer = await ShowYesNoCancelDialogAsync(
                $"'{tab.DisplayName}' ha modifiche non salvate. Salvare prima di uscire?", AppTitle);
            if (answer == null) return; // Annulla interrompe l'uscita
            if (answer == true && !await SaveAsync(tab)) return; // salvataggio annullato: si resta aperti
        }

        ExecuteClose();
    }

    void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        // Il tasto di chiusura non termina l'app: la nasconde nella tray.
        // L'uscita effettiva avviene da menu File > Esci o dalla tray.
        if (_allowClose) return;
        args.Cancel = true;
        AppWindow.Hide();
    }

    void ExecuteClose()
    {
        SaveWindowSettings(); // prima di svuotare gli editor: serve la lista dei tab aperti
        foreach (var tab in _tabs.Where(t => t.IsUnlocked))
            tab.Editor.Document.SetText(TextSetOptions.None, string.Empty);
        GC.Collect();
        ((App)Application.Current).DisposeTrayIcon();
        _allowClose = true;
        Application.Current.Exit();
    }

    internal void ShowFromTray()
    {
        if (AppWindow.Presenter is OverlappedPresenter p)
            p.Restore();
        AppWindow.Show();
        Activate();
    }

    // ---- Titolo / status / font ----

    void UpdateTitle()
    {
        // Niente testo nella title bar (ci sono i tab): Title serve per taskbar e Alt+Tab
        if (ActiveTab is not { } tab)
        {
            Title = AppTitle;
            return;
        }
        Title = tab.Dirty ? $"{tab.DisplayName} * — {AppTitle}" : $"{tab.DisplayName} — {AppTitle}";
    }

    void UpdateStatus()
    {
        if (ActiveTab is not { } tab)
        {
            lblFilePath.Text = string.Empty;
            lblInfo.Text = string.Empty;
            return;
        }
        lblFilePath.Text = tab.FilePath ?? tab.DisplayName;
        if (!tab.IsUnlocked)
        {
            lblInfo.Text = "bloccato";
            return;
        }
        tab.Editor.Document.GetText(TextGetOptions.None, out string text);
        // Il RichEditBox aggiunge sempre un '\r' finale: lo si scarta dal conteggio
        if (text.EndsWith('\r')) text = text[..^1];
        lblInfo.Text = text.Length == 0
            ? string.Empty
            : $"{text.Split('\r').Length} righe, {text.Length} caratteri";
    }

    void FocusEditor(DocumentTab tab)
    {
        if (!tab.IsUnlocked || tab != ActiveTab) return;

        // L'editor nasce Collapsed e potrebbe non essere ancora caricato: il focus
        // dato ora andrebbe perso (e resterebbe sul pulsante "+"); si differisce al Loaded.
        if (!tab.Editor.IsLoaded)
        {
            void OnEditorLoaded(object s, RoutedEventArgs e)
            {
                tab.Editor.Loaded -= OnEditorLoaded;
                FocusEditor(tab);
            }
            tab.Editor.Loaded += OnEditorLoaded;
            return;
        }

        tab.Editor.Focus(FocusState.Programmatic);
        tab.Editor.Document.Selection.SetRange(0, 0);
    }

    void SetFontSize(int size)
    {
        _fontSize = Math.Clamp(size, 10, 32);
        // Tabulazione pari a 4 caratteri: il font e' monospace, quindi 4 x larghezza di un carattere.
        float tabStop = (float)(MeasureCharWidth() * 4);
        foreach (var tab in _tabs)
            tab.ApplyFontSize(_fontSize, tabStop);
    }

    double MeasureCharWidth()
    {
        var probe = new TextBlock
        {
            FontFamily = new FontFamily("Cascadia Code"),
            FontSize = _fontSize,
            Text = "0"
        };
        probe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        return probe.DesiredSize.Width;
    }

    // ---- Handler menu e acceleratori ----

    void OnMenuNew(object sender, RoutedEventArgs e) => AddUntitledTab();
    async void OnMenuOpen(object sender, RoutedEventArgs e) => await OpenFileAsync();
    async void OnMenuSave(object sender, RoutedEventArgs e) => await SaveActiveAsync();
    async void OnMenuSaveAll(object sender, RoutedEventArgs e) => await SaveAllAsync();
    async void OnMenuCloseTab(object sender, RoutedEventArgs e) => await CloseActiveTabAsync();
    async void OnMenuChangePassword(object sender, RoutedEventArgs e) => await ChangePasswordActiveAsync();
    async void OnMenuExit(object sender, RoutedEventArgs e) => await ForceCloseAsync();
    async void OnMenuAbout(object sender, RoutedEventArgs e) => await ShowAboutDialogAsync();
    void OnMenuFontSizeUp(object sender, RoutedEventArgs e)   => SetFontSize(_fontSize + 1);
    void OnMenuFontSizeDown(object sender, RoutedEventArgs e) => SetFontSize(_fontSize - 1);

    void OnCtrlN(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        AddUntitledTab();
    }

    async void OnCtrlO(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await OpenFileAsync();
    }

    async void OnCtrlS(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await SaveActiveAsync();
    }

    async void OnCtrlShiftS(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await SaveAllAsync();
    }

    async void OnCtrlW(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await CloseActiveTabAsync();
    }

    async void OnCtrlP(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ChangePasswordActiveAsync();
    }

    void OnAddTabClick(TabView sender, object args) => AddUntitledTab();

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

    // ---- File Pickers (serializzati dal gate come i dialoghi) ----

    async Task<string?> PickOpenFileAsync()
    {
        await _dialogGate.WaitAsync();
        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add(".stxt");
            picker.FileTypeFilter.Add("*");
            var file = await picker.PickSingleFileAsync();
            return file?.Path;
        }
        finally
        {
            _dialogGate.Release();
        }
    }

    async Task<string?> PickSaveFileAsync()
    {
        await _dialogGate.WaitAsync();
        try
        {
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeChoices.Add("File cifrati", new List<string> { ".stxt" });
            picker.DefaultFileExtension = ".stxt";
            picker.SuggestedFileName = "note";
            var file = await picker.PickSaveFileAsync();
            return file?.Path;
        }
        finally
        {
            _dialogGate.Release();
        }
    }

    // ---- Dialogs (in WinUI 3 un solo ContentDialog puo' essere aperto per volta) ----

    async Task<string?> ShowPasswordDialogAsync(string prompt)
    {
        await _dialogGate.WaitAsync();
        try
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
        finally
        {
            _dialogGate.Release();
        }
    }

    async Task ShowInfoDialogAsync(string message, string title)
    {
        await _dialogGate.WaitAsync();
        try
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
        finally
        {
            _dialogGate.Release();
        }
    }

    async Task<bool?> ShowYesNoCancelDialogAsync(string message, string title)
    {
        await _dialogGate.WaitAsync();
        try
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
        finally
        {
            _dialogGate.Release();
        }
    }

    // ---- Window State ----

    record WindowSettings(int Width, int Height, int X, int Y, bool Maximized,
        List<string>? OpenFiles = null, int ActiveIndex = 0, int FontSize = 15);

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
            _restoreFiles = s.OpenFiles ?? new List<string>();
            _restoreActiveIndex = s.ActiveIndex;
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
            // Lista file nell'ordine visivo della strip (l'utente puo' aver riordinato i tab)
            var openFiles = Tabs.TabItems.OfType<TabViewItem>()
                .Select(item => _tabs.FirstOrDefault(t => t.Item == item)?.FilePath)
                .OfType<string>()
                .ToList();
            int activeIndex = Math.Max(0, Tabs.SelectedIndex);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(
                new WindowSettings(size.Width, size.Height, pos.X, pos.Y, maximized, openFiles, activeIndex, _fontSize)));
        }
        catch { }
    }
}
