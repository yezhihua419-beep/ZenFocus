using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ChanJing.Core.Services;

/// <summary>
/// 前台窗口采集服务：每 5 秒记录当前前台窗口的进程名与标题哈希（内存缓冲，每分钟落库）；
/// 每日限额累计与超限提醒（同域名每日只提醒一次）；
/// 专注中命中屏蔽站点 → 记分心 + 提醒（同域名每次专注只提醒一次）。
/// 隐私友好：只存进程名 + 标题 SHA256 前 16 位，不落明文标题、无截图。
/// </summary>
public sealed class WindowActivityService : IDisposable
{
    public const int TickSeconds = 5;
    private const int FlushEveryTicks = 12; // 60 秒落库一次

    private readonly AppDatabase _db;
    private readonly DailyLimitService _dailyLimits;
    private readonly FocusEngine _engine;
    private readonly BlocklistService _blocklist;
    private readonly object _lock = new();

    private Timer? _timer;
    private bool _disposed;
    private int _tickCount;
    private readonly Dictionary<string, int> _buffer = new();
    private readonly HashSet<string> _notifiedLimits = new();
    private readonly Dictionary<string, DateTime> _lastAppBlockedAt = new();
    private string? _notifiedDistractionKey;
    private DateTime _notifiedDay = DateTime.Today;

    /// <summary>某域名达当日上限时触发（后台线程）。</summary>
    public event Action<string>? LimitExceeded;

    /// <summary>专注中被屏蔽站点分心时触发（后台线程）。</summary>
    public event Action<string>? DistractionDetected;

    /// <summary>屏蔽生效时前台命中分心桌面应用（进程名, 分类）→ 已自动最小化（后台线程）。</summary>
    public event Action<string, string>? AppBlocked;

    public WindowActivityService(AppDatabase db, DailyLimitService dailyLimits,
        FocusEngine engine, BlocklistService blocklist)
    {
        _db = db;
        _dailyLimits = dailyLimits;
        _engine = engine;
        _blocklist = blocklist;
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
        FlushBuffer();
    }

    private void Tick(object? state)
    {
        try
        {
            // 跨天重置提醒去重
            if (DateTime.Today != _notifiedDay)
            {
                _notifiedDay = DateTime.Today;
                _notifiedLimits.Clear();
                _notifiedDistractionKey = null;
            }

            _blocklist.RemoveExpiredTempAllows();

            var info = GetForegroundInfo();
            if (info.ProcessName is null) return;

            // 缓冲采集，每 60 秒批量落库（降低写频次与锁竞争）
            lock (_lock)
            {
                var key = $"{DateTime.Today:yyyy-MM-dd}|{info.ProcessName}|{info.TitleHash}";
                _buffer[key] = _buffer.GetValueOrDefault(key) + TickSeconds;
            }
            if (++_tickCount % FlushEveryTicks == 0)
            {
                FlushBuffer();
            }

            // 每日限额：匹配标题 → 累计 → 超限（每日只提醒一次）
            foreach (var domain in _dailyLimits.MatchDomains(info.TitleText))
            {
                _dailyLimits.AddUsage(domain, TickSeconds);
                if (_dailyLimits.IsExceeded(domain) && _notifiedLimits.Add(domain))
                {
                    LimitExceeded?.Invoke(domain);
                }
            }

            // 专注中命中屏蔽站点 → 记分心（同域名每次专注只记一次/提醒一次）
            if (_engine.IsRunning && info.TitleText is not null)
            {
                foreach (var domain in _blocklist.MatchBlockedDomains(info.TitleText))
                {
                    var key = $"{_engine.Current!.StartedAt:o}|{domain}";
                    if (_notifiedDistractionKey == key) continue;
                    _notifiedDistractionKey = key;
                    _engine.RegisterDistraction();
                    DistractionDetected?.Invoke(domain);
                }
            }

            // 桌面应用拦截：屏蔽已生效时，前台命中分心 App → 最小化/结束进程 + 提醒。
            // 专注中持续拦截（2 秒冷却，最小化后用户再点回会再次拦截）；非专注 10 秒冷却。
            if (_blocklist.IsApplied() && info.Hwnd != IntPtr.Zero)
            {
                var cat = _blocklist.MatchBlockedApp(info.ProcessName);
                if (cat is not null)
                {
                    lock (_lock)
                    {
                        var cooldown = _engine.IsRunning ? 2 : 10;
                        var last = _lastAppBlockedAt.GetValueOrDefault(info.ProcessName);
                        if (DateTime.UtcNow - last > TimeSpan.FromSeconds(cooldown))
                        {
                            _lastAppBlockedAt[info.ProcessName] = DateTime.UtcNow;
                            if (_blocklist.GetAppBlockMode() == "kill" && info.Pid > 0)
                            {
                                try
                                {
                                    Process.GetProcessById(info.Pid).Kill();
                                }
                                catch
                                {
                                    ShowWindow(info.Hwnd, SW_MINIMIZE);
                                }
                            }
                            else
                            {
                                ShowWindow(info.Hwnd, SW_MINIMIZE);
                            }
                            AppBlocked?.Invoke(info.ProcessName, cat);
                        }
                    }
                }
            }
        }
        catch
        {
            // 采集失败静默，不影响主流程。
        }
    }

    private void FlushBuffer()
    {
        List<(string Day, string Process, string? Hash, int Seconds)> batch;
        lock (_lock)
        {
            if (_buffer.Count == 0) return;
            batch = _buffer
                .Select(kv =>
                {
                    var parts = kv.Key.Split('|');
                    return (parts[0], parts[1], parts.Length > 2 ? parts[2] : (string?)null, kv.Value);
                })
                .ToList();
            _buffer.Clear();
        }

        foreach (var item in batch)
        {
            try
            {
                _db.AddAppUsage(item.Day, item.Process, item.Hash, item.Seconds);
            }
            catch
            {
                // 落库失败静默，下轮重新采集
            }
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
        return new WindowInfo(processName, text.Length > 0 ? HashTitle(text) : null, text, hwnd, (int)pid);
    }

    /// <summary>标题 SHA256 哈希，取前 16 位十六进制（防还原）。</summary>
    private static string HashTitle(string title)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(title));
        return Convert.ToHexString(bytes)[..16];
    }

    private readonly record struct WindowInfo(string? ProcessName, string? TitleHash, string? TitleText, IntPtr Hwnd, int Pid);

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

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_MINIMIZE = 6;
}
