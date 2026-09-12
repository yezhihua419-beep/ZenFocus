using ChanJing.Core.Services;
using Xunit;

namespace ChanJing.Tests;

/// <summary>HostsBlocker 预应用功能测试（屏蔽配置先存临时文件，专注开始后同步到系统hosts）。</summary>
[Collection("Hosts")]
public class HostsBlockerPreApplyTests : IDisposable
{
    private readonly string _tempDir;

    public HostsBlockerPreApplyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "chanjing-preapply-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var hostsFile = Path.Combine(_tempDir, "hosts");
        File.WriteAllText(hostsFile, "127.0.0.1 localhost\n");
        HostsBlocker.HostsPathOverride = hostsFile;
        HostsBlocker.PreApplyPathOverride = Path.Combine(_tempDir, "hosts.pre");
        HostsBlocker.ClearPreApply();
    }

    public void Dispose()
    {
        HostsBlocker.ClearPreApply();
        HostsBlocker.HostsPathOverride = null;
        HostsBlocker.PreApplyPathOverride = null;
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* 忽略 */ }
    }

    [Fact]
    public void PreApply_WritesToTempFile()
    {
        Assert.False(HostsBlocker.IsPreApplied());

        HostsBlocker.PreApply(new[] { "douyin.com", "bilibili.com" });

        Assert.True(HostsBlocker.IsPreApplied());
    }

    [Fact]
    public void GetPreAppliedDomains_ReturnsCorrectDomains()
    {
        HostsBlocker.PreApply(new[] { "douyin.com", "bilibili.com", "example.com" });

        var domains = HostsBlocker.GetPreAppliedDomains();

        Assert.Equal(3, domains.Count);
        Assert.Contains("douyin.com", domains);
        Assert.Contains("bilibili.com", domains);
        Assert.Contains("example.com", domains);
    }

    [Fact]
    public void ClearPreApply_RemovesTempFile()
    {
        HostsBlocker.PreApply(new[] { "douyin.com" });
        Assert.True(HostsBlocker.IsPreApplied());

        HostsBlocker.ClearPreApply();

        Assert.False(HostsBlocker.IsPreApplied());
        Assert.Empty(HostsBlocker.GetPreAppliedDomains());
    }

    [Fact]
    public void PreApply_DoesNotWriteToSystemHosts()
    {
        // 预应用只写临时文件，不应该写系统hosts
        HostsBlocker.PreApply(new[] { "douyin.com" });

        Assert.False(HostsBlocker.IsApplied()); // 系统hosts中没有标记段
    }

    [Fact]
    public void ApplyFromPreApply_WritesToSystemHosts()
    {
        // 模拟专注开始时的逻辑：从预应用文件读取配置，写到系统hosts
        HostsBlocker.PreApply(new[] { "douyin.com", "bilibili.com" });
        var domains = HostsBlocker.GetPreAppliedDomains();

        HostsBlocker.Apply(domains);

        Assert.True(HostsBlocker.IsApplied()); // 系统hosts中有标记段
    }

    [Fact]
    public void RemoveFromSystemHosts_ClearsButKeepsPreApply()
    {
        // 模拟专注结束时的逻辑：清除系统hosts，但保留预应用配置（下次专注还能用）
        HostsBlocker.PreApply(new[] { "douyin.com" });
        HostsBlocker.Apply(HostsBlocker.GetPreAppliedDomains());
        Assert.True(HostsBlocker.IsApplied());

        HostsBlocker.Remove(); // 清除系统hosts

        Assert.False(HostsBlocker.IsApplied()); // 系统hosts已清除
        Assert.True(HostsBlocker.IsPreApplied()); // 预应用配置还在
    }

    [Fact]
    public void PreApply_EmptyDomains_CreatesEmptyFile()
    {
        HostsBlocker.PreApply(Array.Empty<string>());

        Assert.True(HostsBlocker.IsPreApplied());
        Assert.Empty(HostsBlocker.GetPreAppliedDomains());
    }

    [Fact]
    public void PreApply_TrimsAndLowercasesDomains()
    {
        HostsBlocker.PreApply(new[] { "  Douyin.COM  ", " Bilibili.com " });

        var domains = HostsBlocker.GetPreAppliedDomains();

        Assert.Contains("douyin.com", domains);
        Assert.Contains("bilibili.com", domains);
    }
}
