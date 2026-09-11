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
        return _engine.IsRunning ? Stop() : Start();
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
                _blocklist.SetEnabledCategories(sceneConfig.Categories);
                _blocklist.Apply(); // 写 hosts.pre，专注开始时同步到系统 hosts
                FocusContext.CurrentWish = sceneConfig.Wish;
                var startMinutes = FocusContext.DeepMode ? 0 : sceneConfig.Minutes;
                FocusContext.CurrentMinutes = startMinutes;
                _engine.Start(sceneConfig.Wish, startMinutes);
                return $"开始专注 {sceneConfig.Minutes}分钟（场景 {sceneTag}）";
            }
        }

        // 未选场景或场景配置无效，用默认配置
        // 关键：即使未选场景，也要调用Apply()确保hosts.pre存在，
        // 否则IsApplied()返回false，OnFocusStarted和前台轮询都不工作，抖音不会被屏蔽
        _blocklist.Apply();
        FocusContext.CurrentWish = "专注";
        var defaultMinutes = FocusContext.DeepMode ? 0 : 25;
        FocusContext.CurrentMinutes = defaultMinutes;
        _engine.Start("专注", defaultMinutes);
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
}
