namespace ChanJing.Core.Models;

/// <summary>专注会话状态。</summary>
public enum FocusSessionState
{
    /// <summary>进行中。</summary>
    Running,

    /// <summary>按计划完成。</summary>
    Completed,

    /// <summary>提前结束（破功）。</summary>
    Broken
}

/// <summary>一次专注会话。</summary>
public sealed class FocusSession
{
    public int Id { get; set; }

    /// <summary>开始时间。</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>结束时间。</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>计划时长（分钟）。</summary>
    public int PlannedMinutes { get; set; }

    /// <summary>实际时长（分钟）。</summary>
    public int ActualMinutes { get; set; }

    /// <summary>会话状态。</summary>
    public FocusSessionState State { get; set; }

    /// <summary>今日一愿（可选）。</summary>
    public string? Wish { get; set; }

    /// <summary>会话期间切换到被屏蔽站点的次数（破功信号）。</summary>
    public int DistractionCount { get; set; }

    /// <summary>是否ADHD模式下的会话。</summary>
    public bool IsAdhd { get; set; }
}
