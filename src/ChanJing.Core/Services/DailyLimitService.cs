namespace ChanJing.Core.Services;

/// <summary>
/// 每日限额服务：给域名设置每日使用上限（分钟），按前台窗口标题模糊匹配累计，
/// 超限可检测。数据全部存本地 Settings。
/// </summary>
public sealed class DailyLimitService
{
    public const string SettingKeyLimits = "daily_limits";
    public const string SettingKeyUsage = "daily_limit_usage";

    private readonly AppDatabase _db;
    private readonly object _lock = new();

    public DailyLimitService(AppDatabase db) => _db = db;

    /// <summary>所有限额：domain → 每日分钟上限。</summary>
    public IReadOnlyDictionary<string, int> GetLimits()
    {
        var raw = _db.GetSetting(SettingKeyLimits);
        var result = new Dictionary<string, int>();
        if (string.IsNullOrWhiteSpace(raw)) return result;

        foreach (var pair in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[1], out var minutes) && minutes > 0)
            {
                result[DomainUtil.Clean(parts[0])] = minutes;
            }
        }
        return result;
    }

    public void SetLimit(string domain, int minutes)
    {
        if (minutes <= 0) return;
        var limits = GetLimits().ToDictionary(kv => kv.Key, kv => kv.Value);
        limits[DomainUtil.Clean(domain)] = minutes;
        SaveLimits(limits);
    }

    public void RemoveLimit(string domain)
    {
        var limits = GetLimits().ToDictionary(kv => kv.Key, kv => kv.Value);
        limits.Remove(DomainUtil.Clean(domain));
        SaveLimits(limits);
    }

    private void SaveLimits(IReadOnlyDictionary<string, int> limits)
    {
        _db.SetSetting(SettingKeyLimits,
            string.Join(",", limits.Select(kv => $"{kv.Key}:{kv.Value}")));
    }

    /// <summary>标题匹配：完整域名 / 主域（≥5 字符）/ 品牌词，命中即算。</summary>
    public IReadOnlyList<string> MatchDomains(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        return GetLimits().Keys
            .Where(d => DomainUtil.TitleMatches(text, d))
            .ToList();
    }

    public int GetUsageSeconds(string domain, DateTime day)
    {
        lock (_lock)
        {
            var usage = ParseTodayUsage();
            var key = DomainUtil.Clean(domain);
            return usage.TryGetValue(key, out var seconds) ? seconds : 0;
        }
    }

    public void AddUsage(string domain, int seconds)
    {
        lock (_lock)
        {
            var usage = ParseTodayUsage();
            var key = DomainUtil.Clean(domain);
            usage[key] = usage.GetValueOrDefault(key) + seconds;
            SaveUsage(usage);
        }
    }

    /// <summary>当日累计是否已达上限。</summary>
    public bool IsExceeded(string domain)
    {
        var key = DomainUtil.Clean(domain);
        return GetLimits().TryGetValue(key, out var minutes) &&
               GetUsageSeconds(key, DateTime.Today) >= minutes * 60;
    }

    /// <summary>当日累计使用（秒），用于统计页展示。</summary>
    public IReadOnlyList<(string Domain, int Seconds)> GetTodayUsage()
    {
        lock (_lock)
        {
            var usage = ParseTodayUsage();
            return usage.Select(kv => (kv.Key, kv.Value))
                .OrderByDescending(x => x.Value)
                .ToList();
        }
    }

    // 存储格式：日期|domain=seconds;domain=seconds;…（只保留当天，跨天自动丢弃）
    private Dictionary<string, int> ParseTodayUsage()
    {
        var result = new Dictionary<string, int>();
        var raw = _db.GetSetting(SettingKeyUsage);
        if (string.IsNullOrWhiteSpace(raw)) return result;

        var parts = raw.Split('|');
        if (parts.Length != 2 || parts[0] != DateTime.Today.ToString("yyyy-MM-dd")) return result;

        foreach (var item in parts[1].Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = item.Split('=');
            if (kv.Length == 2 && int.TryParse(kv[1], out var seconds))
            {
                result[kv[0]] = seconds;
            }
        }
        return result;
    }

    private void SaveUsage(Dictionary<string, int> usage)
    {
        var body = string.Join(";", usage.Select(kv => $"{kv.Key}={kv.Value}"));
        _db.SetSetting(SettingKeyUsage, $"{DateTime.Today:yyyy-MM-dd}|{body}");
    }
}
