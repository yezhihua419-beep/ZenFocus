using System.Diagnostics;
using ChanJing.Core.Services;

namespace ChanJing_App;

/// <summary>有收银台才开浏览器；英文暂无链接则不跳。</summary>
internal static class Checkout
{
    public static string? PayUrl() => CheckoutLinks.ForLanguage(App.GetLanguage());

    public static bool TryOpen()
    {
        var url = PayUrl();
        if (string.IsNullOrEmpty(url)) return false;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        return true;
    }
}
