using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;

namespace LockNotes;

// Stato e UI di un singolo file aperto in un tab: possiede il proprio RichEditBox
// e il TabViewItem, con dirty flag e highlighting indipendenti dagli altri tab.
sealed class DocumentTab
{
    static readonly Color _colorNormal     = Color.FromArgb(0xFF, 0xD8, 0xDE, 0xE9); // #D8DEE9
    static readonly Color _colorMuted      = Color.FromArgb(0xFF, 0x61, 0x6E, 0x88); // #616E88
    static readonly Color _colorBackground = Color.FromArgb(0xFF, 0x2E, 0x34, 0x40); // #2E3440
    static readonly Color _colorSelection  = Color.FromArgb(0xFF, 0x4C, 0x56, 0x6A); // #4C566A

    const string LockGlyph = ""; // lucchetto (Segoe Fluent Icons)

    public string? FilePath { get; set; } // null per i documenti nuovi mai salvati ("Senza titolo")
    public string? Password { get; set; }
    public string DisplayName => FilePath == null ? "Senza titolo" : Path.GetFileName(FilePath);
    public bool Dirty { get; private set; }
    public bool IsUnlocked { get; private set; }
    public bool Unlocking { get; set; } // guard contro doppi prompt password

    public TabViewItem Item { get; }
    public RichEditBox Editor { get; }
    public Grid Root { get; } // editor + placeholder, ospitato in EditorHost di MainWindow

    readonly UIElement _lockedPlaceholder;
    bool _isHighlighting;
    string _lastHighlightedText = string.Empty;

    readonly Action<DocumentTab> _onChanged;       // notifica MainWindow (titolo/status)
    readonly Action<int> _onFontDelta;             // Ctrl+/- dall'editor
    readonly Action<DocumentTab> _onUnlockRequest; // pulsante "Sblocca" del placeholder

    public DocumentTab(string? filePath, Action<DocumentTab> onChanged, Action<int> onFontDelta, Action<DocumentTab> onUnlockRequest)
    {
        FilePath = filePath;
        _onChanged = onChanged;
        _onFontDelta = onFontDelta;
        _onUnlockRequest = onUnlockRequest;

        // Il TabViewItem porta solo l'header (la strip sta nella title bar): editor e
        // placeholder vivono in un contenitore separato, ospitato in EditorHost di
        // MainWindow, che ne mostra uno solo per volta (quello del tab attivo).
        Editor = CreateEditor();
        Editor.Visibility = Visibility.Collapsed;
        _lockedPlaceholder = BuildLockedPlaceholder();

        Root = new Grid { Visibility = Visibility.Collapsed };
        Root.Children.Add(Editor);
        Root.Children.Add(_lockedPlaceholder);
        Item = new TabViewItem();
        UpdateHeader();
    }

    // ---- Stato ----

    public void SetDirty(bool dirty)
    {
        Dirty = dirty;
        UpdateHeader();
        _onChanged(this);
    }

    // password null per i documenti nuovi: viene chiesta al primo salvataggio
    public void MarkUnlocked(string? password)
    {
        Password = password;
        IsUnlocked = true;
        _lockedPlaceholder.Visibility = Visibility.Collapsed;
        Editor.Visibility = Visibility.Visible;
        UpdateHeader();
    }

    public void SetContent(string text)
    {
        Editor.Document.SetText(TextSetOptions.None, text);
        _lastHighlightedText = string.Empty;
        ApplyHighlighting();
        SetDirty(false);
    }

    public string GetContent()
    {
        Editor.Document.GetText(TextGetOptions.None, out string text);
        return text.Replace('\r', '\n');
    }

    public void UpdateHeader()
    {
        // Pallino di modifica come Notepad (al posto dell'asterisco)
        Item.Header = Dirty ? DisplayName + " \u25CF" : DisplayName; // pallino pieno
        Item.IconSource = IsUnlocked ? null : new FontIconSource { Glyph = LockGlyph };
    }

    public void ApplyFontSize(int size, float tabStop)
    {
        Editor.FontSize = size;
        Editor.Document.DefaultTabStop = tabStop;
    }

    // ---- Costruzione UI ----

    RichEditBox CreateEditor()
    {
        var editor = new RichEditBox
        {
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Code"),
            FontWeight = FontWeights.Medium,
            Background = new SolidColorBrush(_colorBackground),
            Foreground = new SolidColorBrush(_colorNormal),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            Padding = new Thickness(12),
            IsSpellCheckEnabled = false,
            IsTextPredictionEnabled = false,
            VerticalContentAlignment = VerticalAlignment.Top,
            SelectionHighlightColor = new SolidColorBrush(_colorSelection)
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(editor, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(editor, ScrollBarVisibility.Auto);

        // Override dei brush di stato del TextControl (normale/pointer-over/focused)
        foreach (string key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" })
            editor.Resources[key] = new SolidColorBrush(_colorBackground);
        foreach (string key in new[] { "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused" })
            editor.Resources[key] = new SolidColorBrush(_colorNormal);
        foreach (string key in new[] { "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused" })
            editor.Resources[key] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        var defaultFormat = editor.Document.GetDefaultCharacterFormat();
        defaultFormat.ForegroundColor = _colorNormal;
        editor.Document.SetDefaultCharacterFormat(defaultFormat);

        // All'applicazione del template il RichEditBox ri-applica il Foreground all'intero
        // documento, cancellando i colori per-range. L'editor nasce Collapsed e il template
        // arriva DOPO SetContent: si ri-applica l'highlighting al Loaded (differito di un
        // giro di dispatcher per essere sicuri che il wipe sia gia' avvenuto).
        editor.Loaded += (_, _) => editor.DispatcherQueue.TryEnqueue(() =>
        {
            _lastHighlightedText = string.Empty;
            ApplyHighlighting();
        });

        editor.TextChanged += OnTextChanged;
        editor.KeyDown += OnKeyDown;
        editor.Paste += OnPaste;
        editor.AddHandler(UIElement.DoubleTappedEvent, new DoubleTappedEventHandler(OnDoubleTapped), true);
        return editor;
    }

    UIElement BuildLockedPlaceholder()
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 12
        };
        panel.Children.Add(new FontIcon
        {
            Glyph = LockGlyph,
            FontSize = 32,
            Foreground = new SolidColorBrush(_colorMuted),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = "File bloccato",
            Foreground = new SolidColorBrush(_colorMuted),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        var unlockButton = new Button
        {
            Content = "Sblocca...",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        unlockButton.Click += (_, _) => _onUnlockRequest(this);
        panel.Children.Add(unlockButton);

        var container = new Grid { Background = new SolidColorBrush(_colorBackground) };
        container.Children.Add(panel);
        return container;
    }

    // ---- Handler editor ----

    void OnTextChanged(object sender, RoutedEventArgs e)
    {
        if (_isHighlighting) return;
        if (!Dirty)
        {
            // Distingue cambi di testo reali da eventi generati dai soli cambi di colore
            Editor.Document.GetText(TextGetOptions.None, out string current);
            if (current == _lastHighlightedText) return;
            SetDirty(true);
        }
        else
        {
            _onChanged(this);
        }
        ApplyHighlighting();
    }

    void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
        bool ctrl = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;

        // VK_OEM_PLUS=187, VK_OEM_MINUS=189 (tasti +/- sulla tastiera principale)
        bool isPlus  = e.Key == Windows.System.VirtualKey.Add      || (int)e.Key == 187;
        bool isMinus = e.Key == Windows.System.VirtualKey.Subtract || (int)e.Key == 189;

        if (ctrl && isPlus)  { _onFontDelta(+1); e.Handled = true; return; }
        if (ctrl && isMinus) { _onFontDelta(-1); e.Handled = true; return; }

        if (e.Key == Windows.System.VirtualKey.Tab)
        {
            Editor.Document.Selection.TypeText("\t");
            e.Handled = true;
        }
    }

    async void OnPaste(object sender, TextControlPasteEventArgs e)
    {
        e.Handled = true; // sopprime il paste di default (che porta la formattazione)

        var dataView = Clipboard.GetContent();
        if (!dataView.Contains(StandardDataFormats.Text)) return;

        string text = await dataView.GetTextAsync();
        Editor.Document.Selection.TypeText(text); // inserisce solo plain text e fa avanzare il cursore
    }

    void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // Il RichEditBox applica la sua selezione "per parola" dopo che l'evento si propaga,
        // quindi si differisce l'override alla prossima iterazione del dispatcher.
        Editor.DispatcherQueue.TryEnqueue(() =>
        {
            Editor.Document.GetText(TextGetOptions.None, out string text);
            if (text.Length == 0) return;

            int anchor = Editor.Document.Selection.StartPosition;

            int tokenStart = anchor;
            while (tokenStart > 0 && !IsTokenBoundary(text[tokenStart - 1]))
                tokenStart--;

            int tokenEnd = anchor;
            while (tokenEnd < text.Length && !IsTokenBoundary(text[tokenEnd]))
                tokenEnd++;

            Editor.Document.Selection.SetRange(tokenStart, tokenEnd);
        });
    }

    static bool IsTokenBoundary(char c) =>
        c == ' ' || c == '\t' || c == '\r' || c == '\n';

    // ---- Highlighting ----

    void ApplyHighlighting()
    {
        if (_isHighlighting) return;
        _isHighlighting = true;
        try
        {
            Editor.Document.GetText(TextGetOptions.None, out string text);
            if (text == _lastHighlightedText) return; // solo cambi di formato, non di testo
            _lastHighlightedText = text;

            if (text.Length == 0) return;

            Editor.Document.GetRange(0, text.Length).CharacterFormat.ForegroundColor = _colorNormal;

            int pos = 0;
            foreach (string line in text.Split('\r'))
            {
                int len = line.Length;

                if (len >= 3 && IsSeparatorLine(line))
                {
                    // Riga intera di separatore (===, ---, ###, ecc.)
                    Editor.Document.GetRange(pos, pos + len).CharacterFormat.ForegroundColor = _colorMuted;
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
                            Editor.Document.GetRange(pos + indent, pos + indent + 1).CharacterFormat.ForegroundColor = _colorMuted;
                    }

                    // '|' come separatore: solo se circondato da whitespace (o a inizio/fine riga)
                    for (int i = 0; i < len; i++)
                    {
                        if (line[i] != '|') continue;
                        bool leftOk  = i == 0       || line[i - 1] == ' ' || line[i - 1] == '\t';
                        bool rightOk = i == len - 1 || line[i + 1] == ' ' || line[i + 1] == '\t';
                        if (leftOk && rightOk)
                            Editor.Document.GetRange(pos + i, pos + i + 1).CharacterFormat.ForegroundColor = _colorMuted;
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
}
