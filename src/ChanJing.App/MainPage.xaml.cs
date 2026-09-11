using System.Globalization;
using ChanJing.Core.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Input;

namespace ChanJing_App;

/// <summary>
/// 禅定首页：今日一愿 → 呼吸引导（3 秒）→ 正计时专注（无倒计时）→ 圆满/破功 → 即时反馈。
/// 抗焦虑设计：藏起剩余时间、破功用提问不审判、反馈用进步框架。
/// </summary>
public sealed partial class MainPage : Page
{
    private readonly FocusEngine _engine = AppServices.Engine;
    private readonly BlocklistService _blocklist = AppServices.Blocklist;
    private readonly AppDatabase _db = AppServices.Db;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _breathTimer;
    private string? _pendingWish;
    private int _pendingMinutes = 25;
    /// <summary>首页主视图状态机：所有Panel切换统一走 SetUiState，避免状态与视图不一致。</summary>
    private enum MainUiState { Idle, Breathing, Focusing, Cooldown, SoftLanding, Feedback }
    private MainUiState _uiState = MainUiState.Idle;
    private bool _deepMode;
    private bool _adhdMode;
    private bool _adhdOnboarded;
    private DispatcherTimer? _cooldownTimer;
    private int _cooldownRemaining;
    private bool _softTargetReached;
    private bool _softReminder30Shown;

    public MainPage()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
        _breathTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _breathTimer.Tick += OnBreathTick;
        Loaded += OnLoaded;
    }

    /// <summary>每次回到首页刷新统计与场景高亮（跨页面同步）。</summary>
    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        try
        {
            RestoreSceneHighlight();
            RefreshTodayStats();
            // 快捷键/托盘触发后回到首页，同步专注界面状态
            if (_engine.IsRunning)
            {
                EnterFocusView();
            }
            else
            {
                EnterIdleView();
            }
        }
        catch (Exception ex) { App.LogCrash("MainPage.OnNavigatedTo", ex); }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            DateText.Text = DateTime.Today.ToString("M 月 d 日 dddd",
                CultureInfo.GetCultureInfo("zh-CN"));
            WishBox.Text = _db.GetSetting("today_wish") ?? string.Empty;
            RestoreSceneHighlight(); // 回到首页时恢复场景高亮（跨页面同步）
            RefreshTodayStats();
            GenerateCompanionQrCode();

            if (_engine.IsRunning) EnterFocusView();
            else EnterIdleView();

            ShowFirstRunGuideIfNeeded();
        }
        catch (Exception ex)
        {
            App.LogCrash("MainPage.OnLoaded", ex);
        }
    }

    /// <summary>首启3步引导：显示浮层卡片（专注 → 屏蔽 → 统计与托盘），关闭后不再出现。</summary>
    private void ShowFirstRunGuideIfNeeded()
    {
        try
        {
            if (_db.GetSetting("onboarded") is not null) return;
            GuidePanel.Visibility = Visibility.Visible;
        }
        catch (Exception ex) { App.LogCrash("MainPage.FirstRunGuide", ex); }
    }

    private void GuideClose_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _db.SetSetting("onboarded", "true");
            GuidePanel.Visibility = Visibility.Collapsed;
            App.LogAction("首启引导", "开始使用");
        }
        catch (Exception ex) { App.LogCrash("MainPage.GuideClose", ex); }
    }
    private void OnTick(object? sender, object e)
    {
        var elapsed = _engine.Elapsed;
        var minutes = (int)elapsed.TotalMinutes;
        TimerText.Text = $"{minutes} 分钟";
        // 非深度模式：更新进度条
        if (!_deepMode && _engine.Current?.PlannedMinutes > 0)
        {
            FocusProgress.Maximum = _engine.Current.PlannedMinutes;
            FocusProgress.Value = Math.Min(minutes, _engine.Current.PlannedMinutes);
        }
        // ADHD模式：15分钟软目标到达提示，30分钟软提醒
        if (_adhdMode && _engine.IsRunning && !_deepMode)
        {
            if (minutes >= 15 && !_softTargetReached)
            {
                _softTargetReached = true;
                WishShow.Text = "已超过15分钟，状态不错，随时可结束";
                WishShow.Foreground = (Brush)Application.Current.Resources["BrushAccent"];
            }
            if (minutes >= 30 && !_softReminder30Shown)
            {
                _softReminder30Shown = true;
                AppServices.Notify("已经专注30分钟了，注意休息一下哦");
            }
        }
    }

    // ---------- 开始流程：存愿 → 呼吸 → 专注 ----------

    private void DeepMode_Toggled(object sender, RoutedEventArgs e)
    {
        _deepMode = DeepModeSwitch.IsOn;
            AppServices.DeepMode = _deepMode;
        if (_deepMode)
        {
            // 与ADHD互斥：开启深度自动关闭ADHD
            if (_adhdMode)
            {
                _adhdMode = false;
                AdhdModeSwitch.IsOn = false;
            }
            SessionHint.Text = "深度模式 · 不计时 · 随心而定 · 手动结束";
        }
        else
        {
            SessionHint.Text = $"{_pendingMinutes} 分钟定心 · 正计时 · 心无旁骛";
        }
    }

    private void AdhdMode_Toggled(object sender, RoutedEventArgs e)
    {
        _adhdMode = AdhdModeSwitch.IsOn;
        if (_adhdMode)
        {
            // 与深度模式互斥：开启ADHD自动关闭深度模式
            if (_deepMode)
            {
                _deepMode = false;
                DeepModeSwitch.IsOn = false;
                AppServices.DeepMode = false;
            }
            _pendingMinutes = 15;
            SessionHint.Text = "ADHD友好模式 · 15分钟短周期 · 结束进程强屏蔽 · 正反馈鼓励";
            // 自动切换拦截方式为结束进程（如果当前不是）
            if (_blocklist.GetAppBlockMode() != "kill")
            {
                _blocklist.SetAppBlockMode("kill");
                AppServices.Notify("ADHD模式已开启：已自动切换为结束进程强屏蔽，15分钟短周期。屏蔽分类跟随当前场景，可在屏蔽页修改。");
            }
            else
            {
                AppServices.Notify("ADHD模式已开启：15分钟短周期·结束进程强屏蔽·正反馈鼓励");
            }
            // 首次开启ADHD引导
            if (!_adhdOnboarded && _db.GetSetting("adhd_onboarded") is null)
            {
                _ = ShowAdhdOnboarding();
            }
        }
        else
        {
            // 关闭ADHD：不恢复拦截方式（用户的选择保留），时长恢复场景预设
            _pendingMinutes = _currentSceneTag != null ? SceneManager.GetSceneConfig(_db, _currentSceneTag).Minutes : 25;
            SessionHint.Text = $"{_pendingMinutes} 分钟定心 · 正计时 · 心无旁骛";
        }
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _pendingWish = WishBox.Text?.Trim();
            if (!string.IsNullOrEmpty(_pendingWish))
            {
                _db.SetSetting("today_wish", _pendingWish);
            }
            App.LogAction("开始专注", _pendingWish is { Length: > 0 } ? $"愿：{_pendingWish}" : "无愿");
            // 同步当前愿望/时长到 AppServices，FocusStarted 时统一保存到场景（首页/托盘/伴侣页所有路径一致）
            AppServices.CurrentWish = _pendingWish;
            AppServices.CurrentMinutes = _deepMode ? 0 : _pendingMinutes;

            StartBreathing();
        }
        catch (Exception ex)
        {
            App.LogCrash("MainPage.Start", ex);
        }
    }



    /// <summary>当前选中的场景标签，用于UI高亮和专注界面显示。</summary>
    private string? _currentSceneTag;



    /// <summary>场景快捷选择：自动填充愿望、预设时长、应用该场景的屏蔽配置。</summary>
    private void Scene_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var tag = (sender as Button)?.Tag?.ToString();
            if (string.IsNullOrEmpty(tag) || !SceneManager.ScenePresets.TryGetValue(tag, out var preset))
            {
                App.LogAction("选择场景", "未知场景: " + tag);
                return;
            }

            // 切换场景前，若旧场景已自定义，保存当前配置到旧场景（避免屏蔽修改后切换场景数据丢失）
            if (!string.IsNullOrEmpty(_currentSceneTag) && _currentSceneTag != tag && SceneManager.IsCustomized(_db, _currentSceneTag))
            {
                var oldCategories = _blocklist.GetEnabledCategories().ToArray();
                SceneManager.SaveSceneConfig(_db, _currentSceneTag, new SceneManager.SceneConfig(WishBox.Text?.Trim() ?? "", _pendingMinutes, oldCategories));
                App.LogAction("场景切换前保存", $"{_currentSceneTag} {_pendingMinutes}分钟 屏蔽=[{string.Join("/", oldCategories)}]");
            }

            _currentSceneTag = tag;
            AppServices.CurrentSceneTag = tag; // 跨页面共享：屏蔽页顶部显示当前场景
            var config = SceneManager.GetSceneConfig(_db, tag);
            WishBox.Text = config.Wish;
            _pendingMinutes = config.Minutes;
            AppServices.CurrentWish = config.Wish;
            AppServices.CurrentMinutes = config.Minutes;

            // 更新时长显示（用户可见）
            SessionHint.Text = $"{config.Minutes} 分钟定心 · 正计时 · 心无旁骛";

            // 一键应用该场景的屏蔽分类（专注开始时自动生效）
            _blocklist.SetEnabledCategories(config.Categories);
            _blocklist.Apply();

            // 更新场景按钮高亮状态
            foreach (var btn in new[] { SceneWork, SceneWrite, SceneStudy, SceneMeeting })
            {
                    var isActive = btn.Tag?.ToString() == tag;
                    btn.Background = isActive ? new SolidColorBrush(ColorHelper.FromArgb(255, 110, 127, 99)) : new SolidColorBrush(Colors.Transparent);
                    btn.Foreground = isActive ? new SolidColorBrush(Colors.White) : (Brush)Application.Current.Resources["BrushTextSecondary"];
                    btn.BorderBrush = isActive ? new SolidColorBrush(ColorHelper.FromArgb(255, 110, 127, 99)) : (Brush)Application.Current.Resources["BrushTextSecondary"];
            }

            // 更新场景配置摘要提示
            var sceneName = SceneManager.GetSceneName(tag);
            SceneConfigHint.Text = $"{sceneName} · {config.Minutes}分钟 · 屏蔽{config.Categories.Length}类（{string.Join("/", config.Categories)}） · 右键可自定义";

            App.LogAction("选择场景", $"{tag} {config.Minutes}分钟 屏蔽=[{string.Join("/", config.Categories)}]");
        }
        catch (Exception ex)
        {
            App.LogCrash("MainPage.Scene", ex);
        }
    }

    /// <summary>长按场景按钮：付费版弹出自定义配置小窗，免费版提示升级。</summary>
    private void Scene_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        try
        {
            var tag = (sender as Button)?.Tag?.ToString();
            if (string.IsNullOrEmpty(tag)) return;

            // 免费版开放 1 个自定义场景额度，超出后提示升级
            if (!_blocklist.IsActivated() && SceneManager.GetCustomSceneCount(_db) >= 1)
            {
                _ = ShowSceneUpgradeHint(tag);
                return;
            }

            _ = ShowSceneConfigDialog(tag);
        }
        catch (Exception ex)
        {
            App.LogCrash("MainPage.Scene_RightTapped", ex);
        }
    }

    /// <summary>免费版长按场景按钮时的升级提示。</summary>
    private async Task ShowSceneUpgradeHint(string tag)
    {
        var sceneName = SceneManager.GetSceneName(tag);

        var dialog = new ContentDialog
        {
            Title = $"自定义「{sceneName}」场景",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "自定义场景额度已用完", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    new TextBlock { Text = "免费版可自定义 1 个场景（时长+屏蔽分类+愿望文案），当前额度已用完。", TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = "升级后可自定义全部 4 个场景。", TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = "¥68 买断，永久使用。", Foreground = (Brush)Application.Current.Resources["BrushAccent"] }
                }
            },
            PrimaryButtonText = "了解升级",
            CloseButtonText = "取消",
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            App.LogAction("场景自定义升级提示", $"{tag} 用户点击了解升级");
            // TODO: 跳转到升级页面（支付上线后接入）
        }
    }

    /// <summary>显示场景自定义配置对话框：时长+屏蔽分类+愿望文案。</summary>
    private async Task ShowSceneConfigDialog(string tag)
    {
        var config = SceneManager.GetSceneConfig(_db, tag);
        var sceneName = SceneManager.GetSceneName(tag);

        var minutesCombo = new ComboBox
        {
            Header = "专注时长（分钟）",
            Items = { 15, 25, 30, 45, 50, 60, 90 },
            SelectedItem = config.Minutes
        };

        var allCategories = new[] { "短视频", "视频娱乐", "社交", "购物", "资讯", "沟通工具" };
        var categoryPanel = new StackPanel { Spacing = 6 };
        var categoryCheckboxes = new List<CheckBox>();
        foreach (var cat in allCategories)
        {
            var cb = new CheckBox
            {
                Content = cat,
                IsChecked = config.Categories.Contains(cat)
            };
            categoryCheckboxes.Add(cb);
            categoryPanel.Children.Add(cb);
        }

        var wishBox = new TextBox
        {
            Header = "愿望文案",
            Text = config.Wish,
            PlaceholderText = "此刻，你最想完成的一件事…"
        };

        var content = new StackPanel { Spacing = 16, MaxWidth = 360 };
        content.Children.Add(minutesCombo);
        content.Children.Add(new TextBlock { Text = "屏蔽分类", Foreground = (Brush)Application.Current.Resources["BrushTextSecondary"], FontSize = 12 });
        content.Children.Add(categoryPanel);
        content.Children.Add(wishBox);

        var usedQuota = SceneManager.GetCustomSceneCount(_db);
        var quotaText = _blocklist.IsActivated() ? "" : $"（免费版已用{usedQuota}/1个自定义额度）";
        var dialog = new ContentDialog
        {
            Title = $"自定义「{sceneName}」场景{quotaText}",
            PrimaryButtonText = "保存",
            SecondaryButtonText = "重置默认",
            CloseButtonText = "取消",
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var selectedMinutes = (int)(minutesCombo.SelectedItem ?? 25);
            var selectedCategories = categoryCheckboxes.Where(cb => cb.IsChecked == true)
                .Select(cb => cb.Content.ToString()!).ToArray();
            var newWish = wishBox.Text.Trim();
            var newConfig = new SceneManager.SceneConfig(newWish, selectedMinutes, selectedCategories);
            SceneManager.SaveSceneConfig(_db, tag, newConfig);
            App.LogAction("自定义场景", $"{tag} {selectedMinutes}分钟 屏蔽=[{string.Join("/", selectedCategories)}]");

            if (_currentSceneTag == tag)
            {
                WishBox.Text = newWish;
                _pendingMinutes = selectedMinutes;
                SessionHint.Text = $"{selectedMinutes} 分钟定心 · 正计时 · 心无旁骛";
                _blocklist.SetEnabledCategories(selectedCategories);
            }
        }
        else if (result == ContentDialogResult.Secondary)
        {
            SceneManager.ResetSceneConfig(_db, tag);
            App.LogAction("重置场景", tag);

            if (_currentSceneTag == tag)
            {
                var preset = SceneManager.GetSceneConfig(_db, tag);
                WishBox.Text = preset.Wish;
                _pendingMinutes = preset.Minutes;
                SessionHint.Text = $"{preset.Minutes} 分钟定心 · 正计时 · 心无旁骛";
                _blocklist.SetEnabledCategories(preset.Categories);
            }
        }
    }
    private void StartBreathing()
    {
        SetUiState(MainUiState.Breathing);
        BreathText.Text = "吸气…";
        _breathTimer.Start();

        var storyboard = BuildBreathStoryboard();
        storyboard.Completed += (_, _) => FinishBreathing();
        storyboard.Begin();
    }

    private void OnBreathTick(object? sender, object e)
    {
        BreathText.Text = BreathText.Text == "吸气…" ? "呼气…" : "吸气…";
    }

    private void SkipBreath_Click(object sender, RoutedEventArgs e)
    {
        _breathTimer.Stop();
        FinishBreathing();
    }

    /// <summary>呼吸引导结束（自动或跳过），真正开始专注。</summary>
    private void FinishBreathing()
    {
        if (_uiState != MainUiState.Breathing) return;
        _breathTimer.Stop();
        var startMinutes = _deepMode ? 0 : _pendingMinutes;
        _softTargetReached = false;
        _softReminder30Shown = false;
        _engine.Start(_pendingWish, startMinutes, _adhdMode);
        App.LogAction("进入专注", _adhdMode ? "ADHD模式" : "普通模式");
        EnterFocusView();
    }

    /// <summary>呼吸动画：1.5 秒放大（吸气）+ 1.5 秒缩小（呼气），共 3 秒。</summary>
    private Storyboard BuildBreathStoryboard()
    {
        var sb = new Storyboard();

        void AddScale(string property)
        {
            var animation = new DoubleAnimationUsingKeyFrames();
            animation.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromSeconds(0), Value = 1.0 });
            animation.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromSeconds(1.5), Value = 1.15 });
            animation.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromSeconds(3), Value = 1.0 });
            Storyboard.SetTarget(animation, BreathScale);
            Storyboard.SetTargetProperty(animation, property);
            sb.Children.Add(animation);
        }

        AddScale("ScaleX");
        AddScale("ScaleY");
        return sb;
    }

    // ---------- 专注态 ----------

    private void StartAgain_Click(object sender, RoutedEventArgs e)
    {
        // 直接开始新专注（ADHD模式15分钟，普通模式场景时长）
        _pendingWish = WishBox.Text?.Trim();
        AppServices.CurrentWish = _pendingWish;
        AppServices.CurrentMinutes = _deepMode ? 0 : _pendingMinutes;
        StartBreathing();
    }

    /// <summary>正反馈界面点"先休息一下"→进入缓冲期（ADHD模式）。</summary>
    private void FeedbackRest_Click(object sender, RoutedEventArgs e)
    {
        if (_adhdMode)
        {
            StartCooldown();
        }
        else
        {
            EnterIdleView();
        }
    }

    private void Complete_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AppServices.Blocklist.EmergencyPass = false;
            var done = _engine.Finish(completed: true);
            App.LogAction("圆满结束", $"专注 {done.ActualMinutes} 分钟 分心 {done.DistractionCount} 次");
            if (_adhdMode)
            {
                ShowAdhdFeedback(done);
            }
            else
            {
                ShowFeedback(_engine.GenerateFeedback(done));
            }
        }
        catch (Exception ex)
        {
            App.LogCrash("MainPage.Complete", ex);
        }
    }

    /// <summary>计算今日连续完成次数（ADHD模式用）。破功或间隔超30分钟则断连。</summary>
    private int GetTodayStreak()
    {
        var today = DateTime.Today;
        var sessions = _db.GetSessions(today, today.AddDays(1))
            .Where(s => s.State == ChanJing.Core.Models.FocusSessionState.Completed)
            .OrderBy(s => s.StartedAt)
            .ToList();
        if (sessions.Count == 0) return 0;
        // 从最近一次往前数连续完成的（间隔不超过30分钟）
        var streak = 1;
        for (var i = sessions.Count - 1; i > 0; i--)
        {
            var gap = sessions[i].StartedAt - (sessions[i - 1].EndedAt ?? sessions[i - 1].StartedAt);
            if (gap <= TimeSpan.FromMinutes(30))
            {
                streak++;
            }
            else
            {
                break;
            }
        }
        return streak;
    }

    /// <summary>ADHD模式正反馈界面：连续次数+再来15分钟。</summary>
    private void ShowAdhdFeedback(ChanJing.Core.Models.FocusSession done)
    {
        _timer.Stop();
        SetUiState(MainUiState.Feedback);

        var streak = GetTodayStreak();
        FeedbackStreak.Text = streak > 1 ? $"✨ 今日已完成 {streak} 次定心" : "";
        FeedbackStreak.Visibility = streak > 1 ? Visibility.Visible : Visibility.Collapsed;

        var encouragement = streak >= 5 ? "心已定，功自成，继续保持" :
                           streak >= 3 ? "状态渐入佳境，继续保持" :
                           streak >= 1 ? "好的开始，念念不忘必有回响" : "";
        FeedbackText.Text = $"今日定心 {done.ActualMinutes} 分钟\n{encouragement}";
        FeedbackAgainButton.Content = "再来 15 分钟";
        FeedbackRestButton.Visibility = Visibility.Visible;
        RefreshTodayStats();
    }

    // ---------- 缓冲期（ADHD模式） ----------

    /// <summary>开始缓冲期：屏蔽保持N分钟，倒计时到了进入软着陆。</summary>
    private void StartCooldown()
    {
        _cooldownRemaining = _blocklist.GetCooldownMinutes(); // 从设置读取，默认10分钟
        SetUiState(MainUiState.Cooldown);
        UpdateCooldownText();

        _cooldownTimer?.Stop();
        _cooldownTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _cooldownTimer.Tick += (_, _) =>
        {
            _cooldownRemaining--;
            if (_cooldownRemaining <= 0)
            {
                _cooldownTimer?.Stop();
                EnterSoftLanding();
            }
            else
            {
                UpdateCooldownText();
            }
        };
        _cooldownTimer.Start();
        App.LogAction("缓冲期开始", "10分钟");
    }

    private void UpdateCooldownText()
    {
        CooldownText.Text = $"专注结束，休息一下\n屏蔽还剩 {_cooldownRemaining} 分钟";
    }

    /// <summary>缓冲期内点"再来15分钟"→取消缓冲，直接开始新专注。</summary>
    private void CooldownAgain_Click(object sender, RoutedEventArgs e)
    {
        _cooldownTimer?.Stop();
        CooldownPanel.Visibility = Visibility.Collapsed;
        StartBreathing();
    }

    /// <summary>缓冲期内点"提前解除屏蔽"→弹确认，确认后解除屏蔽返回首页。</summary>
    private async void CooldownRelease_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "提前解除屏蔽",
            Content = $"缓冲期还剩 {_cooldownRemaining} 分钟，确定现在解除屏蔽吗？",
            PrimaryButtonText = "确定解除",
            CloseButtonText = "再等等",
            XamlRoot = XamlRoot
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            _cooldownTimer?.Stop();
            CooldownPanel.Visibility = Visibility.Collapsed;
            // 解除屏蔽
            ChanJing.Core.Services.HostsBlocker.Remove();
            App.LogAction("缓冲期提前解除", "用户主动解除");
            EnterIdleView();
        }
    }

    // ---------- 软着陆（缓冲期到期） ----------

    /// <summary>进入软着陆：缓冲期到了，屏蔽仍保持，用户选择后才解除。</summary>
    private void EnterSoftLanding()
    {
        SetUiState(MainUiState.SoftLanding);
        App.LogAction("软着陆", "缓冲期到期，等待用户选择");
    }

    /// <summary>软着陆点"再来15分钟"→直接开始新专注。</summary>
    private void SoftLandingAgain_Click(object sender, RoutedEventArgs e)
    {
        SoftLandingPanel.Visibility = Visibility.Collapsed;
        StartBreathing();
    }

    /// <summary>软着陆点"我想自由使用"→解除屏蔽，返回首页。</summary>
    private void SoftLandingRelease_Click(object sender, RoutedEventArgs e)
    {
        SoftLandingPanel.Visibility = Visibility.Collapsed;
        ChanJing.Core.Services.HostsBlocker.Remove();
        App.LogAction("软着陆", "用户选择自由使用，解除屏蔽");
        EnterIdleView();
    }

    /// <summary>临时离开：暂停计时，暂停期间不计入专注时长。ADHD模式下弹确认（防止一去不回）。</summary>
    private async void PauseToggle_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_engine.IsPaused)
            {
                _engine.Resume();
                App.LogAction("继续专注");
                PauseButton.Content = "暂停";
                PauseButton.Foreground = (Microsoft.UI.Xaml.Media.Brush)App.Current.Resources["BrushTextSecondary"];
                TimerText.Text = $"{(int)_engine.Elapsed.TotalMinutes} 分钟";
            }
            else
            {
                // ADHD模式：暂停弹确认（防止一去不回）
                if (_adhdMode)
                {
                    var dialog = new ContentDialog
                    {
                        Title = "暂停专注",
                        Content = "确定要暂停吗？ADHD模式下暂停后容易一去不回，建议直接结束或继续。",
                        PrimaryButtonText = "还是暂停",
                        CloseButtonText = "继续专注",
                        XamlRoot = XamlRoot
                    };
                    var result = await dialog.ShowAsync();
                    if (result != ContentDialogResult.Primary) return;
                }
                _engine.Pause();
                App.LogAction("暂停专注");
                PauseButton.Content = "继续";
                PauseButton.Foreground = (Microsoft.UI.Xaml.Media.Brush)App.Current.Resources["BrushState"];
                TimerText.Text = "已暂停";
            }
        }
        catch (Exception ex)
        {
            App.LogCrash("MainPage.PauseToggle", ex);
        }
    }

    /// <summary>快捷放行（菜单选择时长）：当前前台网站临时放行 5/15/30 分钟（无需切到屏蔽页）。</summary>
    private async void AllowCurrentMenu_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var minutes = int.TryParse((sender as MenuFlyoutItem)?.Tag?.ToString(), out var m) ? m : 5;
            var domain = AppServices.Activity.GetCurrentBlockedDomain();
            if (domain is null)
            {
                var none = new ContentDialog
                {
                    Title = "没有可放行的网站",
                    Content = "当前前台没有正在被屏蔽的网站。先打开那个网站（如 bilibili.com），再点此按钮。",
                    CloseButtonText = "知道了",
                    XamlRoot = XamlRoot
                };
                await none.ShowAsync();
                return;
            }
            AppServices.Blocklist.AddTempAllow(domain, minutes);
            App.LogAction("快捷放行", $"{domain} {minutes}分钟");
            AppServices.Notify($"「{domain}」已临时放行 {minutes} 分钟，期间可正常访问。");
        }
        catch (Exception ex)
        {
            App.LogCrash("MainPage.AllowCurrent", ex);
        }
    }

    private async void EmergencyPass_Click(object sender, RoutedEventArgs e)
    {
        var restMinutes = _adhdMode ? 3 : 5;
        var restLabel = _adhdMode ? "喘口气" : "暂离模式";
        var dialog = new ContentDialog
        {
            Title = restLabel,
            Content = $"{restLabel} {restMinutes} 分钟，桌面应用（抖音/B站等）暂停拦截，网站屏蔽保持生效。期间仍记录专注时长。",
            PrimaryButtonText = "确认放行",
            CloseButtonText = "取消",
            XamlRoot = Content.XamlRoot
        };
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;
        App.LogAction(restLabel, $"桌面应用暂停拦截{restMinutes}分钟");
        AppServices.StartRestBreak(restMinutes);
    }

    private async void Break_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var remaining = 3;
            var hint = new TextBlock
            {
                Text = "深呼吸三次。禅净不会拦你——但你真的要现在结束吗？\n\n请等 3 秒再决定。",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)App.Current.Resources["BrushTextPrimary"]
            };
            var endButton = new Button
            {
                Content = $"结束（{remaining}）",
                IsEnabled = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(32, 10, 32, 10),
                Background = (Microsoft.UI.Xaml.Media.Brush)App.Current.Resources["BrushWarning"],
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                CornerRadius = new CornerRadius(24)
            };
            var panel = new StackPanel { Spacing = 14 };
            panel.Children.Add(hint);
            panel.Children.Add(endButton);

            var dialog = new ContentDialog
            {
                Title = "发生了什么？",
                Content = panel,
                PrimaryButtonText = "再定心一会儿",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            var cooldown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            cooldown.Tick += (_, _) =>
            {
                remaining--;
                if (remaining <= 0)
                {
                    cooldown.Stop();
                    endButton.IsEnabled = true;
                    endButton.Content = "结束";
                    hint.Text = "深呼吸三次。禅净不会拦你——但你真的要现在结束吗？";
                }
                else
                {
                    endButton.Content = $"结束（{remaining}）";
                }
            };
            cooldown.Start();
            endButton.Click += (_, _) =>
            {
                cooldown.Stop();
                dialog.Hide(); // 返回 None → 破功
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.None) // 用户确认结束
            {
                AppServices.Blocklist.EmergencyPass = false;
                App.LogAction("破功", $"专注 {_engine.Elapsed.TotalMinutes:0.#} 分钟");
                var done = _engine.Finish(completed: false);
                if (_adhdMode)
                {
                    // ADHD模式破功：显示正反馈界面（但连续次数会断连）
                    ShowAdhdFeedback(done);
                }
                else
                {
                    ShowFeedback(_engine.GenerateFeedback(done));
                }
            }
            else
            {
                App.LogAction("再定心", "破功对话框选择继续");
            }
        }
        catch (Exception ex)
        {
            App.LogCrash("MainPage.Break", ex);
        }
    }

    private void EnterFocusView()
    {
        _timer.Start();
        SetUiState(MainUiState.Focusing);
        WishShow.Text = _engine.Current?.Wish is { Length: > 0 } w
            ? $"今日一愿：{w}"
            : "心无旁骛，只做眼前这一件事";
        if (_deepMode)
        {
            // 深度模式：隐藏时间和进度条，只显示愿望；无计划时长，"放下"无意义，隐藏
            WishShow.Text = "深度模式 · 随心而定 · 完成后手动结束";
            TimerText.Visibility = Visibility.Collapsed;
            FocusProgress.Visibility = Visibility.Collapsed;
            BreakButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            TimerText.Visibility = Visibility.Visible;
            FocusProgress.Visibility = Visibility.Visible;
            BreakButton.Visibility = Visibility.Visible;
            TimerText.Text = "0 分钟";
            FocusProgress.Value = 0;
            if (_engine.Current?.PlannedMinutes > 0)
                FocusProgress.Maximum = _engine.Current.PlannedMinutes;
        }
    }

    private void EnterIdleView()
    {
        _timer.Stop();
        _cooldownTimer?.Stop();
        SetUiState(MainUiState.Idle);
    }

    private void ShowFeedback(string text)
    {
        _timer.Stop();
        SetUiState(MainUiState.Feedback);
        FeedbackText.Text = text;
        RefreshTodayStats();
    }

    /// <summary>统一状态切换：隐藏全部面板，只显示目标状态面板。</summary>
    private void SetUiState(MainUiState state)
    {
        _uiState = state;
        IdlePanel.Visibility = state == MainUiState.Idle ? Visibility.Visible : Visibility.Collapsed;
        BreathingPanel.Visibility = state == MainUiState.Breathing ? Visibility.Visible : Visibility.Collapsed;
        FocusPanel.Visibility = state == MainUiState.Focusing ? Visibility.Visible : Visibility.Collapsed;
        CooldownPanel.Visibility = state == MainUiState.Cooldown ? Visibility.Visible : Visibility.Collapsed;
        SoftLandingPanel.Visibility = state == MainUiState.SoftLanding ? Visibility.Visible : Visibility.Collapsed;
        FeedbackPanel.Visibility = state == MainUiState.Feedback ? Visibility.Visible : Visibility.Collapsed;
        // 微交互：专注结束的反馈界面"绽放"（淡入+轻微放大）
        if (state == MainUiState.Feedback) PlayFeedbackBloom();
    }

    /// <summary>反馈界面绽放动画：淡入 + 轻微放大（320ms）。</summary>
    private void PlayFeedbackBloom()
    {
        try
        {
            var fade = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(320))
            };
            var scale = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 0.96,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(320))
            };
            var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            sb.Children.Add(fade);
            sb.Children.Add(scale);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fade, FeedbackPanel);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fade, "Opacity");
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(scale, FeedbackPanel);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(scale, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)");
            sb.Begin();
        }
        catch { }
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        return $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
    }

    private void RefreshTodayStats()
    {
        var today = DateTime.Today;
        var sessions = _db.GetSessions(today, today.AddDays(1));
        TodayCount.Text = sessions.Count.ToString(CultureInfo.InvariantCulture);
        TodayMinutes.Text = sessions.Sum(s => s.ActualMinutes).ToString(CultureInfo.InvariantCulture);
        // 屏蔽规则状态：有当前场景时显示场景摘要，否则显示是否已保存配置
        var sceneSummary = SceneManager.GetCurrentSceneSummary(_db, AppServices.CurrentSceneTag);
        BlockStatus.Text = sceneSummary ?? (AppServices.Blocklist.IsApplied() ? "已启用" : "未启用");
    }

    /// <summary>回到首页时恢复上次选中的场景高亮与摘要（跨页面同步）。</summary>
    private void RestoreSceneHighlight()
    {
        try
        {
            var restoreTag = AppServices.CurrentSceneTag;
            if (string.IsNullOrEmpty(restoreTag)) return;

            _currentSceneTag = restoreTag;
            foreach (var btn in new[] { SceneWork, SceneWrite, SceneStudy, SceneMeeting })
            {
                    var isActive = btn.Tag?.ToString() == restoreTag;
                    btn.Background = isActive ? new SolidColorBrush(ColorHelper.FromArgb(255, 110, 127, 99)) : new SolidColorBrush(Colors.Transparent);
                    btn.Foreground = isActive ? new SolidColorBrush(Colors.White) : (Brush)Application.Current.Resources["BrushTextSecondary"];
                    btn.BorderBrush = isActive ? new SolidColorBrush(ColorHelper.FromArgb(255, 110, 127, 99)) : (Brush)Application.Current.Resources["BrushTextSecondary"];
            }

            var config = SceneManager.GetSceneConfig(_db, restoreTag);
            SceneConfigHint.Text = $"{SceneManager.GetSceneName(restoreTag)} · {config.Minutes}分钟 · 屏蔽{config.Categories.Length}类（{string.Join("/", config.Categories)}） · 右键可自定义";
        }
        catch (Exception ex)
        {
            App.LogCrash("MainPage.RestoreScene", ex);
        }
    }

    /// <summary>生成手机伴侣页二维码：手机伴侣为付费功能，免费版显示升级提示，激活后才生成二维码。</summary>
    private void GenerateCompanionQrCode()
    {
        try
        {
            // 免费版：不显示二维码，显示升级提示（付费钩子）
            if (!AppServices.Blocklist.IsActivated())
            {
                QrCodeBorder.Visibility = Visibility.Collapsed;
                CompanionTitleText.Text = "手机伴侣（付费功能）";
                CompanionDescText.Text = "升级后可手机扫码查看专注统计、远程开始/结束专注";
                ConnectUrlText.Text = "";
                CompanionUpgradeText.Visibility = Visibility.Visible;
                App.LogAction("伴侣二维码", "免费版显示升级提示");
                return;
            }

            // 付费版：显示二维码
            QrCodeBorder.Visibility = Visibility.Visible;
            CompanionTitleText.Text = "手机扫码连接";
            CompanionDescText.Text = "手机和电脑需在同一WiFi下，手机浏览器扫码，查看专注统计、远程开始/结束专注";
            CompanionUpgradeText.Visibility = Visibility.Collapsed;

            var server = AppServices.Companion;
            if (server == null || !server.IsRunning)
            {
                ConnectUrlText.Text = "伴侣服务未启动";
                return;
            }

            string url;
            if (server.IsLanAccess)
            {
                var ip = GetLocalIpAddress();
                if (string.IsNullOrEmpty(ip))
                {
                    ConnectUrlText.Text = "未检测到网络";
                    return;
                }
                url = $"http://{ip}:{server.Port}";
                ConnectUrlText.Text = $"{url}（手机和电脑需在同一WiFi下）";
            }
            else
            {
                url = $"http://localhost:{server.Port}";
                ConnectUrlText.Text = "需管理员权限运行才能让手机访问（当前仅本机）";
            }

            using var qrGenerator = new QRCoder.QRCodeGenerator();
            var qrCodeData = qrGenerator.CreateQrCode(url, QRCoder.QRCodeGenerator.ECCLevel.M);
            using var qrCode = new QRCoder.BitmapByteQRCode(qrCodeData);
            var qrBytes = qrCode.GetGraphic(10);

            var bitmap = new BitmapImage();
            using (var stream = new System.IO.MemoryStream(qrBytes))
            {
                bitmap.SetSource(stream.AsRandomAccessStream());
            }
            QrCodeImage.Source = bitmap;

            App.LogAction("伴侣二维码", $"生成成功 url={url}");
        }
        catch (Exception ex)
        {
            App.LogCrash("GenerateCompanionQrCode", ex);
            ConnectUrlText.Text = "二维码生成失败";
        }
    }

    /// <summary>获取本机局域网IPv4地址（排除回环和虚拟网卡）。</summary>
    private static string? GetLocalIpAddress()
    {
        try
        {
            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                var desc = ni.Description.ToLowerInvariant();
                if (desc.Contains("docker") || desc.Contains("vmware") || desc.Contains("virtualbox") ||
                    desc.Contains("hyper-v") || desc.Contains("wsl") || desc.Contains("tailscale")) continue;

                var props = ni.GetIPProperties();
                foreach (var addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        var ipStr = addr.Address.ToString();
                        if (!ipStr.StartsWith("169.254."))
                            return ipStr;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>首次开启ADHD模式的3步引导。</summary>
    private async Task ShowAdhdOnboarding()
    {
        try
        {
            _adhdOnboarded = true;
            _db.SetSetting("adhd_onboarded", "true");

            var step1 = new ContentDialog
            {
                Title = "ADHD友好模式 · 1/3",
                Content = "专为注意力容易分散的你设计：\n\n【15分钟短周期】\n降低心理门槛，坐不住也能开始。\n\n【结束进程强屏蔽】\n抖音/B站等分心App会被直接结束（不是最小化），防止手贱点回去。\n\n【正反馈鼓励】\n结束后只夸你完成了多少，不批评你分心了几次。",
                PrimaryButtonText = "下一步",
                XamlRoot = XamlRoot
            };
            await step1.ShowAsync();

            var step2 = new ContentDialog
            {
                Title = "ADHD友好模式 · 2/3",
                Content = "【缓冲期】\n专注结束后屏蔽保持10分钟，防止「一结束就刷手机」的条件反射。\n\n【软着陆】\n缓冲期到了不会自动解除屏蔽，你需要主动选择「再来15分钟」或「自由使用」。\n\n【暂停有摩擦】\n暂停时会弹确认，防止「暂停一下就再也不回来了」。",
                PrimaryButtonText = "下一步",
                XamlRoot = XamlRoot
            };
            await step2.ShowAsync();

            var step3 = new ContentDialog
            {
                Title = "ADHD友好模式 · 3/3",
                Content = "【注意事项】\n• 结束进程可能丢失未保存内容，请确保重要文件已保存\n• 屏蔽分类跟随当前场景（工作/写作/学习/会议），可在屏蔽页修改\n• 可随时在屏蔽页把拦截方式改回「最小化」\n• ADHD模式与深度模式互斥，不能同时开启\n\n准备好了吗？",
                PrimaryButtonText = "开始使用",
                CloseButtonText = "先关掉",
                XamlRoot = XamlRoot
            };
            var result = await step3.ShowAsync();
            if (result == ContentDialogResult.None) // 用户点"先关掉"
            {
                _adhdMode = false;
                AdhdModeSwitch.IsOn = false;
                _pendingMinutes = _currentSceneTag != null ? SceneManager.GetSceneConfig(_db, _currentSceneTag).Minutes : 25;
                SessionHint.Text = $"{_pendingMinutes} 分钟定心 · 正计时 · 心无旁骛";
            }
            App.LogAction("ADHD首次引导", result == ContentDialogResult.Primary ? "完成" : "跳过");
        }
        catch (Exception ex)
        {
            App.LogCrash("MainPage.ShowAdhdOnboarding", ex);
        }
    }
}
