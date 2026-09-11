using System.Text;
using ChanJing.Core.Models;

namespace ChanJing.Core.Services;

/// <summary>
/// 专注引擎：正计时、无倒计时、可暂停、破功记录与即时反馈。
/// 计时用单调时钟（Environment.TickCount64），不受系统时间调整影响。
/// 引擎只维护状态与时间戳，UI 层用 DispatcherTimer 每秒刷新显示。
/// </summary>
public sealed class FocusEngine
{
    private readonly AppDatabase _db;
    private long _startedTicks;
    private long _pausedMs;
    private long? _pauseStartTicks;
    private int _distractionCount;

    public FocusEngine(AppDatabase db) => _db = db;

    /// <summary>专注开始事件（屏蔽绑定专注：开始时应用屏蔽）。</summary>
    public event Action? FocusStarted;

    /// <summary>专注结束事件（屏蔽绑定专注：结束时解除屏蔽）。参数：是否完成。</summary>
    public event Action<bool>? FocusFinished;

    /// <summary>当前进行中的会话；null 表示空闲。</summary>
    public FocusSession? Current { get; private set; }

    public bool IsRunning => Current is not null;

    /// <summary>是否处于暂停状态。</summary>
    public bool IsPaused => _pauseStartTicks.HasValue;

    /// <summary>已专注时长（正计时，扣除暂停）。</summary>
    public TimeSpan Elapsed
    {
        get
        {
            if (!IsRunning) return TimeSpan.Zero;
            var total = Environment.TickCount64 - _startedTicks - _pausedMs;
            return TimeSpan.FromMilliseconds(Math.Max(0, total));
        }
    }

    /// <summary>开始一次专注（默认 25 分钟，正计时）。</summary>
    public void Start(string? wish, int plannedMinutes = 25, bool isAdhd = false)
    {
        if (plannedMinutes < 0) plannedMinutes = 25; // 0 表示深度模式（不计时，手动结束）
        Current = new FocusSession
        {
            StartedAt = DateTime.Now,
            PlannedMinutes = plannedMinutes,
            State = FocusSessionState.Running,
            Wish = string.IsNullOrWhiteSpace(wish) ? null : wish.Trim(),
            IsAdhd = isAdhd
        };
        _startedTicks = Environment.TickCount64;
        _pausedMs = 0;
        _pauseStartTicks = null;
        _distractionCount = 0;
        FocusStarted?.Invoke();
    }

    /// <summary>暂停计时（如临时离开），暂停期间不计入专注时长。</summary>
    public void Pause()
    {
        if (IsRunning && !IsPaused)
        {
            _pauseStartTicks = Environment.TickCount64;
        }
    }

    /// <summary>恢复计时。</summary>
    public void Resume()
    {
        if (IsPaused)
        {
            _pausedMs += Environment.TickCount64 - _pauseStartTicks!.Value;
            _pauseStartTicks = null;
        }
    }

    /// <summary>记录一次分心信号（如访问被屏蔽站点）。同源去重由调用方负责。</summary>
    public void RegisterDistraction()
    {
        if (IsRunning) _distractionCount++;
    }

    /// <summary>结束当前专注（completed=true 按计划完成，false 为破功）。返回落库的会话。</summary>
    public FocusSession Finish(bool completed)
    {
        if (Current is null)
        {
            throw new InvalidOperationException("当前没有进行中的专注会话。");
        }

        Current.EndedAt = DateTime.Now;
        Current.ActualMinutes = Math.Max(1, (int)Math.Round(Elapsed.TotalMinutes));
        Current.State = completed ? FocusSessionState.Completed : FocusSessionState.Broken;
        Current.DistractionCount = _distractionCount;

        var done = Current;
        _db.SaveFocusSession(done);
        // 先触发事件（此时Current还在，事件处理可访问Current.IsAdhd等信息）
        FocusFinished?.Invoke(completed);
        Current = null;
        _pauseStartTicks = null;
        return done;
    }

    /// <summary>今日已完成专注总分钟数（含当前进行中的会话）。</summary>
    public int GetTodayTotalMinutes()
    {
        var sessions = _db.GetSessions(DateTime.Today, DateTime.Today.AddDays(1));
        var total = sessions.Sum(s => s.ActualMinutes);
        if (IsRunning && !IsPaused)
        {
            total += (int)Math.Round(Elapsed.TotalMinutes);
        }
        return total;
    }

    /// <summary>
    /// 生成专注后即时反馈（1-2 句人话，进步框架，不审判）。
    /// 纯规则模板，不需要 AI。
    /// </summary>
    public string GenerateFeedback(FocusSession session)
    {
        var sb = new StringBuilder();
        sb.Append(session.State == FocusSessionState.Completed
            ? $"心已定。本次定心 {session.ActualMinutes} 分钟"
            : $"发生了什么？本次定心 {session.ActualMinutes} 分钟");

        if (session.DistractionCount == 0)
        {
            sb.Append("，全程未切换，滴水穿石。");
        }
        else
        {
            sb.Append(session.DistractionCount == 1
                ? $"，中途分心 1 次——下次试试把手机放远一点。"
                : $"，中途分心 {session.DistractionCount} 次——每一次觉察，都是一次练习。");
        }

        // 与昨日对比（进步框架）
        var yesterday = DateTime.Today.AddDays(-1);
        var yesterdaySessions = _db.GetSessions(yesterday, DateTime.Today);
        if (yesterdaySessions.Count > 0)
        {
            var yesterdayAvg = (int)Math.Round(yesterdaySessions.Average(s => s.ActualMinutes));
            var diff = session.ActualMinutes - yesterdayAvg;
            if (diff > 0)
            {
                sb.Append($" 比昨日平均多 {diff} 分钟，心已更进一步。");
            }
            else if (diff == 0)
            {
                sb.Append(" 与昨日平均持平，稳即是进。");
            }
        }

        return sb.ToString();
    }
}
