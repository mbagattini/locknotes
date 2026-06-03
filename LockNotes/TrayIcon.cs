using System.Runtime.InteropServices;

namespace LockNotes;

sealed class TrayIcon : IDisposable
{
    // ---- Win32 P/Invoke ----

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        [MarshalAs(UnmanagedType.FunctionPtr)]
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int ptX, ptY; }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int x, y; }

    delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA d);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenu(IntPtr hMenu, uint flags, IntPtr id, string? text);
    [DllImport("user32.dll")] static extern int TrackPopupMenuEx(IntPtr hMenu, uint flags, int x, int y, IntPtr hWnd, IntPtr tpm);
    [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr hMenu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadImage(IntPtr hinst, string name, uint type, int cx, int cy, uint fu);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);

    const uint NIM_ADD = 0, NIM_DELETE = 2, NIM_SETVERSION = 4;
    const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;
    const uint NOTIFYICON_VERSION_4 = 4;
    const uint WM_TRAYICON = 0x8001;
    const uint WM_RBUTTONUP = 0x205, WM_LBUTTONDBLCLK = 0x203, WM_CONTEXTMENU = 0x7B, WM_NULL = 0;
    const uint MF_STRING = 0, MF_SEPARATOR = 0x800, MF_CHECKED = 8;
    const uint TPM_RIGHTBUTTON = 2, TPM_NONOTIFY = 0x80, TPM_RETURNCMD = 0x100;
    const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x10, LR_DEFAULTSIZE = 0x40;

    const int CMD_OPEN = 1, CMD_STARTUP = 2, CMD_EXIT = 3;

    // ---- State ----

    NOTIFYICONDATA _nid;
    IntPtr _hWnd;
    WndProcDelegate? _wndProcRef; // keep alive against GC
    bool _disposed;

    readonly Action _onOpen;
    readonly Action _onExit;
    readonly Func<bool> _isStartupEnabled;
    readonly Action _toggleStartup;

    public TrayIcon(string iconPath, string tooltip, Action onOpen, Action onExit,
                    Func<bool> isStartupEnabled, Action toggleStartup)
    {
        _onOpen = onOpen;
        _onExit = onExit;
        _isStartupEnabled = isStartupEnabled;
        _toggleStartup = toggleStartup;

        var t = new Thread(() => RunLoop(iconPath, tooltip))
        {
            IsBackground = true,
            Name = "TrayIconThread"
        };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    void RunLoop(string iconPath, string tooltip)
    {
        var hInst = GetModuleHandle(null);
        string className = "LockNotesTray_" + Environment.ProcessId;

        _wndProcRef = WndProc;
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = _wndProcRef,
            hInstance = hInst,
            lpszClassName = className
        };
        RegisterClassEx(ref wc);

        // HWND_MESSAGE = -3 → finestra solo per messaggi, non visibile
        _hWnd = CreateWindowEx(0, className, "LockNotesTray", 0, 0, 0, 0, 0,
                               new IntPtr(-3), IntPtr.Zero, hInst, IntPtr.Zero);

        var hIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);

        _nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hWnd,
            uID = 1,
            uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = hIcon,
            szTip = tooltip
        };
        Shell_NotifyIcon(NIM_ADD, ref _nid);
        _nid.uVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIcon(NIM_SETVERSION, ref _nid);

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0))
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAYICON)
        {
            uint evt = (uint)(lParam.ToInt64() & 0xFFFF);
            if (evt == WM_LBUTTONDBLCLK) _onOpen();
            else if (evt == WM_RBUTTONUP || evt == WM_CONTEXTMENU) ShowMenu(hWnd);
            return IntPtr.Zero;
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    void ShowMenu(IntPtr hWnd)
    {
        var hMenu = CreatePopupMenu();
        AppendMenu(hMenu, MF_STRING, new IntPtr(CMD_OPEN), "Apri Lock Notes");
        AppendMenu(hMenu, MF_SEPARATOR, IntPtr.Zero, null);
        uint startupFlag = MF_STRING | (_isStartupEnabled() ? MF_CHECKED : 0);
        AppendMenu(hMenu, startupFlag, new IntPtr(CMD_STARTUP), "Avvia con Windows");
        AppendMenu(hMenu, MF_SEPARATOR, IntPtr.Zero, null);
        AppendMenu(hMenu, MF_STRING, new IntPtr(CMD_EXIT), "Esci");

        SetForegroundWindow(hWnd);
        GetCursorPos(out var pt);

        int cmd = TrackPopupMenuEx(hMenu, TPM_RIGHTBUTTON | TPM_NONOTIFY | TPM_RETURNCMD,
                                   pt.x, pt.y, hWnd, IntPtr.Zero);
        DestroyMenu(hMenu);
        PostMessage(hWnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);

        switch (cmd)
        {
            case CMD_OPEN: _onOpen(); break;
            case CMD_STARTUP: _toggleStartup(); break;
            case CMD_EXIT: _onExit(); break;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Shell_NotifyIcon(NIM_DELETE, ref _nid);
        PostMessage(_hWnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
    }
}
