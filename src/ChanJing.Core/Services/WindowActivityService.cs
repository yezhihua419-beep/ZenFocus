using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ChanJing.Core.Services;

/// <summary>
/// 前台窗口采集服务：每 5 秒记录当前前台窗口的进程名与标题哈希（内存缓冲，每分钟落库）；
/// 每日限额累计与超限提醒（同域名每日只提醒一次）；
/// 专注中命中屏蔽站点 → 记分心 + 提醒（同域名每次专注只提醒一次）。
/// 桌面应用拦截：统一只最小化，杀进程由 UI 层在后台线程执行（避免阻塞 UI）。
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
    private readonly Dictionary<string, DateTime> _lastLimitBlockedAt = new();
    private string? _notifiedDistractionKey;
    private DateTime _notifiedDay = DateTime.Today;
    private int _codingStreak;
    private bool _codingNotified;

    /// <summary>IDE/编辑器识别名单（进程名，不区分大小写）。覆盖 VS Code/JetBrains/AI编程工具/其他编辑器。</summary>
    private static readonly HashSet<string> IdeProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Code", "Cursor", "Windsurf", "Trae", "CodeGeeX", "MarsCode", "tongyi-lingma",
        "idea64", "idea", "pycharm64", "pycharm", "webstorm64", "webstorm", "goland64", "goland",
        "clion64", "clion", "rider64", "rider", "datagrip64", "datagrip", "phpstorm64", "phpstorm",
        "rubymine64", "rubymine", "studio64", "studio", "devecostudio",
        "sublime_text", "nvim", "vim", "emacs", "gedit", "notepad++", "Notepad++",
        "Terminal", "wt", "WindowsTerminal", "powershell", "pwsh", "cmd",
        "git-bash", "Git Bash", "GitHubDesktop", "GitHub Desktop", "SourceTree", "sourcetree",
        "Docker Desktop", "docker", "Postman", "postman", "Insomnia", "insomnia",
        "DBeaver", "dbeaver", "Navicat", "navicat", "TablePlus", "tableplus",
        "Figma", "figma", "Sketch", "sketch", "Adobe XD", "XD",
    };

    /// <summary>某域名达当日上限时触发（后台线程）。</summary>
    public event Action<string>? LimitExceeded;

    /// <summary>每日限额超限后前台窗口被强制最小化（域名）。</summary>
    public event Action<string>? LimitBlocked;

    /// <summary>专注中被屏蔽站点分心时触发（后台线程）。</summary>
    public event Action<string>? DistractionDetected;

    /// <summary>屏蔽生效时前台命中分心桌面应用（进程名, 分类）→ 已自动最小化（后台线程）。</summary>
    public event Action<string, string>? AppBlocked;

    /// <summary>检测到用户持续在 IDE/编辑器中编码（≥2分钟）时触发（后台线程）。同一次编码会话只触发一次。</summary>
    public event Action<string>? CodingDetected;

    public WindowActivityService(AppDatabase db, DailyLimitService dailyLimits,
        FocusEngine engine, BlocklistService blocklist)
    {
        _db = db;
        _dailyLimits = dailyLimits;
        _engine = engine;
        _blocklist = blocklist;
        _engine.FocusStarted += OnFocusStarted;
    }

    /// <summary>立即生效：枚举所有已启用分类的进程并最小化（专注开始或手动屏蔽启用时调用）。</summary>
    public void ApplyShieldNow()
    {
        if (!_blocklist.CanInterceptApps()) return;
        try
        {
            foreach (var proc in _blocklist.GetActiveAppProcesses())
            {
                MinimizeProcessWindows(proc);
            }
            MinimizeStoreHostWindows(invokeBlocked: false);
        }
        catch { }
    }

    /// <summary>专注开始时立即最小化所有已启用分类的桌面应用。</summary>
    private void OnFocusStarted()
    {
        if (!_blocklist.CanInterceptApps()) return;
        try
        {
            var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                var name = p.ProcessName;
                if (BlocklistService.IsStoreHostProcess(name)) continue;
                if (processed.Contains(name)) continue;
                if (_blocklist.IsTempAllowed(name)) continue; // 临时放行的应用不拦截
                var cat = _blocklist.MatchBlockedApp(name);
                if (cat is not null)
                {
                    processed.Add(name);
                    MinimizeProcessWindows(name);
                    AppBlocked?.Invoke(name, cat);
                }
            }
            MinimizeStoreHostWindows(invokeBlocked: true);
        }
        catch { }
    }

    /// <summary>商店壳只最小化命中标题的那扇窗，不按进程名一锅端。</summary>
    private void MinimizeStoreHostWindows(bool invokeBlocked)
    {
        foreach (var p in Process.GetProcesses())
        {
            string name;
            string title;
            IntPtr hwnd;
            try
            {
                name = p.ProcessName;
                title = p.MainWindowTitle;
                hwnd = p.MainWindowHandle;
            }
            catch { continue; }
            if (!BlocklistService.IsStoreHostProcess(name) || hwnd == IntPtr.Zero) continue;
            if (_blocklist.IsTempAllowed(name, title)) continue;
            var cat = _blocklist.MatchBlockedApp(name, title);
            if (cat is null) continue;
            ShowWindow(hwnd, SW_MINIMIZE);
            if (!invokeBlocked) continue;
            var hits = _blocklist.MatchBlockedDomains(title);
            var target = hits.Count > 0 ? hits[0] : name;
            RememberBlockedTarget(target);
            if (_engine.IsRunning && hits.Count > 0) _engine.RegisterDistraction(hits[0]);
            AppBlocked?.Invoke(name, cat);
        }
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
            // 超限后强制阻断：前台（浏览器）窗口最小化 + 提醒，5 秒冷却（用户切回再被拦）
            foreach (var domain in _dailyLimits.MatchDomains(info.TitleText))
            {
                _dailyLimits.AddUsage(domain, TickSeconds);
                if (_dailyLimits.IsExceeded(domain))
                {
                    // 临时放行中：跳过提醒和强制最小化
                    if (_dailyLimits.IsTempAllowed(domain)) continue;
                    if (_notifiedLimits.Add(domain))
                    {
                        LimitExceeded?.Invoke(domain);
                    }
                    if (info.Hwnd != IntPtr.Zero)
                    {
                        lock (_lock)
                        {
                            var last = _lastLimitBlockedAt.GetValueOrDefault(domain);
                            if (DateTime.UtcNow - last > TimeSpan.FromSeconds(5))
                            {
                                _lastLimitBlockedAt[domain] = DateTime.UtcNow;
                                ShowWindow(info.Hwnd, SW_MINIMIZE);
                                LimitBlocked?.Invoke(domain);
                            }
                        }
                    }
                }
            }

            // 专注中命中屏蔽站点 → 记分心（同域名每次专注只记一次/提醒一次）。暂离时网站已放行，不再记。
            if (_engine.IsRunning && !_blocklist.EmergencyPass && info.TitleText is not null)
            {
                foreach (var domain in _blocklist.MatchBlockedDomains(info.TitleText))
                {
                    var key = $"{_engine.Current!.StartedAt:o}|{domain}";
                    if (_notifiedDistractionKey == key) continue;
                    _notifiedDistractionKey = key;
                    RememberBlockedTarget(domain);
                    _engine.RegisterDistraction(domain);
                    DistractionDetected?.Invoke(domain);
                }
            }

            // 桌面应用拦截：仅在专注中生效（屏蔽绑定专注）。前台命中分心 App → 最小化 + 触发AppBlocked事件。
            // 杀进程由 UI 层在后台线程执行（避免阻塞 UI 线程）。
            // 专注中持续拦截（2 秒冷却，最小化后用户再点回会再次拦截）。
            if ((_engine.IsRunning || _blocklist.IsManualShieldActive()) && _blocklist.CanInterceptApps() && info.Hwnd != IntPtr.Zero)
            {
                if (!_blocklist.IsTempAllowed(info.ProcessName, info.TitleText))
                {
                    var cat = _blocklist.MatchBlockedApp(info.ProcessName, info.TitleText);
                    if (cat is not null)
                    {
                        var host = BlocklistService.IsStoreHostProcess(info.ProcessName);
                        var cooldownKey = host ? "host:" + (info.TitleText ?? info.ProcessName) : info.ProcessName;
                        lock (_lock)
                        {
                            var last = _lastAppBlockedAt.GetValueOrDefault(cooldownKey);
                            if (DateTime.UtcNow - last > TimeSpan.FromSeconds(2))
                            {
                                _lastAppBlockedAt[cooldownKey] = DateTime.UtcNow;
                                if (host)
                                    ShowWindow(info.Hwnd, SW_MINIMIZE);
                                else
                                    MinimizeProcessWindows(info.ProcessName);
                                var hits = host ? _blocklist.MatchBlockedDomains(info.TitleText) : Array.Empty<string>();
                                var target = hits.Count > 0 ? hits[0] : info.ProcessName;
                                RememberBlockedTarget(target);
                                if (_engine.IsRunning) _engine.RegisterDistraction(target);
                                AppBlocked?.Invoke(info.ProcessName, cat);
                            }
                        }
                    }
                }
            }

            // IDE/编辑器检测：持续在 IDE 前台 ≥2分钟（24个tick）→ 触发 CodingDetected 事件。
            // 同一次编码会话只提醒一次；IDE 不再前台时重置计数器。
            if (!_engine.IsRunning && IdeProcessNames.Contains(info.ProcessName))
            {
                _codingStreak++;
                if (_codingStreak >= 24 && !_codingNotified)
                {
                    _codingNotified = true;
                    CodingDetected?.Invoke(info.ProcessName);
                }
            }
            else
            {
                _codingStreak = 0;
                _codingNotified = false;
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

    /// <summary>最近一次分心目标（域名或进程名）。点「放行此站点」时前台已是禅净，不能只读当前窗口。</summary>
    public string? LastBlockedTarget { get; private set; }

    /// <summary>记录分心目标（标题命中或桌面 App 拦截时调用）。</summary>
    public void RememberBlockedTarget(string target)
    {
        if (!string.IsNullOrWhiteSpace(target))
            LastBlockedTarget = target;
    }

    /// <summary>当前前台窗口标题命中的第一个屏蔽域名（供专注页快捷放行）。未命中返回 null。</summary>
    public string? GetCurrentBlockedDomain()
    {
        var info = GetForegroundInfo();
        if (info.TitleText is null) return null;
        var hits = _blocklist.MatchBlockedDomains(info.TitleText);
        return hits.Count > 0 ? hits[0] : null;
    }

    /// <summary>可放行目标：优先最近一次分心，其次当前前台标题。</summary>
    public string? GetAllowableTarget()
    {
        if (!string.IsNullOrWhiteSpace(LastBlockedTarget))
        {
            if (_blocklist.MatchBlockedApp(LastBlockedTarget) is not null)
                return LastBlockedTarget;
            if (_blocklist.GetActiveDomains().Any(d =>
                    string.Equals(d, LastBlockedTarget, StringComparison.OrdinalIgnoreCase)))
                return LastBlockedTarget;
        }
        return GetCurrentBlockedDomain();
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

    /// <summary>最小化指定进程名的所有主窗口（直接用Process.MainWindowHandle，对Electron应用如抖音可靠——EnumWindows找不到其窗口）。</summary>
    private static void MinimizeProcessWindows(string processName)
    {
        try
        {
            foreach (var p in Process.GetProcessesByName(processName))
            {
                if (p.MainWindowHandle != IntPtr.Zero)
                {
                    ShowWindow(p.MainWindowHandle, SW_MINIMIZE);
                }
            }
        }
        catch { }
    }
}
