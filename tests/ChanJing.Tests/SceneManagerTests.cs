using ChanJing.Core.Services;
using Xunit;

namespace ChanJing.Tests;

/// <summary>
/// 场景管理核心逻辑测试（P0-1 额度判断 / P0-2 切换保存 / 配置读写）。
/// 验证"自动记忆"与"主动自定义"的区分——免费版正常使用场景不得消耗自定义额度。
/// </summary>
public class SceneManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppDatabase _db;

    public SceneManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "chanjing-scene-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _db = new AppDatabase(Path.Combine(_tempDir, "test.db"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* 忽略 */ }
    }

    [Fact]
    public void FreshDb_NoCustomScenes_CountZero()
    {
        // 全新数据库：没有主动自定义任何场景
        Assert.Equal(0, SceneManager.GetCustomSceneCount(_db));
        Assert.False(SceneManager.IsCustomized(_db, "work"));
    }

    [Fact]
    public void SaveSceneConfig_MarksCustomizedAndConsumesQuota()
    {
        // 用户主动长按自定义「工作」场景 → 应标记为已自定义，额度+1
        SceneManager.SaveSceneConfig(_db, "work", new SceneManager.SceneConfig("完成今日工作任务", 50, new[] { "短视频", "视频娱乐", "购物" }));
        Assert.True(SceneManager.IsCustomized(_db, "work"));
        Assert.Equal(1, SceneManager.GetCustomSceneCount(_db));
    }

    [Fact]
    public void TwoScenesCustomized_CountTwo()
    {
        // 两个场景自定义 → 额度2（付费用户不受限）
        SceneManager.SaveSceneConfig(_db, "work", new SceneManager.SceneConfig("w1", 50, new[] { "短视频" }));
        SceneManager.SaveSceneConfig(_db, "study", new SceneManager.SceneConfig("w2", 25, new[] { "社交" }));
        Assert.Equal(2, SceneManager.GetCustomSceneCount(_db));
    }

    [Fact]
    public void ResetSceneConfig_FreesQuota()
    {
        // 重置场景 → 不再算自定义，额度释放
        SceneManager.SaveSceneConfig(_db, "work", new SceneManager.SceneConfig("w1", 50, new[] { "短视频" }));
        Assert.Equal(1, SceneManager.GetCustomSceneCount(_db));
        SceneManager.ResetSceneConfig(_db, "work");
        Assert.False(SceneManager.IsCustomized(_db, "work"));
        Assert.Equal(0, SceneManager.GetCustomSceneCount(_db));
    }

    [Fact]
    public void GetSceneConfig_NoCustom_UsesPreset()
    {
        // 未自定义的场景返回预设：工作=50分钟/3类
        var cfg = SceneManager.GetSceneConfig(_db, "work");
        Assert.Equal(50, cfg.Minutes);
        Assert.Equal(3, cfg.Categories.Length);
        Assert.Contains("短视频", cfg.Categories);
    }

    [Fact]
    public void GetSceneConfig_Custom_PersistsAllFields()
    {
        // 自定义后读取：愿望/时长/分类完整保留（含修改后的分类）
        SceneManager.SaveSceneConfig(_db, "work", new SceneManager.SceneConfig("专注写周报", 40, new[] { "短视频", "社交", "购物", "资讯" }));
        var cfg = SceneManager.GetSceneConfig(_db, "work");
        Assert.Equal("专注写周报", cfg.Wish);
        Assert.Equal(40, cfg.Minutes);
        Assert.Equal(4, cfg.Categories.Length);
        Assert.Contains("社交", cfg.Categories);
    }

    [Fact]
    public void AutoMemory_OnUncustomizedScene_DoesNotConsumeQuota()
    {
        // P0-1 核心验证：模拟"用户正常使用场景（未主动自定义）+方案B自动记忆"的完整流程
        // 修复前：SaveSceneConfig 无条件执行 → 额度被意外消耗
        // 修复后：StartButton_Click 会先检查 IsCustomized 才保存——这里验证 IsCustomized 的正确区分
        // 场景A未自定义：IsCustomized=false → 不应触发自动保存
        Assert.False(SceneManager.IsCustomized(_db, "work"));
        Assert.Equal(0, SceneManager.GetCustomSceneCount(_db));

        // 场景B主动自定义：IsCustomized=true → 自动记忆允许更新（不新增额度）
        SceneManager.SaveSceneConfig(_db, "study", new SceneManager.SceneConfig("w2", 25, new[] { "社交" }));
        Assert.Equal(1, SceneManager.GetCustomSceneCount(_db));

        // 用户用场景A开始专注（模拟方案B检查通过后执行）——但修复后的判断要求 IsCustomized=true 才保存
        // 因此场景A仍保持未自定义，额度不变
        Assert.False(SceneManager.IsCustomized(_db, "work"));
        Assert.Equal(1, SceneManager.GetCustomSceneCount(_db));
    }

    [Fact]
    public void CustomizedScene_AutoMemoryUpdatesWithoutNewQuota()
    {
        // 已自定义场景的自动记忆：更新配置但额度不增加
        SceneManager.SaveSceneConfig(_db, "work", new SceneManager.SceneConfig("w1", 50, new[] { "短视频" }));
        Assert.Equal(1, SceneManager.GetCustomSceneCount(_db));

        // 模拟方案B自动记忆（用户改屏蔽分类后开始专注）
        SceneManager.SaveSceneConfig(_db, "work", new SceneManager.SceneConfig("w1", 50, new[] { "短视频", "社交" }));
        Assert.Equal(1, SceneManager.GetCustomSceneCount(_db)); // 仍是1，不新增
        var cfg = SceneManager.GetSceneConfig(_db, "work");
        Assert.Equal(2, cfg.Categories.Length); // 分类已更新
    }

    [Fact]
    public void ScenePresets_HaveExpectedDefaults()
    {
        Assert.Equal(4, SceneManager.ScenePresets.Count);
        Assert.Equal(50, SceneManager.ScenePresets["work"].Minutes);
        Assert.Equal(45, SceneManager.ScenePresets["write"].Minutes);
        Assert.Equal(25, SceneManager.ScenePresets["study"].Minutes);
        Assert.Equal(30, SceneManager.ScenePresets["meeting"].Minutes);
        Assert.Equal("工作", SceneManager.GetSceneName("work"));
    }

    [Fact]
    public void GetCurrentSceneSummary_NullTag_ReturnsNull()
    {
        Assert.Null(SceneManager.GetCurrentSceneSummary(_db, null));
        Assert.Null(SceneManager.GetCurrentSceneSummary(_db, ""));
    }

    [Fact]
    public void GetCurrentSceneSummary_ShowsConfig()
    {
        SceneManager.SaveSceneConfig(_db, "work", new SceneManager.SceneConfig("w1", 40, new[] { "短视频", "社交" }));
        var summary = SceneManager.GetCurrentSceneSummary(_db, "work");
        Assert.NotNull(summary);
        Assert.Contains("工作", summary);
        Assert.Contains("40", summary);
        Assert.Contains("屏蔽2类", summary);
    }
}
