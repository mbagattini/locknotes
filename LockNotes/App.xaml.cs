using Microsoft.UI.Xaml;
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LockNotes;

public partial class App : Application
{
    TrayIcon? _trayIcon;
    MainWindow? _mainWindow;

    const string RegistryRunPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    const string StartupValueName = "LockNotes";

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            try
            {
                string log = Path.Combine(AppContext.BaseDirectory, "errors.log");
                File.AppendAllText(log, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}{Environment.NewLine}");
            }
            catch { }
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Solo l'eventuale path da CLI: il ripristino della sessione (lista tab)
        // lo gestisce MainWindow leggendo i settings.
        string[] cmdArgs = Environment.GetCommandLineArgs();
        string? filePath = cmdArgs.Length > 1 ? cmdArgs[1] : null;
        _mainWindow = new MainWindow(filePath);
        _mainWindow.Activate();

        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "locknotes.ico");
        _trayIcon = new TrayIcon(
            iconPath: iconPath,
            tooltip: "Lock Notes",
            onOpen: () => _mainWindow?.DispatcherQueue.TryEnqueue(() => _mainWindow.ShowFromTray()),
            onExit: () => _mainWindow?.DispatcherQueue.TryEnqueue(async () => await _mainWindow.ForceCloseAsync()),
            isStartupEnabled: IsStartupEnabled,
            toggleStartup: ToggleStartup
        );
    }

    internal void DisposeTrayIcon()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    // Invocato quando una seconda istanza prova a partire: ripristina la finestra
    // e apre il file se passato come argomento (es. doppio click su .stxt).
    internal void OnRedirectedActivation(string? filePath)
    {
        _mainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            if (filePath != null)
                _mainWindow.OpenFromPath(filePath);
            else
                _mainWindow.ShowFromTray();
        });
    }

    internal static void RegisterFileAssociation()
    {
        string exe = Process.GetCurrentProcess().MainModule!.FileName;
        const string ProgId = "LockNotes.stxt";

        using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.stxt"))
            k.SetValue("", ProgId);

        using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId))
            k.SetValue("", "Lock Notes Document");

        using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId + @"\DefaultIcon"))
            k.SetValue("", $"\"{exe}\",0");

        using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId + @"\shell\open\command"))
            k.SetValue("", $"\"{exe}\" \"%1\"");

        SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
    }

    internal static bool IsFileAssociationRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\.stxt");
            return key?.GetValue("") as string == "LockNotes.stxt";
        }
        catch { return false; }
    }

    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath);
            return key?.GetValue(StartupValueName) != null;
        }
        catch { return false; }
    }

    static void ToggleStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, writable: true);
            if (key == null) return;
            if (IsStartupEnabled())
                key.DeleteValue(StartupValueName, throwOnMissingValue: false);
            else
            {
                string exe = Process.GetCurrentProcess().MainModule!.FileName;
                key.SetValue(StartupValueName, $"\"{exe}\"");
            }
        }
        catch { }
    }
}
