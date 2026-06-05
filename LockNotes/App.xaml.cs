using Microsoft.UI.Xaml;
using Microsoft.Win32;
using System.Diagnostics;

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
    // esistente (sia nascosta in tray sia minimizzata).
    internal void OnRedirectedActivation()
    {
        _mainWindow?.DispatcherQueue.TryEnqueue(() => _mainWindow.ShowFromTray());
    }

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
