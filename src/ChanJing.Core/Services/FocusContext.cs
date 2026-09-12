namespace ChanJing.Core.Services;

/// <summary>
/// 专注上下文：跨页面/跨入口共享的专注状态。
/// 从AppServices抽出来，让Core项目的纯逻辑类（如FocusController）也能访问，
/// 避免循环依赖（Core不能引用App项目）。
/// </summary>
public static class FocusContext
{
    /// <summary>当前选中的场景标签（work/write/study/meeting）。</summary>
    public static string? CurrentSceneTag { get; set; }

    /// <summary>当前待专注的愿望。</summary>
    public static string? CurrentWish { get; set; }

    /// <summary>当前待专注的时长（分钟）。</summary>
    public static int CurrentMinutes { get; set; } = 25;

    /// <summary>是否深度模式（不计时，手动结束）。</summary>
    public static bool DeepMode { get; set; }

    /// <summary>是否 ADHD 友好模式（短周期）。托盘/快捷键/伴侣与首页共用。</summary>
    public static bool AdhdMode { get; set; }

    /// <summary>App 层注入：把存库愿望译成当前语言。未注入则原样返回。</summary>
    public static Func<string?, string?, string>? ResolveWish { get; set; }

    public const string SettingKeyScene = "ui_scene_tag";
    public const string SettingKeyDeep = "ui_deep_mode";
    public const string SettingKeyAdhd = "ui_adhd_mode";

    /// <summary>启动后从 Settings 恢复。禁止在 App 构造函数里调。</summary>
    public static void Load(AppDatabase db)
    {
        var tag = db.GetSetting(SettingKeyScene);
        CurrentSceneTag = tag is "work" or "write" or "study" or "meeting" ? tag : null;
        DeepMode = db.GetSetting(SettingKeyDeep) == "true";
        AdhdMode = db.GetSetting(SettingKeyAdhd) == "true";
        if (DeepMode && AdhdMode) AdhdMode = false;
    }

    /// <summary>把当前场景/模式写入 Settings。</summary>
    public static void Save(AppDatabase db)
    {
        db.SetSetting(SettingKeyScene, CurrentSceneTag ?? "");
        db.SetSetting(SettingKeyDeep, DeepMode ? "true" : "false");
        db.SetSetting(SettingKeyAdhd, AdhdMode ? "true" : "false");
    }
}
