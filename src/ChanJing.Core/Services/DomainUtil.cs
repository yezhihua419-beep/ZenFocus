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
}
