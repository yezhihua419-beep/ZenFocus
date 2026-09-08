using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ChanJing.Core.Services;

/// <summary>
/// 前台窗口采集服务：每 5 秒记录当前前台窗口的进程名与标题哈希；
/// 同时按每日限额规则对窗口标题做域名匹配，累计使用并触发超限事件。
/// 隐私友好：只存进程名 + 标题 SHA256 前 16 位，不落明文标题、无截图。
/// </summary>
public sealed class WindowActivityService : IDisposable
{
    public const int TickSeconds = 5;

    private readonly AppDatabase _db;
    private readonly DailyLimitService _dailyLimits;
    private Timer? _timer;
    private bool _disposed;

    /// <summary>某域名达当日上限时触发（参数为域名）。线程：Timer 后台线程。</summary>
    public event Action<string>? LimitExceeded;

    public WindowActivityService(AppDatabase db, DailyLimitService dailyLimits)
    {
        _db = db;
        _dailyLimits = dailyLimits;
    }

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
            var info = GetForegroundInfo();
            if (info.ProcessName is null) return;

            _db.AddAppUsage(DateTime.Today.ToString("yyyy-MM-dd"), info.ProcessName, info.TitleHash, TickSeconds);

            // 每日限额：标题匹配域名 → 累计 → 超限事件（事件在后台线程，App 层自行调度）
            var matched = _dailyLimits.MatchDomains(info.TitleText);
            foreach (var domain in matched)
            {
                _dailyLimits.AddUsage(domain, TickSeconds);
                if (_dailyLimits.IsExceeded(domain))
                {
                    LimitExceeded?.Invoke(domain);
                }
            }
        }
        catch
        {
            // 采集失败静默，不影响主流程。
        }
    }

    /// <summary>前台窗口信息。ProcessName 为 null 表示无前台窗口（如锁屏）。</summary>
    private static WindowInfo GetForegroundInfo()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return default;

        _ = GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return default;

        string processName;
        try
        {
            processName = Process.GetProcessById((int)pid).ProcessName;
        }
        catch
        {
            return default;
        }

        var title = new StringBuilder(512);
        _ = GetWindowText(hwnd, title, title.Capacity);
        var text = title.ToString();
        return new WindowInfo(processName, text.Length > 0 ? HashTitle(text) : null, text);
    }

    /// <summary>标题 SHA256 哈希，取前 16 位十六进制（防还原）。</summary>
    private static string HashTitle(string title)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(title));
        return Convert.ToHexString(bytes)[..16];
    }

    private readonly record struct WindowInfo(string? ProcessName, string? TitleHash, string? TitleText);

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
