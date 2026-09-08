using ChanJing.Core.Services;

namespace ChanJing_App;

/// <summary>
/// 应用级服务单例。数据全本地，零上传、零截图。
/// </summary>
public static class AppServices
{
    public static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChanJing", "chanjing.db");

    public static readonly AppDatabase Db = new(DbPath);
    public static readonly FocusEngine Engine = new(Db);
    public static readonly BlocklistService Blocklist = new(Db);
    public static readonly DailyLimitService DailyLimits = new(Db);
    public static readonly WindowActivityService Activity = new(Db, DailyLimits);
}
