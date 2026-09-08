namespace ChanJing.Core.Services;

/// <summary>
/// 屏蔽名单服务：管理国内分类名单与自定义域名，落库启用状态，
/// 通过 HostsBlocker 应用/撤销屏蔽。
/// </summary>
public sealed class BlocklistService
{
    public const string SettingKeyCategories = "blocked_categories";
    public const string SettingKeyCustomDomains = "custom_domains";

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

    /// <summary>已启用的分类（存于 Settings）。</summary>
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

    /// <summary>自定义域名（存于 Settings，逗号分隔）。</summary>
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
            .Select(d => d.Trim())
            .Select(d => d.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? d[8..] : d)
            .Select(d => d.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? d[7..] : d)
            .Select(d => d.Trim().TrimStart('.').TrimEnd('/', '.', ' ').ToLowerInvariant())
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Distinct();
        _db.SetSetting(SettingKeyCustomDomains, string.Join(",", cleaned));
    }

    /// <summary>当前生效的全部域名（启用分类 + 自定义）。</summary>
    public IReadOnlyList<string> GetActiveDomains()
    {
        var result = new List<string>();
        foreach (var category in GetEnabledCategories())
        {
            if (DefaultCategories.TryGetValue(category, out var domains))
            {
                result.AddRange(domains);
            }
        }
        result.AddRange(GetCustomDomains());
        return result.Distinct().ToList();
    }

    /// <summary>应用屏蔽（写入 hosts 标记段）。</summary>
    public void Apply()
    {
        HostsBlocker.Apply(GetActiveDomains());
    }

    /// <summary>撤销屏蔽（清空 hosts 标记段）。</summary>
    public void Remove()
    {
        HostsBlocker.Remove();
    }

    /// <summary>屏蔽是否已生效。</summary>
    public bool IsApplied() => HostsBlocker.IsApplied();
}
