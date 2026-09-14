namespace ChanJing.Core.Services;

/// <summary>对外收银台。无链接时 UI 不得跳空页。</summary>
public static class CheckoutLinks
{
    public const string Afdian = "https://afdian.com/a/zenfocus";

    /// <summary>中文走爱发电；英文暂无可用收款页则返回 null。</summary>
    public static string? ForLanguage(string? language)
        => language == "zh-CN" ? Afdian : null;
}
