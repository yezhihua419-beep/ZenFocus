using ChanJing.Core.Services;
using Xunit;

namespace ChanJing.Tests;

/// <summary>FocusEngine 专注开始/结束事件测试（屏蔽绑定专注的核心事件）。</summary>
[Collection("Hosts")]
public class FocusEngineEventTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppDatabase _db;
    private readonly FocusEngine _engine;

    public FocusEngineEventTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "chanjing-focus-event-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _db = new AppDatabase(Path.Combine(_tempDir, "test.db"));
        _engine = new FocusEngine(_db);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* 忽略 */ }
    }

    [Fact]
    public void Start_TriggersFocusStartedEvent()
    {
        var triggered = false;
        _engine.FocusStarted += () => triggered = true;

        _engine.Start("测试愿望", 25);

        Assert.True(triggered);
        Assert.True(_engine.IsRunning);
    }

    [Fact]
    public void Finish_TriggersFocusFinishedEvent_WithCompletedTrue()
    {
        var completedResult = false;
        var triggered = false;
        _engine.FocusFinished += (completed) =>
        {
            triggered = true;
            completedResult = completed;
        };

        _engine.Start("测试", 25);
        _engine.Finish(completed: true);

        Assert.True(triggered);
        Assert.True(completedResult);
        Assert.False(_engine.IsRunning);
    }

    [Fact]
    public void Finish_TriggersFocusFinishedEvent_WithCompletedFalse()
    {
        var completedResult = true;
        _engine.FocusFinished += (completed) => completedResult = completed;

        _engine.Start("测试", 25);
        _engine.Finish(completed: false);

        Assert.False(completedResult);
    }

    [Fact]
    public void Start_MultipleTimes_TriggersEventEachTime()
    {
        var count = 0;
        _engine.FocusStarted += () => count++;

        _engine.Start("第一次", 25);
        _engine.Finish(completed: true);
        _engine.Start("第二次", 25);
        _engine.Finish(completed: true);

        Assert.Equal(2, count);
    }

    [Fact]
    public void Finish_WithoutStart_ThrowsException()
    {
        Assert.Throws<InvalidOperationException>(() => _engine.Finish(completed: true));
    }

    [Fact]
    public void Start_SetsCurrentSessionWithWishAndMinutes()
    {
        _engine.Start("测试愿望", 50);

        Assert.NotNull(_engine.Current);
        Assert.Equal("测试愿望", _engine.Current!.Wish);
        Assert.Equal(50, _engine.Current.PlannedMinutes);
    }

    [Fact]
    public void Finish_SavesSessionToDatabase()
    {
        _engine.Start("测试", 25);
        var session = _engine.Finish(completed: true);

        var sessions = _db.GetSessions(DateTime.Today, DateTime.Today.AddDays(1));
        Assert.Single(sessions);
        Assert.Equal(25, sessions[0].PlannedMinutes);
        Assert.Equal("测试", sessions[0].Wish);
    }
}
