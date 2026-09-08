using ChanJing.Core.Services;
using Xunit;

namespace ChanJing.Tests;

/// <summary>每日限额服务测试。</summary>
public class DailyLimitServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppDatabase _db;
    private readonly DailyLimitService _limits;

    public DailyLimitServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "chanjing-limit-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _db = new AppDatabase(Path.Combine(_tempDir, "test.db"));
        _limits = new DailyLimitService(_db);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* 忽略 */ }
    }

    [Fact]
    public void SetLimit_PersistsAndCleans()
    {
        _limits.SetLimit("HTTPS://Bilibili.COM/", 30);

        var limits = _limits.GetLimits();

        Assert.Contains("bilibili.com", limits.Keys);
        Assert.Equal(30, limits["bilibili.com"]);
    }

    [Fact]
    public void RemoveLimit_Removes()
    {
        _limits.SetLimit("douyin.com", 20);
        _limits.RemoveLimit("douyin.com");

        Assert.Empty(_limits.GetLimits());
    }

    [Fact]
    public void MatchDomains_MatchesMainDomainInTitle()
    {
        _limits.SetLimit("bilibili.com", 30);

        var matched = _limits.MatchDomains("哔哩哔哩 (゜-゜)つロ 干杯~-bilibili - Google Chrome");

        Assert.Contains("bilibili.com", matched);
    }

    [Fact]
    public void AddUsage_AccumulatesPerDay()
    {
        _limits.AddUsage("bilibili.com", 5);
        _limits.AddUsage("bilibili.com", 10);

        Assert.Equal(15, _limits.GetUsageSeconds("bilibili.com", DateTime.Today));
    }

    [Fact]
    public void IsExceeded_TrueWhenOverLimit()
    {
        _limits.SetLimit("douyin.com", 1); // 1 分钟 = 60 秒
        _limits.AddUsage("douyin.com", 61);

        Assert.True(_limits.IsExceeded("douyin.com"));
    }

    [Fact]
    public void IsExceeded_FalseUnderLimit()
    {
        _limits.SetLimit("douyin.com", 1);
        _limits.AddUsage("douyin.com", 30);

        Assert.False(_limits.IsExceeded("douyin.com"));
    }

    [Fact]
    public void DomainUtil_CleansVariants()
    {
        Assert.Equal("example.com", DomainUtil.Clean("https://example.com/"));
        Assert.Equal("example.com", DomainUtil.Clean("Example.COM"));
        Assert.Equal("example.com", DomainUtil.Clean("  http://example.com  "));
        Assert.Equal("bilibili", DomainUtil.MainDomain("bilibili.com"));
    }
}
