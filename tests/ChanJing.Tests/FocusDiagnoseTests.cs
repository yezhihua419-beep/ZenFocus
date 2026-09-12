using ChanJing.Core.Services;
using Xunit;

namespace ChanJing.Tests;

[Collection("Hosts")]
public class FocusDiagnoseTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppDatabase _db;
    private readonly BlocklistService _service;

    public FocusDiagnoseTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "chanjing-diag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var hosts = Path.Combine(_tempDir, "hosts");
        File.WriteAllText(hosts, "127.0.0.1 localhost\n");
        HostsBlocker.HostsPathOverride = hosts;
        HostsBlocker.PreApplyPathOverride = Path.Combine(_tempDir, "hosts.pre");
        BlocklistService.CatalogPathOverride = Path.Combine(_tempDir, "catalog.txt");
        _db = new AppDatabase(Path.Combine(_tempDir, "test.db"));
        _service = new BlocklistService(_db);
    }

    public void Dispose()
    {
        HostsBlocker.HostsPathOverride = null;
        HostsBlocker.PreApplyPathOverride = null;
        BlocklistService.CatalogPathOverride = null;
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void CanWrite_TempHosts_IsTrue()
    {
        Assert.True(HostsBlocker.CanWrite());
    }

    [Fact]
    public void Run_WritableHosts_ReportsHostsLayer()
    {
        var r = FocusDiagnose.Run(_service, focusRunning: false, elevated: false);
        Assert.False(r.Elevated);
        Assert.True(r.HostsWritable);
        Assert.Equal("hosts", r.WebsiteLayer);
        Assert.Equal("process+store-title", r.AppLayer);
        Assert.Equal("hosts-new-nav", r.BrowserLayer);
        Assert.False(r.OrphanMarker);
    }

    [Fact]
    public void Run_OrphanMarker_WhenIdleAndHostsTagged()
    {
        HostsBlocker.Apply(new[] { "youtube.com" });
        var r = FocusDiagnose.Run(_service, focusRunning: false, elevated: true);
        Assert.True(r.HostsMarkerPresent);
        Assert.True(r.OrphanMarker);

        var focusing = FocusDiagnose.Run(_service, focusRunning: true, elevated: true);
        Assert.False(focusing.OrphanMarker);
    }

    [Fact]
    public void Run_NeedAdmin_WhenHostsMissing()
    {
        File.Delete(HostsBlocker.HostsPath);
        Assert.False(HostsBlocker.CanWrite());
        var r = FocusDiagnose.Run(_service, focusRunning: false, elevated: false);
        Assert.Equal("need-admin", r.WebsiteLayer);
        Assert.Equal("bubble-only", r.BrowserLayer);
    }

    [Fact]
    public void Run_PreApplyAndCatalog_Persist()
    {
        BlocklistService.WriteCatalogLocale("zh-CN");
        _service.SetEnabledCategories(new[] { "短视频" });
        _service.Apply();
        var r = FocusDiagnose.Run(_service, focusRunning: false, elevated: true);
        Assert.True(r.PreApplyPresent);
        Assert.Equal("zh-CN", r.Catalog);
    }
}
