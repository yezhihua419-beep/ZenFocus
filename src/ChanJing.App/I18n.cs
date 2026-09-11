using Microsoft.Windows.ApplicationModel.Resources;

namespace ChanJing_App;

/// <summary>
/// 国际化辅助类：封装 ResourceLoader，提供简单的字符串获取接口。
/// 默认语言 en-US，跟随系统语言自动切换。
/// </summary>
public static class I18n
{
    private static readonly ResourceLoader _loader = new();

    /// <summary>获取资源字符串。key 格式如 "MainPage_StartButton.Content"。</summary>
    public static string Get(string key, string? fallback = null)
    {
        try
        {
            var value = _loader.GetString(key);
            return string.IsNullOrEmpty(value) ? (fallback ?? key) : value;
        }
        catch
        {
            return fallback ?? key;
        }
    }

    /// <summary>获取资源字符串，带格式化参数。</summary>
    public static string GetFormat(string key, params object[] args)
    {
        var template = Get(key);
        try { return string.Format(template, args); }
        catch { return template; }
    }
}
