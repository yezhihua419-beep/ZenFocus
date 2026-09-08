using ChanJing.Core.Models;
using ChanJing.Core.Services;
using Xunit;

namespace ChanJing.Tests;

/// <summary>专注引擎测试。</summary>
public class FocusEngineTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppDatabase _db;
    private readonly FocusEngine _engine;

    public FocusEngineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "chanjing-focus-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _db = new AppDatabase(Path.Combine(_tempDir, "test.db"));
        _engine = new FocusEngine(_db);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* 忽略 */ }
    }

    [Fact]
    public void Start_ThenFinishCompleted_PersistsAndStates()
    {
        _engine.Start("写完第三章", 25);
        Assert.True(_engine.IsRunning);
        Assert.NotNull(_engine.Current);

        var done = _engine.Finish(completed: true);

        Assert.Equal(FocusSessionState.Completed, done.State);
        Assert.Equal("写完第三章", done.Wish);
        Assert.True(done.ActualMinutes >= 1);
        Assert.False(_engine.IsRunning);

        var fromDb = _db.GetSessions(DateTime.Today, DateTime.Today.AddDays(1));
        var item = Assert.Single(fromDb);
        Assert.Equal(FocusSessionState.Completed, item.State);
    }

    [Fact]
    public void FinishBroken_RecordsDistraction()
    {
        _engine.Start(null, 25);
        _engine.RegisterDistraction();
        _engine.RegisterDistraction();

        var done = _engine.Finish(completed: false);

        Assert.Equal(FocusSessionState.Broken, done.State);
        Assert.Equal(2, done.DistractionCount);
    }

    [Fact]
    public void Pause_ThenResume_IsPausedFlips()
    {
        _engine.Start(null, 25);
        Assert.False(_engine.IsPaused);

        _engine.Pause();
        Assert.True(_engine.IsPaused);

        _engine.Resume();
        Assert.False(_engine.IsPaused);
    }

    [Fact]
    public void PauseWithoutRunning_IsNoOp()
    {
        _engine.Pause();
        Assert.False(_engine.IsPaused);
        _engine.Resume();
        Assert.False(_engine.IsPaused);
    }

    [Fact]
    public void Finish_WhilePaused_StillWorks()
    {
        _engine.Start(null, 25);
        _engine.Pause();

        var done = _engine.Finish(completed: true);

        Assert.Equal(FocusSessionState.Completed, done.State);
        Assert.False(_engine.IsPaused);
    }

    [Fact]
    public void Finish_WithoutStart_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => _engine.Finish(completed: true));
    }

    [Fact]
    public void GenerateFeedback_Completed_UsesProgressTone()
    {
        _engine.Start("晨间阅读", 25);
        var done = _engine.Finish(completed: true);

        var text = _engine.GenerateFeedback(done);

        Assert.Contains("心已定", text);
        Assert.Contains("本次定心", text);
        Assert.DoesNotContain("浪费", text);
    }

    [Fact]
    public void GenerateFeedback_Broken_UsesQuestionNotJudgement()
    {
        _engine.Start("写方案", 25);
        _engine.RegisterDistraction();
        var done = _engine.Finish(completed: false);

        var text = _engine.GenerateFeedback(done);

        Assert.Contains("发生了什么？", text);
        Assert.Contains("分心 1 次", text);
    }
}
