namespace ChanJing.Core.Services;

/// <summary>
/// 场景管理：预设定义 + 自定义配置读写（数据库持久化）。
/// 从 MainPage 抽出，供首页/屏蔽页/托盘/统计共用，避免场景逻辑散落多处。
/// </summary>
public static class SceneManager
{
    /// <summary>场景配置记录：愿望+时长+屏蔽分类。</summary>
    public sealed record SceneConfig(string Wish, int Minutes, string[] Categories);

    /// <summary>场景预设：愿望 + 时长 + 屏蔽分类。点场景按钮一键应用，无需去屏蔽页手动设置。</summary>
    public static readonly IReadOnlyDictionary<string, (string Wish, int Minutes, string[] Categories)> ScenePresets =
        new Dictionary<string, (string, int, string[])>
        {
            ["work"] = ("完成今日工作任务", 50, new[] { "短视频", "视频娱乐", "购物" }),
            ["write"] = ("专注写作，心无旁骛", 45, new[] { "短视频", "视频娱乐", "社交", "购物", "资讯" }),
            ["study"] = ("深度学习，理解透彻", 25, new[] { "短视频", "视频娱乐", "社交", "购物", "资讯" }),
            ["meeting"] = ("专注会议，高效沟通", 30, new[] { "短视频", "视频娱乐" }),
        };

    /// <summary>场景标签 → 中文名。</summary>
    public static string GetSceneName(string tag) => tag switch
    {
        "work" => "工作",
        "write" => "写作",
        "study" => "学习",
        "meeting" => "会议",
        _ => tag
    };

    /// <summary>读取场景配置：优先用户自定义，没有则用预设默认值。</summary>
    public static SceneConfig GetSceneConfig(AppDatabase db, string tag)
    {
        if (ScenePresets.TryGetValue(tag, out var preset))
        {
            var raw = db.GetSetting("scene_config_" + tag);
            if (!string.IsNullOrEmpty(raw))
            {
                try
                {
                    using var json = System.Text.Json.JsonDocument.Parse(raw);
                    var wish = json.RootElement.TryGetProperty("wish", out var w) ? w.GetString() ?? preset.Wish : preset.Wish;
                    var minutes = json.RootElement.TryGetProperty("minutes", out var m) ? m.GetInt32() : preset.Minutes;
                    var categories = json.RootElement.TryGetProperty("categories", out var c)
                        ? c.EnumerateArray().Select(x => x.GetString()).Where(s => !string.IsNullOrEmpty(s)).ToArray()
                        : preset.Categories;
                    return new SceneConfig(wish, minutes, categories!);
                }
                catch { /* JSON解析失败，回退预设 */ }
            }
            return new SceneConfig(preset.Wish, preset.Minutes, preset.Categories);
        }
        return new SceneConfig("", 25, Array.Empty<string>());
    }

    /// <summary>保存场景自定义配置到本地数据库。</summary>
    public static void SaveSceneConfig(AppDatabase db, string tag, SceneConfig config)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            wish = config.Wish,
            minutes = config.Minutes,
            categories = config.Categories
        });
        db.SetSetting("scene_config_" + tag, json);
    }

    /// <summary>重置场景配置为预设默认值（删除用户自定义）。</summary>
    public static void ResetSceneConfig(AppDatabase db, string tag)
    {
        db.SetSetting("scene_config_" + tag, "");
    }

    /// <summary>已自定义的场景数量（用于免费版 1 个自定义场景额度）。</summary>
    public static int GetCustomSceneCount(AppDatabase db) =>
        ScenePresets.Keys.Count(tag => !string.IsNullOrEmpty(db.GetSetting("scene_config_" + tag)));

    /// <summary>该场景是否已被用户主动自定义（scene_config_<tag> 非空）。用于区分"自动记忆"与"主动自定义"。</summary>
    public static bool IsCustomized(AppDatabase db, string tag) =>
        !string.IsNullOrEmpty(db.GetSetting("scene_config_" + tag));

    /// <summary>当前场景摘要（如「工作 · 50分钟 · 屏蔽3类」），无场景返回 null。</summary>
    public static string? GetCurrentSceneSummary(AppDatabase db, string? currentTag)
    {
        if (string.IsNullOrEmpty(currentTag)) return null;
        var config = GetSceneConfig(db, currentTag);
        return $"{GetSceneName(currentTag)} · {config.Minutes}分钟 · 屏蔽{config.Categories.Length}类";
    }
}
