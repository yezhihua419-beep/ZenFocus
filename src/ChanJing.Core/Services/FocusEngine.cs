using System.Text;
using ChanJing.Core.Models;

namespace ChanJing.Core.Services;

/// <summary>
/// 专注引擎：正计时、无倒计时、破功记录与即时反馈。
/// 引擎只维护状态与时间戳，UI 层用 DispatcherTimer 每秒刷新显示，避免线程问题。
/// </summary>
public sealed class FocusEngine
{
    private readonly AppDatabase _db;
    private DateTime _startedUtc;
    private int _distractionCount;

    public FocusEngine(AppDatabase db) => _db = db;

    /// <summary>当前进行中的会话；null 表示空闲。</summary>
    public FocusSession? Current { get; private set; }

    public bool IsRunning => Current is not null;

    /// <summary>已专注时长（正计时）。</summary>
    public TimeSpan Elapsed => DateTime.UtcNow - _startedUtc;

    /// <summary>开始一次专注（默认 25 分钟，正计时）。</summary>
    public void Start(string? wish, int plannedMinutes = 25)
    {
        if (plannedMinutes <= 0) plannedMinutes = 25;
        Current = new FocusSession
        {
            StartedAt = DateTime.Now,
            PlannedMinutes = plannedMinutes,
            State = FocusSessionState.Running,
            Wish = string.IsNullOrWhiteSpace(wish) ? null : wish.Trim()
        };
        _startedUtc = DateTime.UtcNow;
        _distractionCount = 0;
    }

    /// <summary>记录一次分心信号（如访问被屏蔽站点）。</summary>
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
        Current = null;
        return done;
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
