using ChanJing.Core.Services;

namespace ChanJing_App;

/// <summary>
/// 应用级服务单例。数据全本地，零上传、零截图。
/// </summary>
public static class AppServices
{
    /// <summary>
    /// 数据库路径。默认 %LOCALAPPDATA%\ChanJing\chanjing.db；
    /// 若命令行含 --db-path=xxx（提权重启时传入普通用户路径），则用指定路径，
    /// 确保管理员身份与普通身份读写同一个数据库，避免设置"丢失"。
    /// </summary>
    public static readonly string DbPath = ResolveDbPath();

    private static string ResolveDbPath()
    {
        foreach (var arg in Environment.GetCommandLineArgs())
        {
            if (arg.StartsWith("--db-path=", StringComparison.OrdinalIgnoreCase))
            {
                var path = arg["--db-path=".Length..];
                if (!string.IsNullOrWhiteSpace(path)) return path;
            }
        }
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChanJing", "chanjing.db");
    }

    public static readonly AppDatabase Db = new(DbPath);
    public static readonly FocusEngine Engine = new(Db);
    public static readonly BlocklistService Blocklist = new(Db);
    public static readonly DailyLimitService DailyLimits = new(Db);
    public static readonly WindowActivityService Activity = new(Db, DailyLimits, Engine, Blocklist);
    public static readonly FocusController Focus = new(Db, Engine, Blocklist);

    /// <summary>局域网伴侣页HTTP服务（App启动时初始化）。</summary>
    public static CompanionHttpServer? Companion { get; set; }

    /// <summary>当前选中的场景标签（跨页面共享：首页选中后，屏蔽页顶部显示当前场景摘要）。</summary>
    public static string? CurrentSceneTag
    {
        get => FocusContext.CurrentSceneTag;
        set => FocusContext.CurrentSceneTag = value;
    }

    /// <summary>当前待专注的愿望（跨路径共享：首页/托盘/伴侣页开始专注前设置，FocusStarted 时统一保存到场景）。</summary>
    public static string? CurrentWish
    {
        get => FocusContext.CurrentWish;
        set => FocusContext.CurrentWish = value;
    }

    /// <summary>当前待专注的时长（分钟）（跨路径共享）。</summary>
    public static int CurrentMinutes
    {
        get => FocusContext.CurrentMinutes;
        set => FocusContext.CurrentMinutes = value;
    }

    /// <summary>是否深度模式（不计时，手动结束）。</summary>
    public static bool DeepMode
    {
        get => FocusContext.DeepMode;
        set => FocusContext.DeepMode = value;
    }

    /// <summary>全局通知（MainWindow 的 InfoBar 承载）：消息 + 严重级别。</summary>
    public static Action<string, Microsoft.UI.Xaml.Controls.InfoBarSeverity>? NotifyHandler { get; set; }

    /// <summary>统一提示入口：页面/服务用此方法发消息，由主窗口 InfoBar 显示。</summary>
    public static void Notify(string message, Microsoft.UI.Xaml.Controls.InfoBarSeverity severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational)
    {
        try { NotifyHandler?.Invoke(message, severity); } catch { }
    }

    /// <summary>开启暂离/喘口气：桌面应用暂停拦截N分钟，到点自动恢复。托盘/首页/快捷键共用。</summary>
    private static Microsoft.UI.Dispatching.DispatcherQueueTimer? _restTimer;
    public static void StartRestBreak(int minutes)
    {
        try
        {
            var label = minutes <= 3 ? "喘口气" : "暂离模式";
            Blocklist.EmergencyPass = true;
            Notify($"{label}已开启 · 桌面应用暂停拦截 · {minutes}分钟后自动恢复", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
            _restTimer?.Stop();
            _restTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
            _restTimer.Interval = TimeSpan.FromMinutes(minutes);
            _restTimer.IsRepeating = false;
            _restTimer.Tick += (s, e) =>
            {
                _restTimer?.Stop();
                Blocklist.EmergencyPass = false;
                Notify($"{label}结束 · 桌面应用拦截已恢复", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational);
                App.LogAction($"{label}结束", "自动恢复桌面应用拦截");
            };
            _restTimer.Start();
            App.LogAction(label, $"桌面应用暂停拦截{minutes}分钟");
        }
        catch (Exception ex)
        {
            App.LogCrash("AppServices.StartRestBreak", ex);
        }
    }
}
