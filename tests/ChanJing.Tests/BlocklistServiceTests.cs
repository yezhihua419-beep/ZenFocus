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
}
