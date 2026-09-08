using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ChanJing.Core.Services;

/// <summary>
/// 前台窗口采集服务：每 5 秒记录当前前台窗口的进程名与标题哈希。
/// 隐私友好：只存进程名 + 标题 SHA256 前 16 位，不落明文标题、无截图。
/// </summary>
public sealed class WindowActivityService : IDisposable
{
    public const int TickSeconds = 5;

    private readonly AppDatabase _db;
    private Timer? _timer;
    private bool _disposed;

    public WindowActivityService(AppDatabase db) => _db = db;

    public bool IsRunning => _timer is not null;

    public void Start()
    {
        if (_timer is not null) return;
        _timer = new Timer(Tick, null, TimeSpan.Zero, TimeSpan.FromSeconds(TickSeconds));
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void Tick(object? state)
    {
        try
        {
            var (processName, titleHash) = GetForegroundInfo();
            if (processName is null) return;
            _db.AddAppUsage(DateTime.Today.ToString("yyyy-MM-dd"), processName, titleHash, TickSeconds);
        }
        catch
        {
            // 采集失败静默，不影响主流程。
        }
    }

    /// <summary>获取前台窗口进程名与标题哈希。返回 (null, null) 表示无前台窗口（如锁屏）。</summary>
    private static (string? Process, string? TitleHash) GetForegroundInfo()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return (null, null);

        _ = GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return (null, null);

        string processName;
        try
        {
            processName = Process.GetProcessById((int)pid).ProcessName;
        }
        catch
        {
            return (null, null);
        }

        var title = new StringBuilder(512);
        _ = GetWindowText(hwnd, title, title.Capacity);
        var titleHash = title.Length > 0 ? HashTitle(title.ToString()) : null;

        return (processName, titleHash);
    }

    /// <summary>标题 SHA256 哈希，取前 16 位十六进制（防还原）。</summary>
    private static string HashTitle(string title)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(title));
        return Convert.ToHexString(bytes)[..16];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }

    // ---------- Win32 ----------

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
}
