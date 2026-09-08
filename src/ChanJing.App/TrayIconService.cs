using System.Runtime.InteropServices;

namespace ChanJing_App;

/// <summary>
/// 系统托盘图标服务（纯 Win32 Shell_NotifyIcon 实现，无第三方依赖）。
/// 功能：托盘图标、双击恢复主窗口、右键菜单（打开/退出）。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint WM_APP = 0x8000;
    private const uint WM_TRAYICON = WM_APP + 1;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_COMMAND = 0x0111;
    private const int ID_OPEN = 1;
    private const int ID_EXIT = 2;
    private const int MAX_TIP_LENGTH = 127;

    private readonly Action _onOpen;
    private readonly Action _onExit;
    private IntPtr _hwnd;
    private IntPtr _icon;
    private WndProcDelegate? _wndProcDelegate; // 持有委托防止被 GC 回收

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public TrayIconService(Action onOpen, Action onExit)
    {
        _onOpen = onOpen;
        _onExit = onExit;
    }

    public void Show(string tooltip)
    {
        _wndProcDelegate = WndProc;
        var hInstance = GetModuleHandle(null);

        var wc = new WNDCLASS
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = hInstance,
            lpszClassName = "ChanJingTrayWindow"
        };
        _ = RegisterClass(ref wc);

        _hwnd = CreateWindowEx(0, "ChanJingTrayWindow", "禅净托盘", 0, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        // IDI_APPLICATION 占位，后续换品牌图标。
        _icon = LoadIcon(IntPtr.Zero, new IntPtr(32512));

        var nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 0,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            hIcon = _icon,
            uCallbackMessage = WM_TRAYICON
        };
        nid.szTip = tooltip.Length > MAX_TIP_LENGTH ? tooltip[..MAX_TIP_LENGTH] : tooltip;
        _ = Shell_NotifyIcon(NIM_ADD, ref nid);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAYICON)
        {
            var evt = (uint)(lParam.ToInt64() & 0xFFFF);
            if (evt == WM_LBUTTONDBLCLK)
            {
                _onOpen();
                return IntPtr.Zero;
            }
            if (evt == WM_RBUTTONUP)
            {
                ShowMenu();
                return IntPtr.Zero;
            }
        }
        else if (msg == WM_COMMAND)
        {
            var id = wParam.ToInt32() & 0xFFFF;
            if (id == ID_OPEN) _onOpen();
            else if (id == ID_EXIT) _onExit();
            return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        _ = AppendMenu(menu, 0, ID_OPEN, "打开禅净");
        _ = AppendMenu(menu, 0, ID_EXIT, "退出");

        _ = GetCursorPos(out var pt);
        _ = SetForegroundWindow(_hwnd);
        _ = TrackPopupMenu(menu, 0, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
        _ = DestroyMenu(menu);
    }

    public void Dispose()
    {
        var nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 0
        };
        _ = Shell_NotifyIcon(NIM_DELETE, ref nid);
        if (_hwnd != IntPtr.Zero) _ = DestroyWindow(_hwnd);
        if (_icon != IntPtr.Zero) _ = DestroyIcon(_icon);
    }

    // ---------- Win32 ----------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("shell32.dll")]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, int uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
