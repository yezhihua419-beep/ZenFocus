namespace ChanJing.Core.Services;

/// <summary>域名清洗工具：剥离协议/空白/结尾斜杠，统一小写。</summary>
public static class DomainUtil
{
    public static string Clean(string input)
    {
        var d = input.Trim();
        if (d.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) d = d[8..];
        if (d.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) d = d[7..];
        return d.Trim().TrimStart('.').TrimEnd('/', '.', ' ').ToLowerInvariant();
    }

    /// <summary>取主域（bilibili.com → bilibili），用于标题模糊匹配。</summary>
    public static string MainDomain(string domain)
    {
        var parts = domain.Split('.');
        return parts.Length >= 2 ? parts[^2] : domain;
    }

    /// <summary>国内站点品牌词映射：浏览器标题常显示中文名而非域名。</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Aliases =
        new Dictionary<string, string[]>
        {
            ["douyin.com"] = new[] { "抖音" },
            ["kuaishou.com"] = new[] { "快手" },
            ["bilibili.com"] = new[] { "哔哩哔哩", "bilibili" },
            ["xiaohongshu.com"] = new[] { "小红书" },
            ["weibo.com"] = new[] { "微博" },
            ["tieba.baidu.com"] = new[] { "贴吧" },
            ["douban.com"] = new[] { "豆瓣" },
            ["toutiao.com"] = new[] { "今日头条", "头条" },
            ["sohu.com"] = new[] { "搜狐" },
            ["taobao.com"] = new[] { "淘宝" },
            ["tmall.com"] = new[] { "天猫" },
            ["jd.com"] = new[] { "京东" },
            ["pinduoduo.com"] = new[] { "拼多多" },
            ["douyu.com"] = new[] { "斗鱼" },
            ["huya.com"] = new[] { "虎牙" },
            ["iqiyi.com"] = new[] { "爱奇艺" },
            ["youku.com"] = new[] { "优酷" }
        };

    /// <summary>标题是否命中某域名：完整域名 / 主域（≥5 字符防误配）/ 品牌词。</summary>
    public static bool TitleMatches(string? title, string domain)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        var lower = title.ToLowerInvariant();
        if (lower.Contains(domain, StringComparison.Ordinal)) return true;

        var main = MainDomain(domain);
        if (main.Length >= 5 && lower.Contains(main, StringComparison.Ordinal)) return true;

        return Aliases.TryGetValue(domain, out var aliases) &&
               aliases.Any(a => lower.Contains(a, StringComparison.Ordinal));
    }
}
