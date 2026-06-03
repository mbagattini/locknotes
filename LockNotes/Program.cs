using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System.Runtime.InteropServices;

namespace LockNotes;

public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // Se e' gia' in esecuzione un'altra istanza, le si inoltra l'attivazione e si esce.
        if (DecideRedirection())
            return;

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }

    // Restituisce true se l'avvio e' stato reindirizzato a un'istanza gia' attiva.
    static bool DecideRedirection()
    {
        var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        var keyInstance = AppInstance.FindOrRegisterForKey("LockNotes-SingleInstance");

        if (keyInstance.IsCurrent)
        {
            // Siamo l'istanza principale: ci mettiamo in ascolto delle riattivazioni.
            keyInstance.Activated += OnActivated;
            return false;
        }

        RedirectActivationTo(activationArgs, keyInstance);
        return true;
    }

    static void OnActivated(object? sender, AppActivationArguments args)
    {
        // Invocato sull'istanza principale quando un'altra istanza prova a partire.
        (Application.Current as App)?.OnRedirectedActivation();
    }

    // ---- Redirezione con pump dei messaggi COM (pattern documentato WinUI 3) ----

    static void RedirectActivationTo(AppActivationArguments args, AppInstance keyInstance)
    {
        IntPtr eventHandle = CreateEvent(IntPtr.Zero, true, false, null);
        Task.Run(() =>
        {
            keyInstance.RedirectActivationToAsync(args).AsTask().Wait();
            SetEvent(eventHandle);
        });

        const uint CWMO_DEFAULT = 0;
        const uint INFINITE = 0xFFFFFFFF;
        _ = CoWaitForMultipleObjects(CWMO_DEFAULT, INFINITE, 1, new[] { eventHandle }, out _);
    }

    [DllImport("kernel32.dll")]
    static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll")]
    static extern bool SetEvent(IntPtr hEvent);

    [DllImport("ole32.dll")]
    static extern uint CoWaitForMultipleObjects(uint dwFlags, uint dwMilliseconds, ulong nHandles, IntPtr[] pHandles, out uint dwIndex);
}
