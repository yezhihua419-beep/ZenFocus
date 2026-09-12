namespace ChanJing.Core.Services;

/// <summary>
/// 专注控制器：统一管理"开始/结束/切换专注"的业务逻辑。
/// 所有UI入口（首页按钮、快捷键、托盘菜单、伴侣页API）都应通过此控制器操作，
/// 避免逻辑散落在多处导致改漏（如快捷键调了Pause而不是Finish）。
/// 纯逻辑，不依赖UI，可单元测试。
/// </summary>
public class FocusController
{
    private readonly AppDatabase _db;
    private readonly FocusEngine _engine;
    private readonly BlocklistService _blocklist;

    public FocusController(AppDatabase db, FocusEngine engine, BlocklistService blocklist)
    {
        _db = db;
        _engine = engine;
        _blocklist = blocklist;
    }

    /// <summary>当前是否在专注中。</summary>
    public bool IsRunning => _engine.IsRunning;

    /// <summary>
    /// 切换专注状态：运行中=结束，未运行=开始。
    /// 返回操作描述（用于日志/通知）。
    /// </summary>
    public string Toggle()
    {
        // 托盘/快捷键没有「圆满/放下」选择，一律提前结束，避免把连续天数刷高
        return _engine.IsRunning ? StopEarly() : Start();
    }

    /// <summary>
    /// 开始专注，用当前场景配置（愿望+时长+屏蔽分类）。
    /// 若未选场景，用默认配置（25分钟，愿望"专注"）。
    /// 返回操作描述。
    /// </summary>
    public string Start()
    {
        var sceneTag = FocusContext.CurrentSceneTag;

        if (!string.IsNullOrEmpty(sceneTag))
        {
            var sceneConfig = SceneManager.GetSceneConfig(_db, sceneTag);
            if (!string.IsNullOrEmpty(sceneConfig.Wish))
            {
                // 分类以屏蔽页为准，场景只提供愿望/时长；空名单才按场景默认补一次
                _blocklist.SeedCategoriesIfEmpty(SceneManager.GetDefaultCategories(sceneTag));
                _blocklist.Apply();
                var wish = FocusContext.ResolveWish?.Invoke(sceneTag, sceneConfig.Wish) ?? sceneConfig.Wish;
                FocusContext.CurrentWish = wish;
                var startMinutes = FocusContext.DeepMode ? 0
                    : FocusContext.AdhdMode ? 15
                    : sceneConfig.Minutes;
                FocusContext.CurrentMinutes = startMinutes;
                _engine.Start(wish, startMinutes, isAdhd: FocusContext.AdhdMode && !FocusContext.DeepMode);
                return $"开始专注 {sceneConfig.Minutes}分钟（场景 {sceneTag}）";
            }
        }

        // 未选场景或场景配置无效，用默认配置
        // 关键：即使未选场景，也要调用Apply()确保hosts.pre存在，
        // 否则IsApplied()返回false，OnFocusStarted和前台轮询都不工作，抖音不会被屏蔽
        _blocklist.SeedCategoriesIfEmpty(SceneManager.GetDefaultCategories(FocusContext.CurrentSceneTag ?? "work"));
        _blocklist.Apply();
        var fallback = FocusContext.ResolveWish?.Invoke(null, "专注") ?? "专注";
        FocusContext.CurrentWish = fallback;
        var defaultMinutes = FocusContext.DeepMode ? 0 : (FocusContext.AdhdMode ? 15 : 25);
        FocusContext.CurrentMinutes = defaultMinutes;
        _engine.Start(fallback, defaultMinutes, isAdhd: FocusContext.AdhdMode && !FocusContext.DeepMode);
        return $"开始专注{defaultMinutes}分钟（未选场景）";
    }

    /// <summary>
    /// 结束专注（圆满结束）。重置暂离模式。
    /// 返回操作描述。
    /// </summary>
    public string Stop()
    {
        var done = _engine.Finish(completed: true);
        _blocklist.EmergencyPass = false;
        return $"结束专注 {done.ActualMinutes}分钟 分心{done.DistractionCount}次";
    }

    /// <summary>
    /// 提前结束专注（破功/伴侣远程喊停）。重置暂离模式。
    /// 返回操作描述。
    /// </summary>
    public string StopEarly()
    {
        var done = _engine.Finish(completed: false);
        _blocklist.EmergencyPass = false;
        return $"提前结束专注 {done.ActualMinutes}分钟 分心{done.DistractionCount}次";
    }
}
