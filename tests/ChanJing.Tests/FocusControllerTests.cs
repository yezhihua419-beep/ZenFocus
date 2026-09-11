using ChanJing.Core.Services;
using Xunit;

namespace ChanJing.Tests;

/// <summary>
/// FocusController单元测试：验证所有UI入口（快捷键/托盘/按钮）共用的专注逻辑。
/// 重点覆盖之前出过的bug：专注中应该结束(Finish)而不是暂停(Pause)。
/// </summary>
[Collection("Hosts")]
public class FocusControllerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppDatabase _db;
    private readonly FocusEngine _engine;
    private readonly BlocklistService _blocklist;
    private readonly FocusController _controller;

    public FocusControllerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "chanjing-focus-ctrl-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var hostsFile = Path.Combine(_tempDir, "hosts");
        File.WriteAllText(hostsFile, "127.0.0.1 localhost\n");
        HostsBlocker.HostsPathOverride = hostsFile;
        _db = new AppDatabase(Path.Combine(_tempDir, "test.db"));
        _engine = new FocusEngine(_db);
        _blocklist = new BlocklistService(_db);
        _controller = new FocusController(_db, _engine, _blocklist);

        // 重置FocusContext静态状态
        FocusContext.CurrentSceneTag = null;
        FocusContext.CurrentWish = null;
        FocusContext.CurrentMinutes = 25;
        FocusContext.DeepMode = false;
    }

    public void Dispose()
    {
        HostsBlocker.HostsPathOverride = null;
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* 忽略 */ }
    }

    [Fact]
    public void Toggle_WhenNotRunning_StartsFocus()
    {
        Assert.False(_controller.IsRunning);
        var result = _controller.Toggle();
        Assert.True(_controller.IsRunning);
        Assert.Contains("开始专注", result);
    }

    [Fact]
    public void Toggle_WhenRunning_StopsFocus_NotPause()
    {
        // 关键回归测试：专注中Toggle应该结束(Finish)，不是暂停(Pause)
        _controller.Start();
        Assert.True(_controller.IsRunning);
        Assert.False(_engine.IsPaused);

        var result = _controller.Toggle();

        Assert.False(_controller.IsRunning); // 已结束，不是暂停
        Assert.False(_engine.IsPaused);
        Assert.Contains("结束专注", result);
    }

    [Fact]
    public void Start_WithScene_UsesSceneConfig()
    {
        FocusContext.CurrentSceneTag = "work";
        var sceneConfig = SceneManager.GetSceneConfig(_db, "work");

        var result = _controller.Start();

        Assert.True(_controller.IsRunning);
        Assert.Equal(sceneConfig.Wish, FocusContext.CurrentWish);
        Assert.Equal(sceneConfig.Minutes, FocusContext.CurrentMinutes);
        Assert.Contains("work", result);
        // 屏蔽分类应已应用
        Assert.True(_blocklist.IsApplied());
    }

    [Fact]
    public void Start_WithoutScene_UsesDefault25Minutes()
    {
        FocusContext.CurrentSceneTag = null;

        var result = _controller.Start();

        Assert.True(_controller.IsRunning);
        Assert.Equal("专注", FocusContext.CurrentWish);
        Assert.Equal(25, FocusContext.CurrentMinutes);
        Assert.Contains("未选场景", result);
        // 关键回归测试：未选场景时也要调用Apply()，否则IsApplied()返回false，
        // OnFocusStarted和前台轮询都不工作，抖音不会被屏蔽
        Assert.True(_blocklist.IsApplied());
    }

    [Fact]
    public void Start_WithoutScene_StillAppliesShieldFromSavedConfig()
    {
        // 模拟用户在屏蔽页保存过配置，但未选场景就通过托盘/快捷键开始专注
        FocusContext.CurrentSceneTag = null;
        _blocklist.SetEnabledCategories(new[] { "短视频", "视频娱乐" });
        // 不调用Apply()，模拟用户保存了分类但还没应用屏蔽
        HostsBlocker.ClearPreApply();
        Assert.False(_blocklist.IsApplied());

        var result = _controller.Start();

        Assert.True(_controller.IsRunning);
        Assert.True(_blocklist.IsApplied()); // Start()应自动调用Apply()
        Assert.Contains("未选场景", result);
    }

    [Fact]
    public void Start_DeepMode_PlannedMinutesZero()
    {
        FocusContext.CurrentSceneTag = "work";
        FocusContext.DeepMode = true;

        _controller.Start();

        Assert.True(_controller.IsRunning);
        Assert.Equal(0, FocusContext.CurrentMinutes);
        Assert.Equal(0, _engine.Current?.PlannedMinutes);
    }

    [Fact]
    public void Stop_ResetsEmergencyPass()
    {
        _blocklist.EmergencyPass = true;
        _controller.Start();

        var result = _controller.Stop();

        Assert.False(_blocklist.EmergencyPass);
        Assert.Contains("结束专注", result);
    }

    [Fact]
    public void Stop_RecordsActualMinutesAndDistractions()
    {
        _controller.Start();
        _engine.RegisterDistraction();
        _engine.RegisterDistraction();

        var result = _controller.Stop();

        Assert.Contains("分心2次", result);
    }

    [Fact]
    public void Toggle_MultipleCycles_StartsAndStopsCorrectly()
    {
        // 连续切换多次，验证状态机正确
        _controller.Toggle(); // 开始
        Assert.True(_controller.IsRunning);

        _controller.Toggle(); // 结束
        Assert.False(_controller.IsRunning);

        _controller.Toggle(); // 再开始
        Assert.True(_controller.IsRunning);

        _controller.Toggle(); // 再结束
        Assert.False(_controller.IsRunning);
    }
}
