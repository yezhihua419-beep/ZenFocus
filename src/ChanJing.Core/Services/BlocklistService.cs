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
            ["购物"] = new[] { "taobao.com", "tmall.com", "jd.com", "pinduoduo.com" }
        };

    /// <summary>是否已激活（买断/订阅）。V1 为占位，支付上线后接入。</summary>
    public bool IsActivated() => _db.GetSetting(SettingKeyActivated) == "true";

    /// <summary>免费版是否达到目标数上限（分类数 + 自定义域名项数）。</summary>
    public bool IsOverFreeLimit(int extra = 0) =>
        !IsActivated() && GetEnabledCategories().Count + GetCustomDomains().Count + extra > FreeTargetLimit;

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
        HostsBlocker.Apply(GetActiveDomains());
    }

    /// <summary>撤销屏蔽（清空 hosts 标记段）。幂等。</summary>
    public void Remove()
    {
        HostsBlocker.Remove();
    }

    /// <summary>屏蔽是否已生效。</summary>
    public bool IsApplied() => HostsBlocker.IsApplied();
}
