using System.Security.Principal;
using ChanJing.Core.Services;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.UI.Xaml.Controls;

namespace ChanJing_App;

/// <summary>
/// 国际化：从 PRI 的 Resources 子树按 language.txt 显式取词。
/// 未打包进程里默认 ResourceLoader() 经常拿不到字符串，只会落到 fallback。
/// </summary>
public static class I18n
{
    /// <summary>当前进程是否已提权。未提权时网站 hosts 屏蔽会跳过，桌面应用拦截仍可用。</summary>
    public static bool IsElevated()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public static string SceneName(string tag) => tag switch
    {
        "work" => Get("MainPage_SceneWork.Content", "Work"),
        "write" => Get("MainPage_SceneWrite.Content", "Write"),
        "study" => Get("MainPage_SceneStudy.Content", "Study"),
        "meeting" => Get("MainPage_SceneMeeting.Content", "Meeting"),
        _ => tag
    };

    /// <summary>分类存库仍用中文 key，展示时再翻译。</summary>
    public static string CategoryName(string key) => key switch
    {
        "短视频" => Get("ShieldPage_AppCatShort.Content", "Short video"),
        "视频娱乐" => Get("ShieldPage_AppCatVideo.Content", "Video"),
        "社交" => Get("ShieldPage_AppCatSocial.Content", "Social"),
        "资讯" => Get("ShieldPage_AppCatNews.Content", "News"),
        "购物" => Get("ShieldPage_AppCatShop.Content", "Shopping"),
        "沟通工具" => Get("ShieldPage_AppCatComm.Content", "Messaging"),
        _ => key
    };

    public static string CategoriesText(IEnumerable<string> cats) =>
        string.Join("/", cats.Select(CategoryName));

    public static string SceneSummary(string tag, int minutes, int catCount) =>
        GetFormat("Scene_Summary", SceneName(tag), minutes, catCount);

    public static string SceneWish(string tag) => tag switch
    {
        "work" => Get("SceneWish_Work", "Finish today's work"),
        "write" => Get("SceneWish_Write", "Write with a quiet mind"),
        "study" => Get("SceneWish_Study", "Study deeply, understand thoroughly"),
        "meeting" => Get("SceneWish_Meeting", "Stay present in the meeting"),
        _ => Get("SceneWish_Default", "Focus")
    };

    /// <summary>预设愿望（含中文原文与当前语言译文）展示时翻译；用户自拟文案原样显示。</summary>
    public static string DisplayWish(string? tag, string? stored)
    {
        if (MatchesPreset(stored, "专注") || stored == Get("SceneWish_Default", "Focus"))
        {
            if (string.IsNullOrEmpty(tag)) return Get("SceneWish_Default", "Focus");
        }
        if (!string.IsNullOrEmpty(tag) && SceneManager.ScenePresets.TryGetValue(tag, out var preset))
        {
            if (string.IsNullOrEmpty(stored) || MatchesPreset(stored, preset.Wish) || stored == SceneWish(tag))
                return SceneWish(tag);
        }
        if (!string.IsNullOrEmpty(stored))
        {
            foreach (var kv in SceneManager.ScenePresets)
            {
                if (MatchesPreset(stored, kv.Value.Wish) || stored == SceneWish(kv.Key))
                    return SceneWish(kv.Key);
            }
        }
        return stored ?? "";
    }

    /// <summary>预设愿望存回中文原文，切语言后还能对上。</summary>
    public static string StoreWish(string? tag, string? displayed)
    {
        var text = displayed?.Trim() ?? "";
        if (!string.IsNullOrEmpty(tag) && SceneManager.ScenePresets.TryGetValue(tag, out var preset))
        {
            if (string.IsNullOrEmpty(text) || MatchesPreset(text, preset.Wish) || text == SceneWish(tag))
                return preset.Wish;
        }
        foreach (var kv in SceneManager.ScenePresets)
        {
            if (MatchesPreset(text, kv.Value.Wish) || text == SceneWish(kv.Key))
                return kv.Value.Wish;
        }
        if (MatchesPreset(text, "专注") || text == Get("SceneWish_Default", "Focus"))
            return "专注";
        return text;
    }

    public static string AppName(string process)
    {
        var key = "AppName_" + process;
        var localized = Get(key, "");
        if (!string.IsNullOrEmpty(localized) && localized != key) return localized;
        return BlocklistService.GetAppDisplayName(process);
    }

    private static bool MatchesPreset(string? value, string canonical) =>
        !string.IsNullOrEmpty(value) && string.Equals(value, canonical, StringComparison.Ordinal);

    public static string FocusFeedback(int actualMinutes, bool completed, int distractionCount, int? yesterdayAvg)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(completed
            ? GetFormat("Feedback_Done", actualMinutes)
            : GetFormat("Feedback_Broken", actualMinutes));
        if (distractionCount == 0)
            sb.Append(Get("Feedback_NoDist", ", no switches. Keep going."));
        else if (distractionCount == 1)
            sb.Append(Get("Feedback_Dist1", ", 1 distraction — try putting the phone farther next time."));
        else
            sb.Append(GetFormat("Feedback_DistN", distractionCount));
        if (yesterdayAvg is int avg)
        {
            var diff = actualMinutes - avg;
            if (diff > 0) sb.Append(GetFormat("Feedback_Better", diff));
            else if (diff == 0) sb.Append(Get("Feedback_Same", " Same as yesterday's average — steady is progress."));
        }
        return sb.ToString();
    }

    private static ResourceManager? _manager;
    private static ResourceMap? _map;

    private static void EnsureMap()
    {
        if (_map is not null) return;
        var pri = "";
        try { pri = ResourceLoader.GetDefaultResourceFilePath(); } catch { }
        _manager = (!string.IsNullOrEmpty(pri) && File.Exists(pri))
            ? new ResourceManager(pri)
            : new ResourceManager();
        _map = _manager.MainResourceMap.TryGetSubtree("Resources") ?? _manager.MainResourceMap;
        App.LogAction("i18n-pri", string.IsNullOrEmpty(pri) ? "(default)" : pri);
    }

    /// <summary>获取资源字符串。key 格式如 "MainPage_StartButton.Content"。</summary>
    public static string Get(string key, string? fallback = null)
    {
        try
        {
            EnsureMap();
            var ctx = _manager!.CreateResourceContext();
            ctx.QualifierValues["Language"] = App.GetLanguage();
            var candidate = _map!.TryGetValue(key, ctx)
                            ?? _map.TryGetValue(key.Replace('.', '/'), ctx);
            var value = candidate?.ValueAsString;
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

    /// <summary>把资源写到控件。x:Uid 遇到本地 Content="" 会被盖掉，按钮必须走这里。</summary>
    public static void SetContent(object? target, string key, string fallback)
    {
        var text = Get(key, fallback);
        switch (target)
        {
            case Button b: b.Content = text; break;
            case NavigationViewItem n: n.Content = text; break;
            case ComboBoxItem c: c.Content = text; break;
            case MenuFlyoutItem m: m.Text = text; break;
            case TextBlock t: t.Text = text; break;
            case TextBox tb: tb.PlaceholderText = text; break;
            case ToggleSwitch ts when key.EndsWith(".Header", StringComparison.Ordinal): ts.Header = text; break;
            case ToggleSwitch ts when key.EndsWith(".OnContent", StringComparison.Ordinal): ts.OnContent = text; break;
            case ToggleSwitch ts when key.EndsWith(".OffContent", StringComparison.Ordinal): ts.OffContent = text; break;
            case Expander ex: ex.Header = text; break;
            case NumberBox nb: nb.Header = text; break;
        }
    }
}
