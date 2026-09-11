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
}
