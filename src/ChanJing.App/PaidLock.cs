using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChanJing_App;

/// <summary>免费额度用尽：变灰 + 锁标 + hover，控件仍可点出升级说明。</summary>
internal static class PaidLock
{
    public static void Apply(FrameworkElement element, bool locked, string tooltip)
    {
        // 只降透明度+tooltip，不禁用：点击仍能弹出升级说明
        element.Opacity = locked ? 0.45 : 1;
        ToolTipService.SetToolTip(element, locked ? tooltip : null);
    }

    public static void Prefix(Button button, string baseText, bool locked)
    {
        button.Content = locked ? "🔒 " + baseText : baseText;
    }

    public static void Prefix(TextBlock text, string baseText, bool locked)
    {
        text.Text = locked ? "🔒 " + baseText : baseText;
    }

    public static void Prefix(ComboBoxItem item, string baseText, bool locked)
    {
        item.Content = locked ? "🔒 " + baseText : baseText;
    }
}
