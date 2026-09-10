namespace ChanJing.Core.Services;

/// <summary>
/// 屏蔽名单服务：管理国内分类名单与自定义域名、临时放行、激活状态，
/// 通过 HostsBlocker 应用/撤销屏蔽。所有状态存本地 Settings。
/// </summary>
public sealed class BlocklistService
{
    public const string SettingKeyCategories = "blocked_categories";
    public const string SettingKeyCustomDomains = "custom_domains";
    public const string SettingKeyTempAllow = "temp_allow";
    public const string SettingKeyActivated = "activated";
    public const string SettingKeyCustomApps = "custom_apps";
    public const string SettingKeyAppBlockMode = "app_block_mode";
    public const string SettingKeyFocusOnlyCommunication = "focus_only_communication";
    public const string SettingKeyManualShield = "manual_shield";

    /// <summary>免费版最多可配置的屏蔽目标数（分类 + 自定义域名项）。</summary>
    public const int FreeTargetLimit = 3;

    private readonly AppDatabase _db;

    public BlocklistService(AppDatabase db) => _db = db;

    /// <summary>国内默认分类名单（域名不含 www，HostsBlocker 会自动补）。</summary>
    public static IReadOnlyDictionary<string, string[]> DefaultCategories { get; } =
        new Dictionary<string, string[]>
        {
            ["短视频"] = new[] { "douyin.com", "kuaishou.com" },
            ["视频娱乐"] = new[] { "bilibili.com", "douyu.com", "huya.com", "iqiyi.com", "youku.com" },
            ["社交"] = new[] { "weibo.com", "xiaohongshu.com", "tieba.baidu.com", "douban.com" },
            ["资讯"] = new[] { "toutiao.com", "sohu.com" },
            ["购物"] = new[] { "taobao.com", "tmall.com", "jd.com", "pinduoduo.com" },
            ["沟通工具"] = new[] { "wx.qq.com", "web.wechat.com", "im.dingtalk.com", "dingtalk.com", "im.qq.com", "web.qq.com", "work.weixin.qq.com", "feishu.cn", "larkoffice.com", "web.telegram.org", "discord.com", "slack.com" }
        };

    /// <summary>桌面应用拦截预设：分类 → 进程名（不含 .exe，匹配忽略大小写）。
    /// 只收录有独立桌面客户端的常见分心应用；用户可自行添加更多。</summary>
    public static IReadOnlyDictionary<string, string[]> DefaultAppCategories { get; } =
        new Dictionary<string, string[]>
        {
            ["短视频"] = new[] { "douyin", "kwai" },
            ["视频娱乐"] = new[] { "bilibili", "huya", "douyu", "iqiyi", "youku" },
            ["购物"] = new[] { "taobao", "jd", "pinduoduo" },
            ["沟通工具"] = new[] { "WeChat", "DingTalk", "QQ", "WXWork", "Lark", "Feishu", "Telegram", "Discord", "slack", "WeChatApp", "DingTalkLauncher" }
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
            ["Discord"] = "Discord",
            ["slack"] = "Slack"
        };

    /// <summary>进程名 → 中文显示名（仅预设应用有映射，未知返回原名）。</summary>
    public static string GetAppDisplayName(string processName) =>
        AppDisplayNames.TryGetValue(processName, out var name) ? name : processName;

    /// <summary>桌面应用进程名是否命中已启用分类（预设 + 用户自定义）。返回命中分类名，未命中返回 null。</summary>
    public string? MatchBlockedApp(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return null;
        var enabled = GetEnabledCategories().ToHashSet(StringComparer.Ordinal);
        if (enabled.Count == 0) return null;

        foreach (var kv in DefaultAppCategories)
        {
            if (enabled.Contains(kv.Key) &&
                kv.Value.Any(p => string.Equals(p, processName, StringComparison.OrdinalIgnoreCase)))
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
            if (enabled.Contains(kv.Key)) result.AddRange(kv.Value);
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

    /// <summary>沟通工具是否仅在专注中屏蔽（默认false=全局屏蔽）。</summary>
    public bool IsFocusOnlyCommunication() => _db.GetSetting(SettingKeyFocusOnlyCommunication) == "true";

    /// <summary>设置沟通工具是否仅在专注中屏蔽。</summary>
    public void SetFocusOnlyCommunication(bool value) => _db.SetSetting(SettingKeyFocusOnlyCommunication, value ? "true" : "false");

    /// <summary>是否已激活（买断/订阅）。V1 为占位，支付上线后接入。</summary>
    public bool IsActivated() => _db.GetSetting(SettingKeyActivated) == "true";

    // ---------- 手动屏蔽总开关（独立于专注计时） ----------

    /// <summary>手动屏蔽是否已启用（用户主动开启，不依赖专注计时）。</summary>
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
        Remove(); // 清 hosts.pre
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
            if (DefaultCategories.TryGetValue(category, out var domains))
            {
                result.AddRange(domains.Where(d => !allowed.Contains(d)));
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

    /// <summary>屏蔽是否已生效。</summary>
    public bool IsApplied() => HostsBlocker.IsPreApplied();

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
