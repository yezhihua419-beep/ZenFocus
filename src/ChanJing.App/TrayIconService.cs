using System.Runtime.InteropServices;

namespace ChanJing_App;

/// <summary>
/// 系统托盘图标服务（纯 Win32 Shell_NotifyIcon 实现，无第三方依赖）。
/// 功能：托盘图标、双击恢复主窗口、右键菜单（打开/退出）。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;
    private const uint WM_APP = 0x8000;
    private const uint WM_TRAYICON = WM_APP + 1;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_COMMAND = 0x0111;
    private const int ID_OPEN = 1;
    private const int ID_EXIT = 2;
    private const int ID_TOGGLE_FOCUS = 3;
    private const int ID_TOGGLE_SHIELD = 4;
    private const int MAX_TIP_LENGTH = 127;
    private const uint MF_SEPARATOR = 0x00000800;
    private const uint MF_GRAYED = 0x00000001;

    private readonly Action _onOpen;
    private readonly Action _onExit;
    private readonly Action? _onToggleFocus;
    private readonly Action? _onToggleShield;
    private IntPtr _hwnd;
    private IntPtr _icon;
    private NOTIFYICONDATA _nid;
    private WndProcDelegate? _wndProcDelegate;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public TrayIconService(Action onOpen, Action onExit, Action? onToggleFocus = null, Action? onToggleShield = null)
    {
        _onOpen = onOpen;
        _onExit = onExit;
        _onToggleFocus = onToggleFocus;
        _onToggleShield = onToggleShield;
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
        var reg = RegisterClass(ref wc);
        App.LogAction("托盘窗口类", $"RegisterClass={reg} err={Marshal.GetLastWin32Error()}");

        _hwnd = CreateWindowEx(0, "ChanJingTrayWindow", "禅净托盘", 0, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        App.LogAction("托盘窗口", $"CreateWindowEx={_hwnd} err={Marshal.GetLastWin32Error()}");

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
        _nid = nid;
        var added = Shell_NotifyIcon(NIM_ADD, ref _nid);
        App.LogAction("托盘图标", $"NIM_ADD={added} hwnd={_hwnd} hIcon={_icon}");
        if (!added)
        {
            // 重试一次（Explorer 偶发未就绪）
            _ = Shell_NotifyIcon(NIM_DELETE, ref _nid);
            _ = Shell_NotifyIcon(NIM_ADD, ref _nid);
            App.LogAction("托盘图标", $"NIM_ADD 重试后={Shell_NotifyIcon(NIM_ADD, ref _nid)}");
        }
    }

    /// <summary>托盘气泡通知（如每日限额提醒）。</summary>
    public void ShowBalloon(string text, string title)
    {
        if (_hwnd == IntPtr.Zero) return;
        _nid.uFlags |= NIF_INFO;
        _nid.szInfo = text.Length > 255 ? text[..255] : text;
        _nid.szInfoTitle = title.Length > 63 ? title[..63] : title;
        _nid.dwInfoFlags = 0;
        _ = Shell_NotifyIcon(NIM_MODIFY, ref _nid);
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
            else if (id == ID_TOGGLE_FOCUS) _onToggleFocus?.Invoke();
            else if (id == ID_TOGGLE_SHIELD) _onToggleShield?.Invoke();
            return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();

        // 标题（不可点击）
        _ = AppendMenu(menu, MF_GRAYED, 0, "禅净");
        _ = AppendMenu(menu, MF_SEPARATOR, 0, "");

        // 专注状态
        var isFocusing = AppServices.Engine.IsRunning;
        var todayMinutes = AppServices.Engine.GetTodayTotalMinutes();
        _ = AppendMenu(menu, MF_GRAYED, 0, isFocusing ? $"专注中 · 今日 {todayMinutes} 分钟" : $"空闲 · 今日 {todayMinutes} 分钟");
        _ = AppendMenu(menu, 0, ID_TOGGLE_FOCUS, isFocusing ? "暂停专注" : "开始专注");
        _ = AppendMenu(menu, MF_SEPARATOR, 0, "");

        // 屏蔽状态
        var isShieldOn = AppServices.Blocklist.IsApplied();
        _ = AppendMenu(menu, MF_GRAYED, 0, isShieldOn ? "屏蔽：已开启" : "屏蔽：已关闭");
        _ = AppendMenu(menu, 0, ID_TOGGLE_SHIELD, isShieldOn ? "关闭屏蔽" : "开启屏蔽");
        _ = AppendMenu(menu, MF_SEPARATOR, 0, "");

        // 打开/退出
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
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
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
