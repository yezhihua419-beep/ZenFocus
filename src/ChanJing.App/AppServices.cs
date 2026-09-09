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
}
