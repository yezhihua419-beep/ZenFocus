using ChanJing.Core.Services;
using Xunit;

namespace ChanJing.Tests;

/// <summary>屏蔽名单服务测试（hosts 使用临时文件）。</summary>
[Collection("Hosts")]
public class BlocklistServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppDatabase _db;
    private readonly BlocklistService _service;

    public BlocklistServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "chanjing-block-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var hostsFile = Path.Combine(_tempDir, "hosts");
        File.WriteAllText(hostsFile, "127.0.0.1 localhost\n");
        HostsBlocker.HostsPathOverride = hostsFile;
        _db = new AppDatabase(Path.Combine(_tempDir, "test.db"));
        _service = new BlocklistService(_db);
    }

    public void Dispose()
    {
        HostsBlocker.HostsPathOverride = null;
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* 忽略 */ }
    }

    [Fact]
    public void DefaultCategories_ContainKeySites()
    {
        Assert.Contains("douyin.com", BlocklistService.DefaultCategories["短视频"]);
        Assert.Contains("bilibili.com", BlocklistService.DefaultCategories["视频娱乐"]);
    }

    [Fact]
    public void EnabledCategories_PersistAcrossInstances()
    {
        _service.SetEnabledCategories(new[] { "短视频", "社交" });

        var reloaded = new BlocklistService(_db);
        var categories = reloaded.GetEnabledCategories();

        Assert.Equal(2, categories.Count);
        Assert.Contains("短视频", categories);
        Assert.Contains("社交", categories);
    }

    [Fact]
    public void AppBlockMode_PersistsAcrossInstances_RegressionForKillModeLost()
    {
        // 回归测试：设置 kill 后，新建实例（模拟提权重启/进程重启）读取应为 kill，
        // 不应被页面销毁时 ComboBox 重置的 minimize 覆盖。
        _service.SetAppBlockMode("kill");
        Assert.Equal("kill", _service.GetAppBlockMode());

        var reloaded = new BlocklistService(_db);
        Assert.Equal("kill", reloaded.GetAppBlockMode());

        // 再验证 minimize 也能正确持久化
        reloaded.SetAppBlockMode("minimize");
        var reloaded2 = new BlocklistService(_db);
        Assert.Equal("minimize", reloaded2.GetAppBlockMode());
    }

    [Fact]
    public void ActiveDomains_CombineCategoriesAndCustom()
    {
        _service.SetEnabledCategories(new[] { "短视频" });
        _service.SetCustomDomains(new[] { "example.com", "test.cn" });

        var domains = _service.GetActiveDomains();

        Assert.Contains("douyin.com", domains);
        Assert.Contains("example.com", domains);
        Assert.Contains("test.cn", domains);
    }

    [Fact]
    public void SetCustomDomains_CleansInput()
    {
        _service.SetCustomDomains(new[] { "  HTTPS://Example.COM ", "example.com", "weibo.com/" });

        var domains = _service.GetCustomDomains();

        Assert.Equal(2, domains.Count);
        Assert.Contains("example.com", domains);
        Assert.Contains("weibo.com", domains);
    }

    [Fact]
    public void Apply_ThenRemove_WritesAndClearsHosts()
    {
        _service.SetEnabledCategories(new[] { "短视频" });
        _service.SetCustomDomains(new[] { "example.com" });

        _service.Apply();
        Assert.True(_service.IsApplied());

        _service.Remove();
        Assert.False(_service.IsApplied());
    }

    [Fact]
    public void TempAllow_ExcludesDomainFromActive()
    {
        _service.SetEnabledCategories(new[] { "短视频" });
        Assert.Contains("douyin.com", _service.GetActiveDomains());

        _service.AddTempAllow("douyin.com", 10);

        Assert.DoesNotContain("douyin.com", _service.GetActiveDomains());
        Assert.Single(_service.GetTempAllows());
    }

    [Fact]
    public void MatchBlockedDomains_MatchesMainDomain()
    {
        _service.SetEnabledCategories(new[] { "短视频" });

        var matched = _service.MatchBlockedDomains("抖音 - 记录美好生活 - Google Chrome");

        Assert.Contains("douyin.com", matched);
    }

    [Fact]
    public void FreeLimit_RespectsActivation()
    {
        Assert.False(_service.IsActivated());

        // 未激活：分类不限制，4个分类不超限
        _service.SetEnabledCategories(new[] { "短视频", "社交", "资讯", "购物" });
        Assert.False(_service.IsOverFreeLimit());

        // 未激活：自定义域名限3个，第4个超限
        _service.SetCustomDomains(new[] { "a.com", "b.com", "c.com", "d.com" });
        Assert.True(_service.IsOverFreeLimit());

        // 激活后不再受限
        var db2 = new AppDatabase(Path.Combine(_tempDir, "test.db"));
        db2.SetSetting(BlocklistService.SettingKeyActivated, "true");
        var activated = new BlocklistService(db2);
        Assert.True(activated.IsActivated());
        Assert.False(activated.IsOverFreeLimit());
    }

    // ===== P0-1 手动屏蔽开关测试 =====

    [Fact]
    public void ManualShield_DefaultOff()
    {
        Assert.False(_service.IsManualShieldActive());
    }

    [Fact]
    public void ManualShield_EnableThenDisable()
    {
        _service.EnableManualShield();
        Assert.True(_service.IsManualShieldActive());

        _service.DisableManualShield(focusRunning: false);
        Assert.False(_service.IsManualShieldActive());
    }

    [Fact]
    public void ManualShield_PersistsAcrossInstances()
    {
        _service.EnableManualShield();

        var reloaded = new BlocklistService(_db);
        Assert.True(reloaded.IsManualShieldActive());

        reloaded.DisableManualShield(focusRunning: false);
        var reloaded2 = new BlocklistService(_db);
        Assert.False(reloaded2.IsManualShieldActive());
    }

    [Fact]
    public void ManualShield_DisableWhileFocusRunning_DoesNotClearHosts()
    {
        // 模拟：专注中禁用手动屏蔽，不应清除hosts（专注仍在运行）
        _service.EnableManualShield();
        _service.Apply(); // 写hosts.pre
        Assert.True(_service.IsApplied());

        _service.DisableManualShield(focusRunning: true);
        // 专注中禁用手动屏蔽，hosts.pre应保留（专注结束时才清除）
        Assert.True(_service.IsApplied());
    }

    [Fact]
    public void ManualShield_Enable_WritesSystemHosts()
    {
        // EnableManualShield 应写系统hosts（用临时hosts文件模拟）
        _service.SetEnabledCategories(new[] { "短视频" });
        _service.EnableManualShield();

        var hostsContent = File.ReadAllText(HostsBlocker.HostsPathOverride!);
        Assert.Contains("douyin.com", hostsContent);
        Assert.Contains("# BEGIN CHANJING", hostsContent);
    }

    [Fact]
    public void ManualShield_Disable_NotFocusing_ClearsSystemHosts()
    {
        _service.SetEnabledCategories(new[] { "短视频" });
        _service.EnableManualShield();
        Assert.Contains("# BEGIN CHANJING", File.ReadAllText(HostsBlocker.HostsPathOverride!));

        _service.DisableManualShield(focusRunning: false);
        var hostsContent = File.ReadAllText(HostsBlocker.HostsPathOverride!);
        Assert.DoesNotContain("# BEGIN CHANJING", hostsContent);
    }

    [Fact]
    public void ManualShield_Toggle_SwitchesState()
    {
        Assert.False(_service.IsManualShieldActive());

        var newState = _service.ToggleManualShield(focusRunning: false);
        Assert.True(newState);
        Assert.True(_service.IsManualShieldActive());

        newState = _service.ToggleManualShield(focusRunning: false);
        Assert.False(newState);
        Assert.False(_service.IsManualShieldActive());
    }

    // ===== P2-8 配置导入导出版本测试 =====

    [Fact]
    public void ExportConfig_ContainsVersion1()
    {
        _service.SetEnabledCategories(new[] { "短视频", "社交" });
        var json = _service.ExportConfig();
        Assert.Contains("\"version\": 1", json);
        Assert.Contains("\"enabledCategories\"", json);
    }

    [Fact]
    public void ImportConfig_Version1_ImportsCorrectly()
    {
        var json = "{\"version\":1,\"enabledCategories\":[\"短视频\",\"购物\"],\"customDomains\":[\"test.com\"],\"customApps\":[],\"appBlockMode\":\"kill\"}";
        _service.ImportConfig(json);

        Assert.Contains("短视频", _service.GetEnabledCategories());
        Assert.Contains("购物", _service.GetEnabledCategories());
        Assert.Contains("test.com", _service.GetCustomDomains());
        Assert.Equal("kill", _service.GetAppBlockMode());
    }

    [Fact]
    public void ImportConfig_HigherVersion_StillImportsKnownFields()
    {
        // 模拟未来v2配置，包含v1没有的字段，应按兼容导入已知字段
        var json = "{\"version\":2,\"enabledCategories\":[\"视频娱乐\"],\"customDomains\":[],\"customApps\":[],\"appBlockMode\":\"minimize\",\"futureField\":\"ignored\"}";
        _service.ImportConfig(json);

        Assert.Contains("视频娱乐", _service.GetEnabledCategories());
        Assert.Equal("minimize", _service.GetAppBlockMode());
    }

    [Fact]
    public void ImportConfig_NoVersion_DefaultsToV1()
    {
        // 旧版配置没有version字段，应按v1处理
        var json = "{\"enabledCategories\":[\"资讯\"],\"customDomains\":[],\"customApps\":[],\"appBlockMode\":\"minimize\"}";
        _service.ImportConfig(json);

        Assert.Contains("资讯", _service.GetEnabledCategories());
    }

    // ---------- ADHD缓冲期时长测试 ----------

    [Fact]
    public void GetCooldownMinutes_DefaultIs10()
    {
        Assert.Equal(10, _service.GetCooldownMinutes());
    }

    [Fact]
    public void SetCooldownMinutes_PersistsAcrossInstances()
    {
        _service.SetCooldownMinutes(15);

        var service2 = new BlocklistService(_db);
        Assert.Equal(15, service2.GetCooldownMinutes());
    }

    [Fact]
    public void SetCooldownMinutes_ClampsToValidRange()
    {
        _service.SetCooldownMinutes(0); // 小于1应钳制为1
        Assert.Equal(1, _service.GetCooldownMinutes());

        _service.SetCooldownMinutes(100); // 大于60应钳制为60
        Assert.Equal(60, _service.GetCooldownMinutes());
    }

    [Fact]
    public void SetCooldownMinutes_ValidValues()
    {
        foreach (var m in new[] { 5, 10, 15, 20, 30 })
        {
            _service.SetCooldownMinutes(m);
            Assert.Equal(m, _service.GetCooldownMinutes());
        }
    }
}
