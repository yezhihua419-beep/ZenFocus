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

        // 未激活：第 4 个目标超限
        _service.SetEnabledCategories(new[] { "短视频", "社交", "资讯", "购物" });
        Assert.True(_service.IsOverFreeLimit());

        // 激活后不再受限
        var db2 = new AppDatabase(Path.Combine(_tempDir, "test.db"));
        db2.SetSetting(BlocklistService.SettingKeyActivated, "true");
        var activated = new BlocklistService(db2);
        Assert.True(activated.IsActivated());
        Assert.False(activated.IsOverFreeLimit());
    }
}
