namespace ChanJing.Core.Services;

/// <summary>一键诊断：只读检查，不写系统 hosts。</summary>
public sealed record DiagnoseReport(
    bool Elevated,
    bool HostsWritable,
    bool HostsMarkerPresent,
    bool PreApplyPresent,
    bool OrphanMarker,
    string Catalog,
    string WebsiteLayer,
    string AppLayer,
    string BrowserLayer);

public static class FocusDiagnose
{
    /// <summary>website/app/browser 层用稳定 key，UI 再翻译。</summary>
    public static DiagnoseReport Run(BlocklistService blocklist, bool focusRunning, bool elevated)
    {
        var writable = HostsBlocker.CanWrite();
        var marker = HostsBlocker.IsApplied();
        var pre = HostsBlocker.IsPreApplied();
        var manual = blocklist.IsManualShieldActive();
        var orphan = marker && !focusRunning && !manual;
        return new DiagnoseReport(
            Elevated: elevated,
            HostsWritable: writable,
            HostsMarkerPresent: marker,
            PreApplyPresent: pre,
            OrphanMarker: orphan,
            Catalog: BlocklistService.ReadCatalogLocale("en-US"),
            WebsiteLayer: writable ? "hosts" : "need-admin",
            AppLayer: "process+store-title",
            BrowserLayer: writable ? "hosts-new-nav" : "bubble-only");
    }
}
