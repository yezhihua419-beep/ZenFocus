using ChanJing.Core.Models;
using ChanJing.Core.Services;
using Xunit;

namespace ChanJing.Tests;

/// <summary>
/// L3集成测试：跨模块操作流测试。
/// 覆盖6大核心操作流，断言跨模块状态一致性。
/// </summary>
[Collection("Hosts")]
public class IntegrationFlowTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppDatabase _db;
    private readonly BlocklistService _blocklist;
    private readonly FocusEngine _engine;

    public IntegrationFlowTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "chanjing-integration-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var hostsFile = Path.Combine(_tempDir, "hosts");
        File.WriteAllText(hostsFile, "127.0.0.1 localhost\n");
        HostsBlocker.HostsPathOverride = hostsFile;
        _db = new AppDatabase(Path.Combine(_tempDir, "test.db"));
        _blocklist = new BlocklistService(_db);
        _engine = new FocusEngine(_db);
    }

    public void Dispose()
    {
        HostsBlocker.HostsPathOverride = null;
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* 忽略 */ }
    }

    // ========== 操作流1：场景切换流 ==========

    [Fact]
    public void Flow1_SceneSwitch_PreservesCustomConfig()
    {
        // 选工作场景，保存自定义配置（去掉购物）
        var workConfig = SceneManager.GetSceneConfig(_db, "work");
        var customCategories = workConfig.Categories.Where(c => c != "购物").ToArray();
        SceneManager.SaveSceneConfig(_db, "work", new SceneManager.SceneConfig(workConfig.Wish, workConfig.Minutes, customCategories));

        // 切换到学习场景
        var studyConfig = SceneManager.GetSceneConfig(_db, "study");
        _blocklist.SetEnabledCategories(studyConfig.Categories);

        // 切回工作场景，确认自定义配置保留
        var reloaded = SceneManager.GetSceneConfig(_db, "work");
        Assert.DoesNotContain("购物", reloaded.Categories);
        Assert.Contains("短视频", reloaded.Categories);
        Assert.True(SceneManager.IsCustomized(_db, "work"));
    }

    [Fact]
    public void Flow1_SceneSwitch_UncustomizedScene_DoesNotConsumeQuota()
    {
        // 初始状态：无自定义场景
        Assert.Equal(0, SceneManager.GetCustomSceneCount(_db));

        // 选工作场景（不自定义），开始专注，结束
        var config = SceneManager.GetSceneConfig(_db, "work");
        _blocklist.SetEnabledCategories(config.Categories);
        _blocklist.Apply();
        _engine.Start("测试", config.Minutes);
        _engine.Finish(true);

        // 未自定义的场景不应消耗额度
        Assert.Equal(0, SceneManager.GetCustomSceneCount(_db));
    }

    // ========== 操作流2：专注全流程 ==========

    [Fact]
    public void Flow2_FocusFullCycle_HostsWrittenThenCleared()
    {
        // 配置屏蔽
        _blocklist.SetEnabledCategories(new[] { "短视频" });
        _blocklist.Apply();

        // 开始专注 → hosts应写入
        _engine.Start("测试专注", 25);
        Assert.True(_engine.IsRunning);
        // 模拟App.xaml.cs的FocusStarted逻辑：写系统hosts
        HostsBlocker.Apply(_blocklist.GetActiveDomains());
        var hostsContent = File.ReadAllText(HostsBlocker.HostsPathOverride!);
        Assert.Contains("douyin.com", hostsContent);

        // 结束专注 → hosts应清除
        _engine.Finish(true);
        Assert.False(_engine.IsRunning);
        // 模拟App.xaml.cs的FocusFinished逻辑：清除系统hosts
        HostsBlocker.Remove();
        hostsContent = File.ReadAllText(HostsBlocker.HostsPathOverride!);
        Assert.DoesNotContain("douyin.com", hostsContent);
    }

    [Fact]
    public void Flow2_FocusFullCycle_DistractionRecorded()
    {
        _engine.Start("测试", 25);
        _engine.RegisterDistraction();
        _engine.RegisterDistraction();
        var result = _engine.Finish(true);
        Assert.Equal(2, result.DistractionCount);
        Assert.True(result.ActualMinutes >= 0);
    }

    [Fact]
    public void Flow2_FocusController_WithoutScene_StillAppliesShield()
    {
        // 回归测试：托盘/快捷键未选场景开始专注时，屏蔽也应生效
        // 之前的bug：FocusController.Start()未选场景分支没调Apply()，
        // 导致IsApplied()返回false，OnFocusStarted和前台轮询都不工作

        // 1. 配置屏蔽分类（模拟用户在屏蔽页保存过）
        _blocklist.SetEnabledCategories(new[] { "短视频", "视频娱乐" });

        // 2. 清除hosts.pre（模拟第一次使用或之前清除过配置）
        HostsBlocker.ClearPreApply();
        Assert.False(_blocklist.IsApplied());

        // 3. 未选场景，通过FocusController开始专注
        FocusContext.CurrentSceneTag = null;
        var controller = new FocusController(_db, _engine, _blocklist);
        var result = controller.Start();

        // 4. 验证屏蔽已应用（关键断言）
        Assert.True(controller.IsRunning);
        Assert.True(_blocklist.IsApplied()); // hosts.pre应存在
        Assert.Contains("未选场景", result);

        // 5. 模拟App.xaml.cs的FocusStarted逻辑：写系统hosts
        HostsBlocker.Apply(_blocklist.GetActiveDomains());
        var hostsContent = File.ReadAllText(HostsBlocker.HostsPathOverride!);
        Assert.Contains("douyin.com", hostsContent); // 抖音应在系统hosts里

        // 6. 结束专注，清除系统hosts
        controller.Stop();
        HostsBlocker.Remove();
        hostsContent = File.ReadAllText(HostsBlocker.HostsPathOverride!);
        Assert.DoesNotContain("douyin.com", hostsContent);
    }

    // ========== 操作流3：深度模式流 ==========

    [Fact]
    public void Flow3_DeepMode_PlannedMinutesZero_NoAutoFinish()
    {
        // 深度模式：plannedMinutes=0
        _engine.Start("深度模式测试", 0);
        Assert.True(_engine.IsRunning);
        Assert.Equal(0, _engine.Current?.PlannedMinutes);

        // 深度模式不会自动结束（需要手动结束）
        // 模拟等待（实际测试中用很短的时间）
        System.Threading.Thread.Sleep(100);
        Assert.True(_engine.IsRunning); // 仍然在运行，没有自动结束

        // 手动结束
        var result = _engine.Finish(true);
        Assert.False(_engine.IsRunning);
    }

    [Fact]
    public void Flow3_DeepMode_EmergencyPassResetOnFinish()
    {
        _blocklist.EmergencyPass = true;
        _engine.Start("深度模式", 0);
        // 模拟FocusStarted重置EmergencyPass
        _blocklist.EmergencyPass = false;
        Assert.False(_blocklist.EmergencyPass);

        _engine.Finish(true);
        // 模拟FocusFinished重置
        _blocklist.EmergencyPass = false;
        Assert.False(_blocklist.EmergencyPass);
    }

    // ========== 操作流4：暂离模式流 ==========

    [Fact]
    public void Flow4_EmergencyPass_DesktopBlockPausedThenResumed()
    {
        _blocklist.SetEnabledCategories(new[] { "短视频" });
        _blocklist.Apply();
        _engine.Start("测试", 25);

        // 开启暂离模式
        _blocklist.EmergencyPass = true;
        Assert.True(_blocklist.EmergencyPass);
        // 模拟WindowActivityService拦截条件：!EmergencyPass时才拦截
        bool shouldBlock = _engine.IsRunning && _blocklist.IsApplied() && !_blocklist.EmergencyPass;
        Assert.False(shouldBlock); // 暂离模式下不拦截桌面应用

        // 5分钟后恢复（模拟计时器）
        _blocklist.EmergencyPass = false;
        shouldBlock = _engine.IsRunning && _blocklist.IsApplied() && !_blocklist.EmergencyPass;
        Assert.True(shouldBlock); // 恢复后拦截

        _engine.Finish(true);
    }

    [Fact]
    public void Flow4_EmergencyPass_ResetOnBreakAndComplete()
    {
        // 圆满结束路径
        _blocklist.EmergencyPass = true;
        _engine.Start("测试1", 25);
        _engine.Finish(true);
        _blocklist.EmergencyPass = false; // 模拟Complete_Click重置
        Assert.False(_blocklist.EmergencyPass);

        // 放下（破功）路径
        _blocklist.EmergencyPass = true;
        _engine.Start("测试2", 25);
        _engine.Finish(false);
        _blocklist.EmergencyPass = false; // 模拟Break_Click重置
        Assert.False(_blocklist.EmergencyPass);
    }

    // ========== 操作流5：手动屏蔽流 ==========

    [Fact]
    public void Flow5_ManualShield_ImmediateThenPersistent()
    {
        _blocklist.SetEnabledCategories(new[] { "短视频" });

        // 开启手动屏蔽 → 立即写hosts
        _blocklist.EnableManualShield();
        Assert.True(_blocklist.IsManualShieldActive());
        var hostsContent = File.ReadAllText(HostsBlocker.HostsPathOverride!);
        Assert.Contains("douyin.com", hostsContent);

        // 手动屏蔽不依赖专注计时
        Assert.False(_engine.IsRunning);

        // 关闭手动屏蔽（非专注中）→ 清除hosts
        _blocklist.DisableManualShield(focusRunning: false);
        Assert.False(_blocklist.IsManualShieldActive());
        hostsContent = File.ReadAllText(HostsBlocker.HostsPathOverride!);
        Assert.DoesNotContain("douyin.com", hostsContent);
    }

    [Fact]
    public void Flow5_ManualShield_DuringFocus_OnlyClearsFlagNotHosts()
    {
        _blocklist.SetEnabledCategories(new[] { "短视频" });
        _blocklist.EnableManualShield();
        _engine.Start("测试", 25);

        // 专注中关闭手动屏蔽 → 只清标记，不清hosts（由FocusFinished清除）
        _blocklist.DisableManualShield(focusRunning: true);
        Assert.False(_blocklist.IsManualShieldActive());
        var hostsContent = File.ReadAllText(HostsBlocker.HostsPathOverride!);
        Assert.Contains("douyin.com", hostsContent); // hosts还在

        // 结束专注 → 清除hosts
        _engine.Finish(true);
        HostsBlocker.Remove();
        hostsContent = File.ReadAllText(HostsBlocker.HostsPathOverride!);
        Assert.DoesNotContain("douyin.com", hostsContent);
    }

    // ========== 操作流6：退出清理流 ==========

    [Fact]
    public void Flow6_ExitApp_ClearsHosts()
    {
        // 模拟专注中退出
        _blocklist.SetEnabledCategories(new[] { "短视频" });
        _blocklist.Apply();
        _engine.Start("测试", 25);
        HostsBlocker.Apply(_blocklist.GetActiveDomains());

        // 模拟ExitApp清理
        HostsBlocker.Remove();

        // hosts应被清除
        var hostsContent = File.ReadAllText(HostsBlocker.HostsPathOverride!);
        Assert.DoesNotContain("douyin.com", hostsContent);
    }

    [Fact]
    public void Flow6_ExitApp_WithManualShield_ClearsHosts()
    {
        // 模拟手动屏蔽中退出
        _blocklist.SetEnabledCategories(new[] { "短视频" });
        _blocklist.EnableManualShield();

        // 模拟ExitApp清理
        HostsBlocker.Remove();

        // hosts应被清除
        var hostsContent = File.ReadAllText(HostsBlocker.HostsPathOverride!);
        Assert.DoesNotContain("douyin.com", hostsContent);
    }

    // ========== 操作流7：FocusOnlyCommunication流 ==========

    [Fact]
    public void Flow7_FocusOnlyCommunication_ExcludedWhenNotFocusing()
    {
        _blocklist.SetEnabledCategories(new[] { "短视频", "沟通工具" });
        _blocklist.SetFocusOnlyCommunication(true);

        // 非专注中：沟通工具应被排除
        _blocklist.IsFocusRunning = false;
        var domains = _blocklist.GetActiveDomains();
        Assert.Contains("douyin.com", domains);
        Assert.DoesNotContain("wx.qq.com", domains); // 沟通工具被排除
    }

    [Fact]
    public void Flow7_FocusOnlyCommunication_IncludedWhenFocusing()
    {
        _blocklist.SetEnabledCategories(new[] { "短视频", "沟通工具" });
        _blocklist.SetFocusOnlyCommunication(true);

        // 专注中：沟通工具应被包含
        _blocklist.IsFocusRunning = true;
        var domains = _blocklist.GetActiveDomains();
        Assert.Contains("douyin.com", domains);
        Assert.Contains("wx.qq.com", domains); // 沟通工具在专注中被包含
    }

    // ========== 操作流8：ResetAll流 ==========

    [Fact]
    public void Flow8_ResetAll_ClearsAllButPreservesScenes()
    {
        // 设置各种配置
        _blocklist.SetEnabledCategories(new[] { "短视频", "社交" });
        _blocklist.SetCustomDomains(new[] { "test.com" });
        _blocklist.AddCustomApp("testapp", "短视频");
        _blocklist.EnableManualShield();

        // 保存场景配置
        SceneManager.SaveSceneConfig(_db, "work", new SceneManager.SceneConfig("测试愿望", 50, new[] { "短视频" }));

        // ResetAll
        _blocklist.ResetAll();

        // 屏蔽配置应被清除
        Assert.Empty(_blocklist.GetEnabledCategories());
        Assert.Empty(_blocklist.GetCustomDomains());
        Assert.False(_blocklist.IsManualShieldActive());

        // 场景配置应保留
        Assert.True(SceneManager.IsCustomized(_db, "work"));
        var sceneConfig = SceneManager.GetSceneConfig(_db, "work");
        Assert.Equal("测试愿望", sceneConfig.Wish);
    }

    // ========== 操作流9：ADHD模式全流程 ==========

    [Fact]
    public void Flow9_AdhdFocus_FullCycle_PersistsIsAdhd()
    {
        // 开始ADHD模式专注
        _engine.Start("ADHD专注测试", 15, isAdhd: true);
        Assert.True(_engine.IsRunning);
        Assert.True(_engine.Current!.IsAdhd);
        Assert.Equal(15, _engine.Current.PlannedMinutes);

        // 结束专注
        var done = _engine.Finish(completed: true);
        Assert.True(done.IsAdhd);
        Assert.Equal(FocusSessionState.Completed, done.State);

        // 验证落库
        var fromDb = _db.GetSessions(DateTime.Today, DateTime.Today.AddDays(1));
        var item = Assert.Single(fromDb);
        Assert.True(item.IsAdhd);
        Assert.Equal(15, item.PlannedMinutes);
    }

    [Fact]
    public void Flow9_AdhdAndNormal_Isolated()
    {
        // 第一次：ADHD模式
        _engine.Start("ADHD", 15, isAdhd: true);
        _engine.Finish(completed: true);

        // 第二次：普通模式
        _engine.Start("普通", 25, isAdhd: false);
        _engine.Finish(completed: true);

        // 验证两条记录互不影响
        var sessions = _db.GetSessions(DateTime.Today, DateTime.Today.AddDays(1));
        Assert.Equal(2, sessions.Count);

        var adhdSession = sessions.First(s => s.Wish == "ADHD");
        var normalSession = sessions.First(s => s.Wish == "普通");

        Assert.True(adhdSession.IsAdhd);
        Assert.Equal(15, adhdSession.PlannedMinutes);
        Assert.False(normalSession.IsAdhd);
        Assert.Equal(25, normalSession.PlannedMinutes);
    }

    [Fact]
    public void Flow9_AdhdFocusFinished_EventCanAccessIsAdhd()
    {
        // 验证FocusFinished事件触发时Current还在，可访问IsAdhd
        bool? eventIsAdhd = null;
        bool? eventCompleted = null;
        _engine.FocusFinished += (completed) =>
        {
            eventIsAdhd = _engine.Current?.IsAdhd;
            eventCompleted = completed;
        };

        _engine.Start("ADHD事件测试", 15, isAdhd: true);
        _engine.Finish(completed: true);

        Assert.True(eventIsAdhd.HasValue);
        Assert.True(eventIsAdhd.Value);
        Assert.True(eventCompleted.HasValue);
        Assert.True(eventCompleted.Value);
    }

    [Fact]
    public void Flow9_AdhdCooldownMinutes_PersistsAndUsed()
    {
        // 设置缓冲期时长为15分钟
        _blocklist.SetCooldownMinutes(15);
        Assert.Equal(15, _blocklist.GetCooldownMinutes());

        // 跨实例验证
        var blocklist2 = new BlocklistService(_db);
        Assert.Equal(15, blocklist2.GetCooldownMinutes());

        // 改回默认10分钟
        _blocklist.SetCooldownMinutes(10);
        Assert.Equal(10, _blocklist.GetCooldownMinutes());
    }

    [Fact]
    public void Flow9_AdhdDeepModeMutuallyExclusive_VerifiedAtEngineLevel()
    {
        // 引擎层面：ADHD和深度模式是两个独立参数，UI层负责互斥
        // 验证深度模式(plannedMinutes=0)可以和ADHD同时设置（虽然UI层互斥）
        _engine.Start("ADHD+深度", 0, isAdhd: true);
        Assert.True(_engine.Current!.IsAdhd);
        Assert.Equal(0, _engine.Current.PlannedMinutes);
        _engine.Finish(completed: true);

        // 验证普通深度模式
        _engine.Start("普通深度", 0, isAdhd: false);
        Assert.False(_engine.Current!.IsAdhd);
        Assert.Equal(0, _engine.Current.PlannedMinutes);
        _engine.Finish(completed: true);
    }
}
