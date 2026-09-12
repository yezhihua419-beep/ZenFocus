using ChanJing.Core.Services;
using Xunit;

namespace ChanJing.Tests;

/// <summary>英文名单：进程别名、hosts 扩展域、浏览器标题、拦截门闩。进 Hosts 集合以免 ResolveLocale 串台。</summary>
[Collection("Hosts")]
public class EnglishBlockCatalogTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppDatabase _db;
    private readonly BlocklistService _service;
    private readonly Func<string>? _prevLocale;

    public EnglishBlockCatalogTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "chanjing-en-block-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "hosts"), "127.0.0.1 localhost\n");
        HostsBlocker.HostsPathOverride = Path.Combine(_tempDir, "hosts");
        HostsBlocker.PreApplyPathOverride = Path.Combine(_tempDir, "hosts.pre");
        BlocklistService.CatalogPathOverride = Path.Combine(_tempDir, "catalog.txt");
        _db = new AppDatabase(Path.Combine(_tempDir, "test.db"));
        _service = new BlocklistService(_db);
        _prevLocale = BlocklistService.ResolveLocale;
        BlocklistService.ResolveLocale = () => "en-US";
    }

    public void Dispose()
    {
        BlocklistService.ResolveLocale = _prevLocale;
        HostsBlocker.HostsPathOverride = null;
        HostsBlocker.PreApplyPathOverride = null;
        BlocklistService.CatalogPathOverride = null;
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* 忽略 */ }
    }

    [Fact]
    public void WorkScene_MatchesDesktopApps_NotChat()
    {
        _service.SetEnabledCategories(SceneManager.GetDefaultCategories("work"));

        Assert.Equal("短视频", _service.MatchBlockedApp("TikTok"));
        Assert.Equal("短视频", _service.MatchBlockedApp("TikTokLIVEStudio"));
        Assert.Equal("短视频", _service.MatchBlockedApp("Instagram"));
        Assert.Equal("视频娱乐", _service.MatchBlockedApp("Spotify"));
        Assert.Equal("视频娱乐", _service.MatchBlockedApp("steam"));
        Assert.Equal("视频娱乐", _service.MatchBlockedApp("steamwebhelper"));
        Assert.Equal("视频娱乐", _service.MatchBlockedApp("Netflix"));
        Assert.Equal("购物", _service.MatchBlockedApp("Amazon"));

        Assert.Null(_service.MatchBlockedApp("Discord"));
        Assert.Null(_service.MatchBlockedApp("slack"));
        Assert.Null(_service.MatchBlockedApp("chrome"));
        Assert.Null(_service.MatchBlockedApp("msedge"));
        Assert.Null(_service.MatchBlockedApp("Code"));

        var processes = _service.GetActiveAppProcesses();
        Assert.Contains("TikTokLIVEStudio", processes);
        Assert.Contains("steamwebhelper", processes);
    }

    [Fact]
    public void Communication_MatchesDesktopAliases()
    {
        _service.SetEnabledCategories(new[] { "沟通工具" });

        Assert.Equal("沟通工具", _service.MatchBlockedApp("Discord"));
        Assert.Equal("沟通工具", _service.MatchBlockedApp("DiscordPTB"));
        Assert.Equal("沟通工具", _service.MatchBlockedApp("DiscordCanary"));
        Assert.Equal("沟通工具", _service.MatchBlockedApp("slack"));
        Assert.Equal("沟通工具", _service.MatchBlockedApp("Telegram"));
        Assert.Equal("沟通工具", _service.MatchBlockedApp("TelegramDesktop"));
        Assert.Equal("沟通工具", _service.MatchBlockedApp("WhatsApp"));
        Assert.Equal("沟通工具", _service.MatchBlockedApp("WhatsAppDesktop"));
        Assert.Equal("沟通工具", _service.MatchBlockedApp("Teams"));
        Assert.Equal("沟通工具", _service.MatchBlockedApp("ms-teams"));
        Assert.Equal("沟通工具", _service.MatchBlockedApp("Signal"));

        var processes = _service.GetActiveAppProcesses();
        Assert.Contains("DiscordPTB", processes);
        Assert.Contains("WhatsAppDesktop", processes);
        Assert.Contains("TelegramDesktop", processes);
        Assert.DoesNotContain("steam", processes, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("steamwebhelper", processes, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkScene_ActiveDomains_CoverIntlSitesAndExtras()
    {
        _service.SetEnabledCategories(SceneManager.GetDefaultCategories("work"));
        var domains = _service.GetActiveDomains();

        Assert.Contains("tiktok.com", domains);
        Assert.Contains("vm.tiktok.com", domains);
        Assert.Contains("instagram.com", domains);
        Assert.Contains("youtube.com", domains);
        Assert.Contains("youtu.be", domains);
        Assert.Contains("m.youtube.com", domains);
        Assert.Contains("netflix.com", domains);
        Assert.Contains("amazon.com", domains);

        Assert.Contains("x.com", domains);
        Assert.DoesNotContain("douyin.com", domains);
        Assert.DoesNotContain("discord.com", domains);
    }

    [Fact]
    public void WorkScene_Apply_WritesYoutubeToIsolatedHosts()
    {
        _service.SetEnabledCategories(SceneManager.GetDefaultCategories("work"));
        _service.Apply();
        HostsBlocker.Apply(_service.GetActiveDomains());

        var text = File.ReadAllText(HostsBlocker.HostsPath);
        Assert.Contains(HostsBlocker.BeginMarker, text);
        Assert.Contains("127.0.0.1 youtube.com", text);
        Assert.Contains("127.0.0.1 www.youtube.com", text);
        Assert.Contains("127.0.0.1 youtu.be", text);
        Assert.Contains("127.0.0.1 www.youtu.be", text);
        Assert.Contains("127.0.0.1 tiktok.com", text);
        Assert.Contains("127.0.0.1 www.tiktok.com", text);
        Assert.Contains("127.0.0.1 x.com", text);
        Assert.DoesNotContain("douyin.com", text);
    }

    [Fact]
    public void Unapplied_MatchStillHits_ButInterceptSkipped()
    {
        _service.SetEnabledCategories(SceneManager.GetDefaultCategories("work"));

        Assert.Equal("短视频", _service.MatchBlockedApp("TikTok"));
        Assert.False(_service.IsApplied());
        Assert.False(_service.CanInterceptApps());

        var engine = new FocusEngine(_db);
        var daily = new DailyLimitService(_db);
        using var activity = new WindowActivityService(_db, daily, engine, _service);
        var blocked = 0;
        activity.AppBlocked += (_, _) => blocked++;

        activity.ApplyShieldNow();
        Assert.Equal(0, blocked);

        _service.Apply();
        Assert.True(_service.IsApplied());
        Assert.True(_service.CanInterceptApps());

        _service.EmergencyPass = true;
        Assert.False(_service.CanInterceptApps());
        activity.ApplyShieldNow();
        Assert.Equal(0, blocked);
    }

    [Fact]
    public void BrowserTitles_MatchIntlSites()
    {
        _service.SetEnabledCategories(new[] { "短视频", "视频娱乐", "社交", "资讯", "购物", "沟通工具" });

        Assert.Contains("youtube.com", _service.MatchBlockedDomains("Never Gonna Give You Up - YouTube - Google Chrome"));
        Assert.Contains("tiktok.com", _service.MatchBlockedDomains("TikTok - Make Your Day - Google Chrome"));
        Assert.Contains("instagram.com", _service.MatchBlockedDomains("Instagram - Google Chrome"));
        Assert.Contains("x.com", _service.MatchBlockedDomains("Home / X - Google Chrome"));
        Assert.Contains("x.com", _service.MatchBlockedDomains("(2) Notifications / X - Mozilla Firefox"));
        Assert.Contains("twitter.com", _service.MatchBlockedDomains("Twitter - Google Chrome"));
        Assert.Contains("reddit.com", _service.MatchBlockedDomains("reddit: the front page of the internet - Google Chrome"));
        Assert.Contains("netflix.com", _service.MatchBlockedDomains("Netflix - Google Chrome"));
        Assert.Contains("amazon.com", _service.MatchBlockedDomains("Amazon.com. Spend less. Smile more. - Google Chrome"));
        Assert.Contains("cnn.com", _service.MatchBlockedDomains("Breaking News, Latest News and Videos | CNN - Google Chrome"));
        Assert.Contains("bbc.com", _service.MatchBlockedDomains("BBC - Home - Google Chrome"));
        Assert.Contains("web.whatsapp.com", _service.MatchBlockedDomains("WhatsApp - Google Chrome"));

        Assert.Empty(_service.MatchBlockedDomains("Cursor — chanjing - Visual Studio Code"));
        Assert.Empty(_service.MatchBlockedDomains("Outlook - Microsoft"));
    }

    [Fact]
    public void BrowserProcess_IsNotAppHit_TitleStillHitsSite()
    {
        _service.SetEnabledCategories(new[] { "视频娱乐" });

        Assert.Null(_service.MatchBlockedApp("chrome"));
        Assert.Null(_service.MatchBlockedApp("msedge"));
        Assert.Null(_service.MatchBlockedApp("chrome", "YouTube - Google Chrome"));
        Assert.Null(_service.MatchBlockedApp("msedge", "Instagram"));
        Assert.Contains("youtube.com", _service.MatchBlockedDomains("YouTube - Google Chrome"));
    }

    [Fact]
    public void StoreHost_TitleHits_BrowserDoesNot()
    {
        _service.SetEnabledCategories(new[] { "短视频", "视频娱乐" });

        Assert.Null(_service.MatchBlockedApp("ApplicationFrameHost"));
        Assert.Equal("短视频", _service.MatchBlockedApp("ApplicationFrameHost", "Instagram"));
        Assert.Equal("短视频", _service.MatchBlockedApp("WWAHost", "TikTok"));
        Assert.Equal("视频娱乐", _service.MatchBlockedApp("ApplicationFrameHost", "Netflix"));
        Assert.Equal("视频娱乐", _service.MatchBlockedApp("ApplicationFrameHost", "YouTube"));
        Assert.Null(_service.MatchBlockedApp("ApplicationFrameHost", "Settings"));
        Assert.Null(_service.MatchBlockedApp("ApplicationFrameHost", "Calculator"));
        Assert.True(BlocklistService.IsStoreHostProcess("ApplicationFrameHost"));
        Assert.False(BlocklistService.IsStoreHostProcess("chrome"));
    }

    [Fact]
    public void AllowableTarget_PrefersLastDistraction_NotForeground()
    {
        _service.SetEnabledCategories(SceneManager.GetDefaultCategories("work"));
        var engine = new FocusEngine(_db);
        var daily = new DailyLimitService(_db);
        using var activity = new WindowActivityService(_db, daily, engine, _service);

        Assert.Null(activity.GetAllowableTarget());
        activity.RememberBlockedTarget("youtube.com");
        Assert.Equal("youtube.com", activity.GetAllowableTarget());
        activity.RememberBlockedTarget("TikTok");
        Assert.Equal("TikTok", activity.GetAllowableTarget());
    }

    [Fact]
    public void TitleMatches_XCom_DoesNotNeedFullDomain()
    {
        Assert.True(DomainUtil.TitleMatches("Home / X - Google Chrome", "x.com"));
        Assert.False(DomainUtil.TitleMatches("Excel - Book1", "x.com"));
        Assert.False(DomainUtil.TitleMatches("Outlook - Microsoft", "teams.microsoft.com"));
        Assert.True(DomainUtil.TitleMatches("Chat | Microsoft Teams - Google Chrome", "teams.microsoft.com"));
    }
}
