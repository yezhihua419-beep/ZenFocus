using ChanJing.Core.Services;
using Xunit;

namespace ChanJing.Tests;

/// <summary>hosts 标记段管理器测试（使用临时文件，不触碰真实 hosts）。</summary>
[Collection("Hosts")]
public class HostsBlockerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _hostsFile;

    public HostsBlockerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "chanjing-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _hostsFile = Path.Combine(_tempDir, "hosts");
        HostsBlocker.HostsPathOverride = _hostsFile;
    }

    public void Dispose()
    {
        HostsBlocker.HostsPathOverride = null;
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* 忽略清理失败 */ }
    }

    [Fact]
    public void Apply_WritesMarkersAndDomains()
    {
        File.WriteAllText(_hostsFile, "127.0.0.1 localhost\n");

        HostsBlocker.Apply(new[] { "bilibili.com", "douyin.com" });

        var text = File.ReadAllText(_hostsFile);
        Assert.Contains(HostsBlocker.BeginMarker, text);
        Assert.Contains(HostsBlocker.EndMarker, text);
        Assert.Contains("127.0.0.1 bilibili.com", text);
        Assert.Contains("127.0.0.1 www.bilibili.com", text);
        Assert.Contains("127.0.0.1 douyin.com", text);
        Assert.True(HostsBlocker.IsApplied());
    }

    [Fact]
    public void Apply_PreservesUserEntriesOutsideBlock()
    {
        File.WriteAllText(_hostsFile, "127.0.0.1 localhost\n192.168.1.1 myrouter.local\n");

        HostsBlocker.Apply(new[] { "weibo.com" });

        var text = File.ReadAllText(_hostsFile);
        Assert.Contains("127.0.0.1 localhost", text);
        Assert.Contains("192.168.1.1 myrouter.local", text);
    }

    [Fact]
    public void Apply_Twice_DoesNotDuplicateBlock()
    {
        HostsBlocker.Apply(new[] { "a.com" });
        HostsBlocker.Apply(new[] { "a.com", "b.com" });

        var text = File.ReadAllText(_hostsFile);
        var beginCount = text.Split(HostsBlocker.BeginMarker).Length - 1;
        Assert.Equal(1, beginCount);
        Assert.Contains("127.0.0.1 b.com", text);
    }

    [Fact]
    public void Remove_ClearsBlockAndKeepsUserLines()
    {
        File.WriteAllText(_hostsFile, "127.0.0.1 localhost\n");
        HostsBlocker.Apply(new[] { "taobao.com" });

        HostsBlocker.Remove();

        var text = File.ReadAllText(_hostsFile);
        Assert.DoesNotContain(HostsBlocker.BeginMarker, text);
        Assert.DoesNotContain("taobao.com", text);
        Assert.Contains("127.0.0.1 localhost", text);
        Assert.False(HostsBlocker.IsApplied());
    }

    [Fact]
    public void Apply_CreatesBackupOnce()
    {
        File.WriteAllText(_hostsFile, "127.0.0.1 localhost\n");

        HostsBlocker.Apply(new[] { "a.com" });
        HostsBlocker.Apply(new[] { "b.com" });

        var bak = _hostsFile + ".chanjing.bak";
        Assert.True(File.Exists(bak));
        Assert.Equal("127.0.0.1 localhost\n", File.ReadAllText(bak));
    }
}
