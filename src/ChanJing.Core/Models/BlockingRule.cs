namespace ChanJing.Core.Models;

/// <summary>屏蔽规则类型。</summary>
public enum BlockRuleKind
{
    /// <summary>分类名单（如：娱乐视频、社交、购物）。</summary>
    Category,

    /// <summary>自定义域名黑名单。</summary>
    Domain,

    /// <summary>每日限额（按域名或分类）。</summary>
    DailyLimit
}

/// <summary>一条屏蔽规则。</summary>
public sealed class BlockingRule
{
    public int Id { get; set; }

    /// <summary>规则类型。</summary>
    public BlockRuleKind Kind { get; set; }

    /// <summary>分类名或域名（Kind 为 DailyLimit 时用于关联的目标描述）。</summary>
    public string Target { get; set; } = "";

    /// <summary>规则是否启用。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>每日限额（分钟），仅 DailyLimit 使用；0 表示不限制。</summary>
    public int DailyLimitMinutes { get; set; }

    /// <summary>规则的显示名（用于报告与界面）。</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>创建时间。</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
