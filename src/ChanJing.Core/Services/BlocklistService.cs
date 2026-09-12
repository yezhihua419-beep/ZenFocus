using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ChanJing.Core.Services;

/// <summary>
/// 屏蔽名单服务：管理国内分类名单与自定义域名、临时放行、激活状态，
/// 通过 HostsBlocker 应用/撤销屏蔽。所有状态存本地 Settings。
/// </summary>
public sealed class BlocklistService
{
    /// <summary>HMAC激活码签名密钥（编译在软件内，离线验证）。</summary>
    private const string LicenseSecret = "chanjing-zen-focus-2026-v1";

    /// <summary>激活码格式：CJ-XXXX(序号hex)-XXXXXX(签名hex)。</summary>
    private static readonly Regex LicenseKeyPattern = new(@"^CJ-[0-9A-F]{4}-[0-9A-F]{6}$", RegexOptions.Compiled);

    public const string SettingKeyCategories = "blocked_categories";
    public const string SettingKeyCustomDomains = "custom_domains";
    public const string SettingKeyTempAllow = "temp_allow";
    public const string SettingKeyActivated = "activated";
    public const string SettingKeyCustomApps = "custom_apps";
    public const string SettingKeyAppBlockMode = "app_block_mode";
    public const string SettingKeyFocusOnlyCommunication = "focus_only_communication";
    public const string SettingKeyManualShield = "manual_shield";
    public const string SettingKeyCooldownMinutes = "cooldown_minutes";

    /// <summary>免费版最多可配置的屏蔽目标数（分类 + 自定义域名项）。</summary>
    public const int FreeTargetLimit = 3;

    private readonly AppDatabase _db;

    public BlocklistService(AppDatabase db) => _db = db;

    /// <summary>App 层注入当前名单语言。应读 catalog 文件，不要绑界面语言。</summary>
    public static Func<string>? ResolveLocale { get; set; }

    public static bool UseZhCatalog =>
        (ResolveLocale?.Invoke() ?? "zh-CN").StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    /// <summary>测试注入：名单语言文件。切 UI 语言不得改这份文件。</summary>
    private static string? _catalogPathOverride;
    private static string? _catalogCache;
    public static string? CatalogPathOverride
    {
        get => _catalogPathOverride;
        set { _catalogPathOverride = value; _catalogCache = null; }
    }

    public static string CatalogPath =>
        _catalogPathOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChanJing", "catalog.txt");

    /// <summary>读名单语言。文件不存在时写入 fallback（通常是当前界面语言，只种一次）。</summary>
    public static string ReadCatalogLocale(string fallback)
    {
        if (_catalogCache is "en-US" or "zh-CN") return _catalogCache;
        var seed = fallback is "zh-CN" ? "zh-CN" : "en-US";
        try
        {
            if (File.Exists(CatalogPath))
            {
                var saved = File.ReadAllText(CatalogPath).Trim();
                if (saved is "en-US" or "zh-CN")
                {
                    _catalogCache = saved;
                    return saved;
                }
            }
        }
        catch { }
        WriteCatalogLocale(seed);
        return seed;
    }

    public static void WriteCatalogLocale(string lang)
    {
        if (lang is not ("en-US" or "zh-CN")) lang = "en-US";
        try
        {
            var dir = Path.GetDirectoryName(CatalogPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(CatalogPath, lang);
        }
        catch { }
        _catalogCache = lang;
    }

    /// <summary>当前语言的默认网站分类（域名不含 www）。分类 key 固定中文。</summary>
    public static IReadOnlyDictionary<string, string[]> DefaultCategories =>
        UseZhCatalog ? CategoriesZh : CategoriesEn;

    /// <summary>当前语言的桌面应用预设（进程名不含 .exe）。</summary>
    public static IReadOnlyDictionary<string, string[]> DefaultAppCategories =>
        UseZhCatalog ? AppsZh : AppsEn;

    public static readonly IReadOnlyDictionary<string, string[]> CategoriesZh =
        new Dictionary<string, string[]>
        {
            ["短视频"] = new[] { "douyin.com", "kuaishou.com" },
            ["视频娱乐"] = new[] { "bilibili.com", "douyu.com", "huya.com", "iqiyi.com", "youku.com" },
            ["社交"] = new[] { "weibo.com", "xiaohongshu.com", "tieba.baidu.com", "douban.com" },
            ["资讯"] = new[] { "toutiao.com", "sohu.com" },
            ["购物"] = new[] { "taobao.com", "tmall.com", "jd.com", "pinduoduo.com" },
            ["沟通工具"] = new[] { "wx.qq.com", "web.wechat.com", "im.dingtalk.com", "dingtalk.com", "im.qq.com", "web.qq.com", "work.weixin.qq.com", "feishu.cn", "larkoffice.com", "web.telegram.org", "discord.com", "slack.com" }
        };

    public static readonly IReadOnlyDictionary<string, string[]> CategoriesEn =
        new Dictionary<string, string[]>
        {
            ["短视频"] = new[] { "tiktok.com", "instagram.com" },
            ["视频娱乐"] = new[] { "youtube.com", "netflix.com", "twitch.tv", "disneyplus.com", "hulu.com" },
            ["社交"] = new[] { "x.com", "twitter.com", "facebook.com", "reddit.com" },
            ["资讯"] = new[] { "cnn.com", "bbc.com", "nytimes.com" },
            ["购物"] = new[] { "amazon.com", "ebay.com", "etsy.com" },
            ["沟通工具"] = new[] { "web.whatsapp.com", "web.telegram.org", "discord.com", "slack.com", "teams.microsoft.com", "messenger.com" }
        };

    public static readonly IReadOnlyDictionary<string, string[]> AppsZh =
        new Dictionary<string, string[]>
        {
            ["短视频"] = new[] { "douyin", "kwai" },
            ["视频娱乐"] = new[] { "bilibili", "huya", "douyu", "iqiyi", "youku" },
            ["购物"] = new[] { "taobao", "jd", "pinduoduo" },
            ["沟通工具"] = new[] { "WeChat", "DingTalk", "QQ", "WXWork", "Lark", "Feishu", "Telegram", "Discord", "slack", "WeChatApp", "DingTalkLauncher" }
        };

    public static readonly IReadOnlyDictionary<string, string[]> AppsEn =
        new Dictionary<string, string[]>
        {
            ["短视频"] = new[] { "TikTok", "Instagram" },
            ["视频娱乐"] = new[] { "Spotify", "Steam", "Netflix" },
            ["购物"] = new[] { "Amazon" },
            ["沟通工具"] = new[] { "Discord", "slack", "Telegram", "WhatsApp", "Teams", "ms-teams", "Signal" }
        };

    /// <summary>桌面应用预设中文显示名（进程名 → 中文名）。未知进程显示原名。</summary>
    private static readonly IReadOnlyDictionary<string, string> AppDisplayNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["douyin"] = "抖音",
            ["kwai"] = "快手",
            ["bilibili"] = "哔哩哔哩",
            ["huya"] = "虎牙",
            ["douyu"] = "斗鱼",
            ["iqiyi"] = "爱奇艺",
            ["youku"] = "优酷",
            ["taobao"] = "淘宝",
            ["jd"] = "京东",
            ["pinduoduo"] = "拼多多",
            ["WeChat"] = "微信",
            ["DingTalk"] = "钉钉",
            ["QQ"] = "QQ",
            ["WXWork"] = "企业微信",
            ["Lark"] = "飞书",
            ["Feishu"] = "飞书",
            ["Telegram"] = "Telegram",
            ["TelegramDesktop"] = "Telegram",
            ["Discord"] = "Discord",
            ["DiscordPTB"] = "Discord",
            ["DiscordCanary"] = "Discord",
            ["slack"] = "Slack",
            ["TikTok"] = "TikTok",
            ["TikTokLIVEStudio"] = "TikTok",
            ["Instagram"] = "Instagram",
            ["Spotify"] = "Spotify",
            ["Steam"] = "Steam",
            ["steam"] = "Steam",
            ["steamwebhelper"] = "Steam",
            ["Netflix"] = "Netflix",
            ["Amazon"] = "Amazon",
            ["WhatsApp"] = "WhatsApp",
            ["WhatsAppDesktop"] = "WhatsApp",
            ["Teams"] = "Teams",
            ["ms-teams"] = "Teams",
            ["Signal"] = "Signal",
            ["Weixin"] = "微信"
        };

    /// <summary>进程别名：商店/Electron/预览版的真实 ProcessName 常与展示名不同。UI 仍只显示规范名。</summary>
    public static readonly IReadOnlyDictionary<string, string[]> AppProcessAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["TikTok"] = new[] { "TikTok", "TikTokLIVEStudio" },
            ["Discord"] = new[] { "Discord", "DiscordPTB", "DiscordCanary" },
            ["WhatsApp"] = new[] { "WhatsApp", "WhatsAppDesktop" },
            ["Steam"] = new[] { "steam", "steamwebhelper" },
            ["steam"] = new[] { "steam", "steamwebhelper" },
            ["Teams"] = new[] { "Teams", "ms-teams" },
            ["ms-teams"] = new[] { "Teams", "ms-teams" },
            ["Telegram"] = new[] { "Telegram", "TelegramDesktop" },
            ["WeChat"] = new[] { "WeChat", "WeChatApp", "Weixin" },
            ["DingTalk"] = new[] { "DingTalk", "DingTalkLauncher" }
        };

    /// <summary>站点附属域名：写 hosts / 标题匹配用，不进分类 UI。</summary>
    public static readonly IReadOnlyDictionary<string, string[]> DomainHostExtras =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["tiktok.com"] = new[] { "vm.tiktok.com" },
            ["youtube.com"] = new[] { "youtu.be", "m.youtube.com", "music.youtube.com", "youtube-nocookie.com" },
            ["x.com"] = new[] { "mobile.twitter.com", "mobile.x.com" },
            ["twitter.com"] = new[] { "mobile.twitter.com" },
            ["facebook.com"] = new[] { "fb.com", "m.facebook.com" },
            ["reddit.com"] = new[] { "old.reddit.com", "m.reddit.com" },
            ["amazon.com"] = new[] { "smile.amazon.com" },
            ["discord.com"] = new[] { "discordapp.com" },
            ["web.whatsapp.com"] = new[] { "whatsapp.com" }
        };

    /// <summary>规范进程名展开为真实可拦截 ProcessName（含自身）。</summary>
    public static IReadOnlyList<string> ExpandAppProcesses(string listed)
    {
        if (string.IsNullOrWhiteSpace(listed)) return Array.Empty<string>();
        return AppProcessAliases.TryGetValue(listed, out var aliases)
            ? aliases
            : new[] { listed };
    }

    /// <summary>进程名是否命中某条预设（含别名）。</summary>
    public static bool ProcessMatches(string listed, string actual) =>
        ExpandAppProcesses(listed).Any(a => string.Equals(a, actual, StringComparison.OrdinalIgnoreCase));

    /// <summary>进程名 → 中文显示名（仅预设应用有映射，未知返回原名）。</summary>
    public static string GetAppDisplayName(string processName) =>
        AppDisplayNames.TryGetValue(processName, out var name) ? name : processName;

    /// <summary>商店/UWP 壳进程：只能按窗口标题拦当前窗口，禁止按进程名杀光。</summary>
    public static bool IsStoreHostProcess(string? processName) =>
        processName is not null && StoreHostProcesses.Contains(processName);

    private static readonly HashSet<string> StoreHostProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ApplicationFrameHost", "WWAHost", "WinStore.App"
    };

    /// <summary>桌面应用进程名是否命中已启用分类（预设 + 用户自定义）。商店壳可再对标题匹配。未命中返回 null。</summary>
    public string? MatchBlockedApp(string processName, string? windowTitle = null)
    {
        if (string.IsNullOrWhiteSpace(processName)) return null;
        var enabled = GetEnabledCategories().ToHashSet(StringComparer.Ordinal);
        if (enabled.Count == 0) return null;

        foreach (var kv in DefaultAppCategories)
        {
            // FocusOnlyCommunication：非专注中时排除沟通工具分类
            if (IsFocusOnlyCommunication() && !IsFocusRunning && kv.Key == "沟通工具") continue;
            if (enabled.Contains(kv.Key) &&
                kv.Value.Any(p => ProcessMatches(p, processName)))
            {
                return kv.Key;
            }
        }
        foreach (var (proc, cat) in GetCustomApps())
        {
            if (enabled.Contains(cat) &&
                string.Equals(proc, processName, StringComparison.OrdinalIgnoreCase))
            {
                return cat;
            }
        }
        return MatchStoreHostByTitle(processName, windowTitle);
    }

    /// <summary>仅商店壳：用标题对已启用网站/App 名。浏览器（chrome/msedge）绝不走这里。</summary>
    private string? MatchStoreHostByTitle(string processName, string? windowTitle)
    {
        if (!IsStoreHostProcess(processName) || string.IsNullOrWhiteSpace(windowTitle)) return null;
        var hits = MatchBlockedDomains(windowTitle);
        if (hits.Count > 0)
            return CategoryOfDomain(hits[0]) ?? GetEnabledCategories().FirstOrDefault();

        var enabled = GetEnabledCategories().ToHashSet(StringComparer.Ordinal);
        foreach (var kv in DefaultAppCategories)
        {
            if (IsFocusOnlyCommunication() && !IsFocusRunning && kv.Key == "沟通工具") continue;
            if (!enabled.Contains(kv.Key)) continue;
            foreach (var proc in kv.Value)
            {
                if (proc.Length >= 4 && windowTitle.Contains(proc, StringComparison.OrdinalIgnoreCase))
                    return kv.Key;
            }
        }
        return null;
    }

    private string? CategoryOfDomain(string domain)
    {
        foreach (var kv in DefaultCategories)
        {
            foreach (var d in kv.Value)
            {
                if (d.Equals(domain, StringComparison.OrdinalIgnoreCase)) return kv.Key;
                if (DomainHostExtras.TryGetValue(d, out var extras) &&
                    extras.Any(e => e.Equals(domain, StringComparison.OrdinalIgnoreCase)))
                    return kv.Key;
            }
        }
        return null;
    }

    /// <summary>当前应拦截的桌面应用进程名集合（启用分类的预设 + 自定义）。</summary>
    public IReadOnlyList<string> GetActiveAppProcesses()
    {
        var enabled = GetEnabledCategories().ToHashSet(StringComparer.Ordinal);
        if (enabled.Count == 0) return Array.Empty<string>();

        var result = new List<string>();
        foreach (var kv in DefaultAppCategories)
        {
            if (!enabled.Contains(kv.Key)) continue;
            foreach (var proc in kv.Value)
                result.AddRange(ExpandAppProcesses(proc));
        }
        result.AddRange(GetCustomApps().Where(a => enabled.Contains(a.Category)).Select(a => a.Process));
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>当前应拦截的桌面应用（进程名, 分类），供 UI 展示。</summary>
    public IReadOnlyList<(string Process, string Category)> GetActiveApps()
    {
        var enabled = GetEnabledCategories().ToHashSet(StringComparer.Ordinal);
        if (enabled.Count == 0) return Array.Empty<(string, string)>();

        var result = new List<(string Process, string Category)>();
        foreach (var kv in DefaultAppCategories)
        {
            if (enabled.Contains(kv.Key))
            {
                foreach (var proc in kv.Value) result.Add((proc, kv.Key));
            }
        }
        result.AddRange(GetCustomApps().Where(a => enabled.Contains(a.Category)));
        return result
            .GroupBy(a => a.Process, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>用户自定义桌面应用（进程名, 分类）。</summary>
    public IReadOnlyList<(string Process, string Category)> GetCustomApps()
    {
        var raw = _db.GetSetting(SettingKeyCustomApps);
        var result = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(raw)) return result;
        foreach (var item in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = item.Split('|');
            if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
            {
                result.Add((parts[0].Trim(), parts[1].Trim()));
            }
        }
        return result;
    }

    /// <summary>添加自定义桌面应用（进程名不含 .exe；同进程同分类去重）。</summary>
    public void AddCustomApp(string processName, string category)
    {
        var proc = processName.Trim();
        if (proc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            proc = proc[..^4];
        }
        proc = proc.Trim();
        if (string.IsNullOrWhiteSpace(proc)) return;

        var current = GetCustomApps()
            .Where(a => !(string.Equals(a.Process, proc, StringComparison.OrdinalIgnoreCase) && a.Category == category))
            .ToList();
        current.Add((proc, category));
        _db.SetSetting(SettingKeyCustomApps,
            string.Join(";", current.Select(a => $"{a.Process}|{a.Category}")));
    }

    /// <summary>删除自定义桌面应用（按进程名，忽略分类）。</summary>
    public void RemoveCustomApp(string processName)
    {
        var remaining = GetCustomApps()
            .Where(a => !string.Equals(a.Process, processName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        _db.SetSetting(SettingKeyCustomApps,
            string.Join(";", remaining.Select(a => $"{a.Process}|{a.Category}")));
    }

    /// <summary>桌面应用拦截方式：minimize=自动最小化（默认）/ kill=结束进程。</summary>
    public string GetAppBlockMode() => _db.GetSetting(SettingKeyAppBlockMode) ?? "minimize";

    /// <summary>设置桌面应用拦截方式。</summary>
    public void SetAppBlockMode(string mode)
    {
        _db.SetSetting(SettingKeyAppBlockMode, mode == "kill" ? "kill" : "minimize");
    }

    /// <summary>ADHD模式缓冲期时长（分钟），默认10。免费版只能5/10/15，付费版可自定义。</summary>
    public int GetCooldownMinutes()
    {
        var val = _db.GetSetting(SettingKeyCooldownMinutes);
        return int.TryParse(val, out var m) && m >= 1 && m <= 60 ? m : 10;
    }

    /// <summary>设置ADHD模式缓冲期时长（分钟）。</summary>
    public void SetCooldownMinutes(int minutes)
    {
        _db.SetSetting(SettingKeyCooldownMinutes, Math.Clamp(minutes, 1, 60).ToString());
    }

    /// <summary>沟通工具是否仅在专注中屏蔽（默认false=全局屏蔽）。</summary>
    public bool IsFocusOnlyCommunication() => _db.GetSetting(SettingKeyFocusOnlyCommunication) == "true";

    /// <summary>设置沟通工具是否仅在专注中屏蔽。</summary>
    public void SetFocusOnlyCommunication(bool value) => _db.SetSetting(SettingKeyFocusOnlyCommunication, value ? "true" : "false");

    /// <summary>是否已激活（买断/订阅）。V1 为占位，支付上线后接入。</summary>
    public bool IsActivated() => _db.GetSetting(SettingKeyActivated) == "true";

    /// <summary>验证激活码格式与HMAC签名。格式：CJ-XXXX(序号)-XXXXXX(签名)。</summary>
    public static bool ValidateLicenseKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var k = key.Trim().ToUpperInvariant();
        if (!LicenseKeyPattern.IsMatch(k)) return false;
        // 提取序号（第4-7位，即 CJ-XXXX- 中的 XXXX）
        var seq = k.Substring(3, 4);
        // 计算签名：HMAC-SHA256(密钥, "CJ-" + 序号) 取前6位hex
        var expectedSig = ComputeSignature(seq);
        var actualSig = k.Substring(8, 6);
        return string.Equals(expectedSig, actualSig, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>计算激活码签名：HMAC-SHA256(密钥, "CJ-" + 序号hex) 前6位大写hex。</summary>
    private static string ComputeSignature(string seqHex)
    {
        var data = Encoding.UTF8.GetBytes("CJ-" + seqHex);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(LicenseSecret));
        var hash = hmac.ComputeHash(data);
        return Convert.ToHexString(hash).Substring(0, 6);
    }

    /// <summary>激活正式版：先验证HMAC签名，通过后写本地 activated=true。返回是否激活成功。</summary>
    public bool Activate(string licenseKey)
    {
        if (!ValidateLicenseKey(licenseKey)) return false;
        _db.SetSetting(SettingKeyActivated, "true");
        _db.SetSetting("license_key", licenseKey.Trim().ToUpperInvariant());
        return true;
    }

    /// <summary>当前是否在专注中（由App.xaml.cs在FocusStarted/FocusFinished时设置）。用于FocusOnlyCommunication判断。</summary>
    public bool IsFocusRunning { get; set; }

    // ---------- 手动屏蔽总开关（独立于专注计时） ----------

    /// <summary>手动屏蔽是否已启用（用户主动开启，不依赖专注计时）。</summary>
    /// <summary>紧急放行：为true时暂停桌面应用拦截（网站屏蔽保持生效）。</summary>
        public bool EmergencyPass { get; set; }

        public bool IsManualShieldActive() => _db.GetSetting(SettingKeyManualShield) == "true";

    /// <summary>启用手动屏蔽：立即写系统hosts（网站屏蔽），桌面App拦截同步生效。专注结束后不自动关闭。</summary>
    public void EnableManualShield()
    {
        _db.SetSetting(SettingKeyManualShield, "true");
        Apply(); // 写 hosts.pre
        try { HostsBlocker.Apply(GetActiveDomains()); }
        catch (UnauthorizedAccessException) { /* 普通权限写不了hosts，桌面App拦截仍生效 */ }
    }

    /// <summary>禁用手动屏蔽：清除系统hosts（仅当不在专注中）。专注中调用只清标记，专注结束时再清hosts。</summary>
    public void DisableManualShield(bool focusRunning = false)
    {
        _db.SetSetting(SettingKeyManualShield, "false");
        // 不调用 Remove()：hosts.pre 是用户保存的配置，手动屏蔽禁用不应清除配置
        // 专注中：只清标记，系统hosts由专注结束时的FocusFinished清除
        if (!focusRunning)
        {
            try { HostsBlocker.Remove(); }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>切换手动屏蔽状态，返回切换后状态。</summary>
    public bool ToggleManualShield(bool focusRunning = false)
    {
        if (IsManualShieldActive()) { DisableManualShield(focusRunning); return false; }
        else { EnableManualShield(); return true; }
    }

    /// <summary>免费版自定义域名是否达到上限（分类不限制，仅自定义域名限3个）。</summary>
    public bool IsOverFreeLimit(int extra = 0) =>
        !IsActivated() && GetCustomDomains().Count + extra > FreeTargetLimit;

    // ---------- 分类 ----------

    public IReadOnlyList<string> GetEnabledCategories()
    {
        var raw = _db.GetSetting(SettingKeyCategories);
        return string.IsNullOrWhiteSpace(raw)
            ? Array.Empty<string>()
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public void SetEnabledCategories(IEnumerable<string> categories)
    {
        _db.SetSetting(SettingKeyCategories, string.Join(",", categories));
    }

    /// <summary>还没勾过分类时写入兜底名单，不覆盖用户已保存的屏蔽页勾选。</summary>
    public void SeedCategoriesIfEmpty(IEnumerable<string> fallback)
    {
        if (GetEnabledCategories().Count > 0) return;
        var list = fallback.Where(c => !string.IsNullOrWhiteSpace(c)).ToArray();
        if (list.Length == 0) return;
        SetEnabledCategories(list);
    }

    /// <summary>删除一条自定义域名（清洗后精确匹配）。</summary>
    public void RemoveCustomDomain(string domain)
    {
        var clean = DomainUtil.Clean(domain);
        if (string.IsNullOrWhiteSpace(clean)) return;
        SetCustomDomains(GetCustomDomains().Where(d => !d.Equals(clean, StringComparison.OrdinalIgnoreCase)));
    }

    // ---------- 自定义域名 ----------

    public IReadOnlyList<string> GetCustomDomains()
    {
        var raw = _db.GetSetting(SettingKeyCustomDomains);
        return string.IsNullOrWhiteSpace(raw)
            ? Array.Empty<string>()
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public void SetCustomDomains(IEnumerable<string> domains)
    {
        var cleaned = domains
            .Select(DomainUtil.Clean)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Distinct();
        _db.SetSetting(SettingKeyCustomDomains, string.Join(",", cleaned));
    }

    // ---------- 临时放行 ----------

    /// <summary>临时放行某域名（分钟），到期自动恢复屏蔽。放行写入 Settings，读取时懒过期。</summary>
    public void AddTempAllow(string domain, int minutes)
    {
        var clean = DomainUtil.Clean(domain);
        if (string.IsNullOrWhiteSpace(clean) || minutes <= 0) return;

        var expires = DateTime.Now.AddMinutes(minutes);
        var current = GetTempAllows()
            .Where(a => !a.Domain.Equals(clean, StringComparison.Ordinal))
            .ToList();
        current.Add((clean, expires));
        SaveTempAllows(current);
    }

    /// <summary>当前未过期的临时放行项。</summary>
    public IReadOnlyList<(string Domain, DateTime Expires)> GetTempAllows()
    {
        var raw = _db.GetSetting(SettingKeyTempAllow);
        var result = new List<(string, DateTime)>();
        if (string.IsNullOrWhiteSpace(raw)) return result;

        var now = DateTime.Now;
        foreach (var item in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = item.Split('|');
            if (parts.Length == 2 &&
                DateTime.TryParse(parts[1], out var expires) &&
                expires > now)
            {
                result.Add((parts[0], expires));
            }
        }
        return result;
    }

    /// <summary>移除所有临时放行（手动恢复屏蔽）。</summary>
    public void ClearTempAllows()
    {
        _db.SetSetting(SettingKeyTempAllow, string.Empty);
    }

    private void SaveTempAllows(IEnumerable<(string Domain, DateTime Expires)> items)
    {
        _db.SetSetting(SettingKeyTempAllow,
            string.Join(";", items.Select(a => $"{a.Domain}|{a.Expires:o}")));
    }

    /// <summary>清理已过期的临时放行项。</summary>
    public void RemoveExpiredTempAllows()
    {
        var active = GetTempAllows(); // 读取时已过滤过期项
        SaveTempAllows(active);
    }

    /// <summary>进程名或商店壳标题对应域名是否在临时放行中。</summary>
    public bool IsTempAllowed(string processName, string? windowTitle = null)
    {
        var allows = GetTempAllows();
        if (!string.IsNullOrWhiteSpace(processName) &&
            allows.Any(a => string.Equals(a.Domain, processName, StringComparison.OrdinalIgnoreCase)))
            return true;
        if (string.IsNullOrWhiteSpace(windowTitle)) return false;
        return MatchBlockedDomains(windowTitle).Any(d =>
            allows.Any(a => string.Equals(a.Domain, d, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>清除所有临时放行（专注结束时调用）。</summary>
    public void ClearAllTempAllows()
    {
        SaveTempAllows(new List<(string, DateTime)>());
    }

    // ---------- 生效集合 ----------

    /// <summary>当前应生效的域名（启用分类 + 自定义 − 未过期临时放行）。</summary>
    public IReadOnlyList<string> GetActiveDomains()
    {
        var allowed = GetTempAllows()
            .Select(a => a.Domain)
            .ToHashSet(StringComparer.Ordinal);

        var result = new List<string>();
        foreach (var category in GetEnabledCategories())
        {
            // FocusOnlyCommunication：非专注中时排除沟通工具分类
            if (IsFocusOnlyCommunication() && !IsFocusRunning && category == "沟通工具") continue;
            if (DefaultCategories.TryGetValue(category, out var domains))
            {
                foreach (var d in domains)
                {
                    if (!allowed.Contains(d)) result.Add(d);
                    if (!DomainHostExtras.TryGetValue(d, out var extras)) continue;
                    result.AddRange(extras.Where(e => !allowed.Contains(e)));
                }
            }
        }
        result.AddRange(GetCustomDomains().Where(d => !allowed.Contains(d)));
        return result.Distinct().ToList();
    }

    /// <summary>标题是否命中当前屏蔽域名（完整域名/主域/品牌词）。</summary>
    public IReadOnlyList<string> MatchBlockedDomains(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        return GetActiveDomains()
            .Where(d => DomainUtil.TitleMatches(text, d))
            .ToList();
    }

    /// <summary>应用屏蔽（写入 hosts 标记段）。权限不足时向上抛出，由 UI 层引导。</summary>
    public void Apply()
    {
        RemoveExpiredTempAllows();
        HostsBlocker.PreApply(GetActiveDomains());
    }

    /// <summary>撤销屏蔽（清空 hosts 标记段）。幂等。</summary>
    public void Remove()
    {
        HostsBlocker.ClearPreApply();
    }

    /// <summary>重置所有屏蔽配置：清除启用分类/自定义域名/自定义应用/每日限额/临时放行/hosts.pre。场景配置不受影响（需单独重置场景）。</summary>
    public void ResetAll()
    {
        _db.SetSetting(SettingKeyCategories, "");
        _db.SetSetting(SettingKeyCustomDomains, "");
        _db.SetSetting(SettingKeyCustomApps, "");
        _db.SetSetting("daily_limits", "");
        _db.SetSetting(SettingKeyTempAllow, "");
        _db.SetSetting(SettingKeyManualShield, "false");
        HostsBlocker.ClearPreApply();
        try { HostsBlocker.Remove(); }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>屏蔽是否已生效。</summary>
    public bool IsApplied() => HostsBlocker.IsPreApplied();

    /// <summary>桌面应用拦截是否允许执行。名单命中不够，还要已 Apply 且非紧急放行。专注/手动开关由调用方判断。</summary>
    public bool CanInterceptApps() => IsApplied() && !EmergencyPass;

    /// <summary>启动清残留：无专注且非手动屏蔽时，清掉崩溃留下的系统 hosts 标记段。清掉返回 true。</summary>
    public bool TryClearOrphanSystemHosts(bool focusRunning)
    {
        if (focusRunning || IsManualShieldActive()) return false;
        if (!HostsBlocker.IsApplied()) return false;
        try
        {
            HostsBlocker.Remove();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>暂离：清系统 hosts（网站暂时能开）。权限不足时静默，桌面拦截仍靠 EmergencyPass。</summary>
    public void PauseSystemHosts()
    {
        try { HostsBlocker.Remove(); }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>暂离结束：专注中或手动屏蔽时把 hosts 写回去。</summary>
    public void RestoreSystemHostsIfNeeded(bool focusOrManual)
    {
        if (!focusOrManual) return;
        try { HostsBlocker.Apply(GetActiveDomains()); }
        catch (UnauthorizedAccessException) { }
    }

    // ---------- 导入/导出 ----------

    public string ExportConfig()
    {
        var config = new { version = 1, exportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), enabledCategories = GetEnabledCategories(), customDomains = GetCustomDomains(), customApps = GetCustomApps().Select(a => new { process = a.Process, category = a.Category }), appBlockMode = GetAppBlockMode() };
        return System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    public void ImportConfig(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        // 版本迁移：当前支持version=1，未来版本新增字段时在此处理
        int version = 1;
        if (root.TryGetProperty("version", out var ver)) version = ver.GetInt32();
        if (version > 1) System.Diagnostics.Debug.WriteLine($"ImportConfig: 配置版本v{version}高于当前v1，按v1兼容导入");
        if (root.TryGetProperty("enabledCategories", out var cats)) SetEnabledCategories(cats.EnumerateArray().Select(c => c.GetString()!).Where(s => !string.IsNullOrEmpty(s)).ToList());
        if (root.TryGetProperty("customDomains", out var domains)) { var domainList = domains.EnumerateArray().Select(d => d.GetString()!).Where(s => !string.IsNullOrEmpty(s)).ToList(); SetCustomDomains(domainList); }
        if (root.TryGetProperty("customApps", out var apps)) { foreach (var a in GetCustomApps()) RemoveCustomApp(a.Process); foreach (var a in apps.EnumerateArray()) { var process = a.GetProperty("process").GetString(); var category = a.TryGetProperty("category", out var cat) ? cat.GetString() ?? "短视频" : "短视频"; if (!string.IsNullOrEmpty(process)) AddCustomApp(process, category); } }
        if (root.TryGetProperty("appBlockMode", out var mode)) SetAppBlockMode(mode.GetString() ?? "minimize");
    }
}
