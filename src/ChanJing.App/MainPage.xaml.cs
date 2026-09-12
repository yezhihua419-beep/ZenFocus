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
        Unloaded += (_, _) => AppServices.SceneAppliedExternally -= OnSceneAppliedExternally;
        AppServices.SceneAppliedExternally += OnSceneAppliedExternally;
        ApplyLocalizedButtons();
        App.LogAction("i18n-probe",
            $"lang={App.GetLanguage()} i18n={I18n.Get("MainPage_Title.Text")} xaml={TitleText.Text} start={StartButton.Content} scene={SceneWork.Content} presetWish={I18n.SceneWish("work")} app={I18n.AppName("douyin")}");
    }

    /// <summary>x:Uid 对 Button.Content 会被本地空值盖掉，启动时用 PRI 回填。</summary>
    private void ApplyLocalizedButtons()
    {
        I18n.SetContent(SceneWork, "MainPage_SceneWork.Content", "Work");
        I18n.SetContent(SceneWrite, "MainPage_SceneWrite.Content", "Write");
        I18n.SetContent(SceneStudy, "MainPage_SceneStudy.Content", "Study");
        I18n.SetContent(SceneMeeting, "MainPage_SceneMeeting.Content", "Meeting");
        I18n.SetContent(StartButton, "MainPage_StartButton.Content", "Start Focus");
        I18n.SetContent(BreakButton, "MainPage_Break.Content", "Let Go");
        I18n.SetContent(PauseButton, "MainPage_Pause.Content", "Pause");
        I18n.SetContent(FeedbackAgainButton, "MainPage_FeedbackAgain.Content", "Another 15 min");
        I18n.SetContent(FeedbackRestButton, "MainPage_FeedbackRest.Content", "Take a break");
        I18n.SetContent(CooldownReleaseButton, "MainPage_CooldownRelease.Content", "End blocking early");
        I18n.SetContent(GuideCloseButton, "MainPage_GuideClose.Content", "Get Started");
        I18n.SetContent(SkipBreathButton, "MainPage_SkipBreath.Content", "Skip");
        I18n.SetContent(CompleteButton, "MainPage_Complete.Content", "Complete");
        I18n.SetContent(Rest5Button, "MainPage_Rest5.Content", "5-min Break");
        I18n.SetContent(AllowCurrentButton, "MainPage_AllowCurrent.Content", "Temporarily allow this site");
        I18n.SetContent(Allow5Item, "MainPage_Allow5.Text", "Allow 5 min");
        I18n.SetContent(Allow15Item, "MainPage_Allow15.Text", "Allow 15 min");
        I18n.SetContent(Allow30Item, "MainPage_Allow30.Text", "Allow 30 min");
        I18n.SetContent(CooldownAgainButton, "MainPage_CooldownAgain.Content", "Another 15 min");
        I18n.SetContent(SoftLandingAgainButton, "MainPage_SoftLandingAgain.Content", "Another 15 min");
        I18n.SetContent(SoftLandingReleaseButton, "MainPage_SoftLandingRelease.Content", "Use freely");
        I18n.SetContent(DeepModeSwitch, "MainPage_DeepModeSwitch.Header", "Deep Mode (untimed, follow your rhythm)");
        I18n.SetContent(DeepModeSwitch, "MainPage_DeepModeSwitch.OnContent", "On");
        I18n.SetContent(DeepModeSwitch, "MainPage_DeepModeSwitch.OffContent", "Off");
        I18n.SetContent(AdhdModeSwitch, "MainPage_AdhdModeSwitch.Header", "ADHD-Friendly Mode (15-min cycles)");
        I18n.SetContent(AdhdModeSwitch, "MainPage_AdhdModeSwitch.OnContent", "On");
        I18n.SetContent(AdhdModeSwitch, "MainPage_AdhdModeSwitch.OffContent", "Off");
        I18n.SetContent(LangEnButton, "Lang_English", "English");
        I18n.SetContent(LangZhButton, "Lang_Chinese", "中文");
        LangLabel.Text = I18n.Get("Lang_Label", "Language");
        HotkeyHint.Text = I18n.Get("MainPage_Hotkeys", "Shortcuts: Ctrl+Alt+F start/end · P pause · R 3-min break · S block page");
        ToolTipService.SetToolTip(SettingsButton, I18n.Get("MainPage_SettingsTooltip", "Mode and language"));
        ApplyAdminHint();
    }

    /// <summary>未提权时写清：网站 hosts 会跳过，桌面应用拦截仍在。</summary>
    private void ApplyAdminHint()
    {
        var show = !I18n.IsElevated();
        AdminHint.Text = I18n.Get("Admin_Hint", "Not administrator: YouTube/TikTok tabs will stay open (hosts skipped). Desktop apps can still be minimized. Right-click → Run as administrator.");
        AdminHint.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        AdminPrivacy.Text = I18n.Get("Admin_Privacy", "Admin is only for hosts and desktop-app blocking. Nothing is uploaded. Data stays on this PC.");
        ToolTipService.SetToolTip(StartButton, show
            ? I18n.Get("Admin_StartTooltip", "Without administrator, browser tabs will not close. Desktop app blocking still works.")
            : I18n.Get("MainPage_StartButton.Content", "Start Focus"));
    }

    private void LangEn_Click(object sender, RoutedEventArgs e) => _ = ConfirmSwitchLanguage("en-US");

    private void LangZh_Click(object sender, RoutedEventArgs e) => _ = ConfirmSwitchLanguage("zh-CN");

    /// <summary>切语言会重启进程：先确认，重启路径会结束专注并清 hosts。</summary>
    private async Task ConfirmSwitchLanguage(string lang)
    {
        if (App.GetLanguage() == lang) return;
        try
        {
            var dialog = new ContentDialog
            {
                Title = I18n.Get("Lang_ConfirmTitle", "Switch language?"),
                Content = I18n.Get("Lang_ConfirmContent", "The app will restart. Current focus will end and website blocking will be cleared."),
                PrimaryButtonText = I18n.Get("Lang_ConfirmPrimary", "Restart"),
                CloseButtonText = I18n.Get("Common_Cancel.Content", "Cancel"),
                XamlRoot = XamlRoot
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            App.SwitchLanguage(lang);
        }
        catch (Exception ex) { App.LogCrash("MainPage.SwitchLanguage", ex); }
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
            else if (_uiState is MainUiState.Cooldown or MainUiState.SoftLanding or MainUiState.Feedback or MainUiState.Breathing)
            {
                // 缓冲/反馈还在，不要拆掉面板（hosts 可能仍生效）
            }
            else
            {
                EnterIdleView();
            }
            ApplyLocalizedButtons();
        }
        catch (Exception ex) { App.LogCrash("MainPage.OnNavigatedTo", ex); }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var culture = App.GetLanguage() == "zh-CN" ? "zh-CN" : "en-US";
            DateText.Text = DateTime.Today.ToString(I18n.Get("MainPage_DateFmt", "MMMM d, dddd"),
                CultureInfo.GetCultureInfo(culture));
            WishBox.Text = I18n.DisplayWish(AppServices.CurrentSceneTag, _db.GetSetting("today_wish"));
            RestoreSceneHighlight(); // 回到首页时恢复场景高亮（跨页面同步）
            RefreshTodayStats();
            GenerateCompanionQrCode();

            DeepModeSwitch.IsOn = AppServices.DeepMode;
            AdhdModeSwitch.IsOn = AppServices.AdhdMode;
            _deepMode = AppServices.DeepMode;
            _adhdMode = AppServices.AdhdMode;
            if (_adhdMode) _pendingMinutes = 15;

            if (_engine.IsRunning) EnterFocusView();
            else if (_uiState is not (MainUiState.Cooldown or MainUiState.SoftLanding or MainUiState.Feedback or MainUiState.Breathing))
                EnterIdleView();

            ApplyLocalizedButtons();
            App.LogAction("i18n-loaded", $"scene={SceneWork.Content} start={StartButton.Content} wish={WishBox.Text}");
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
        TimerText.Text = I18n.GetFormat("MainPage_MinutesFmt", minutes);
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
                WishShow.Text = I18n.Get("MainPage_AdhdSoftTarget", "Past 15 min — looking good. End whenever you're ready.");
                WishShow.Foreground = (Brush)Application.Current.Resources["BrushAccent"];
            }
            if (minutes >= 30 && !_softReminder30Shown)
            {
                _softReminder30Shown = true;
                AppServices.Notify(I18n.Get("Notify_Rest30", "30 min focused. Take a break."));
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
                AppServices.AdhdMode = false;
            }
            SessionHint.Text = I18n.Get("MainPage_DeepHint", "Deep mode · untimed · follow your rhythm · end manually");
        }
        else
        {
            SessionHint.Text = I18n.GetFormat("Notify_SessionHint", _pendingMinutes);
        }
    }

    private void AdhdMode_Toggled(object sender, RoutedEventArgs e)
    {
        _adhdMode = AdhdModeSwitch.IsOn;
        AppServices.AdhdMode = _adhdMode;
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
            SessionHint.Text = I18n.Get("MainPage_AdhdHint", "ADHD-friendly mode · 15-min cycles · positive feedback");
            AppServices.Notify(I18n.Get("Notify_AdhdOn", "ADHD mode on: 15-min cycles · positive feedback. Blocking mode stays as set on the Block page."));
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
            SessionHint.Text = I18n.GetFormat("Notify_SessionHint", _pendingMinutes);
        }
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _pendingWish = WishBox.Text?.Trim();
            if (!string.IsNullOrEmpty(_pendingWish))
            {
                _db.SetSetting("today_wish", I18n.StoreWish(_currentSceneTag, _pendingWish));
            }
            App.LogAction("开始专注", _pendingWish is { Length: > 0 } ? $"愿：{_pendingWish}" : "无愿");
            // 同步当前愿望/时长到 AppServices，FocusStarted 时统一保存到场景（首页/托盘/伴侣页所有路径一致）
            AppServices.CurrentWish = _pendingWish;
            AppServices.CurrentMinutes = _deepMode ? 0 : _pendingMinutes;
            if (!I18n.IsElevated())
                AppServices.Notify(I18n.Get("Notify_HostsSkipped", "Website blocking skipped (needs administrator). Browser tabs such as YouTube will stay open."), Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);

            StartBreathing();
        }
        catch (Exception ex)
        {
            App.LogCrash("MainPage.Start", ex);
        }
    }



    /// <summary>当前选中的场景标签，用于UI高亮和专注界面显示。</summary>
    private string? _currentSceneTag;



    /// <summary>编码检测等外部入口切场景后，刷新首页高亮/愿望/时长。</summary>
    private void OnSceneAppliedExternally(string tag)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try { ApplySceneToUi(tag); }
            catch (Exception ex) { App.LogCrash("MainPage.SceneExternal", ex); }
        });
    }

    /// <summary>场景快捷选择：愿望/时长走统一入口，分类仍以屏蔽页为准。</summary>
    private void Scene_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var tag = (sender as Button)?.Tag?.ToString();
            if (string.IsNullOrEmpty(tag) || !SceneManager.ScenePresets.ContainsKey(tag))
            {
                App.LogAction("选择场景", "未知场景: " + tag);
                return;
            }

            AppServices.ApplyScenePreset(tag);
            App.LogAction("选择场景", $"{tag} {AppServices.CurrentMinutes}分钟 分类保持屏蔽页");
        }
        catch (Exception ex)
        {
            App.LogCrash("MainPage.Scene", ex);
        }
    }

    /// <summary>把已写入 FocusContext 的场景同步到首页控件（不改分类勾选）。</summary>
    private void ApplySceneToUi(string tag)
    {
        if (string.IsNullOrEmpty(tag) || !SceneManager.ScenePresets.ContainsKey(tag)) return;
        _currentSceneTag = tag;
        var config = SceneManager.GetSceneConfig(_db, tag);
        var shownWish = AppServices.CurrentWish ?? I18n.DisplayWish(tag, config.Wish);
        WishBox.Text = shownWish;
        _pendingMinutes = AppServices.AdhdMode ? 15 : AppServices.CurrentMinutes;
        if (_pendingMinutes <= 0) _pendingMinutes = AppServices.AdhdMode ? 15 : config.Minutes;
        SessionHint.Text = I18n.GetFormat("Notify_SessionHint", _pendingMinutes);
        HighlightSceneButtons(tag);
        UpdateSceneHint(tag, config.Minutes);
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
        var sceneName = I18n.SceneName(tag);

        var dialog = new ContentDialog
        {
            Title = I18n.GetFormat("SceneUpgrade_Title", sceneName),
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = I18n.Get("SceneUpgrade_Head", "Custom scene quota used"), FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    new TextBlock { Text = I18n.Get("SceneUpgrade_Body", "Free version allows 1 custom scene. Quota used up."), TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = I18n.Get("SceneUpgrade_More", "Upgrade to customize all 4 scenes."), TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = I18n.Get("Price_Buyout", "$19 lifetime, forever."), Foreground = (Brush)Application.Current.Resources["BrushAccent"] }
                }
            },
            PrimaryButtonText = I18n.Get("SceneUpgrade_Primary", "Learn more"),
            CloseButtonText = I18n.Get("Common_Cancel.Content", "Cancel"),
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
        var sceneName = I18n.SceneName(tag);

        var minutesCombo = new ComboBox
        {
            Header = I18n.Get("SceneDialog_MinutesHeader", "Focus length (minutes)"),
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
                Content = I18n.CategoryName(cat),
                Tag = cat,
                IsChecked = config.Categories.Contains(cat)
            };
            categoryCheckboxes.Add(cb);
            categoryPanel.Children.Add(cb);
        }

        var wishBox = new TextBox
        {
            Header = I18n.Get("SceneDialog_WishHeader", "Intention"),
            Text = I18n.DisplayWish(tag, config.Wish),
            PlaceholderText = I18n.Get("SceneDialog_WishPlaceholder", "What is the one thing you most want to accomplish right now…")
        };

        var content = new StackPanel { Spacing = 16, MaxWidth = 360 };
        content.Children.Add(minutesCombo);
        content.Children.Add(new TextBlock { Text = I18n.Get("SceneDialog_Categories", "Block Categories"), Foreground = (Brush)Application.Current.Resources["BrushTextSecondary"], FontSize = 12 });
        content.Children.Add(categoryPanel);
        content.Children.Add(wishBox);

        var usedQuota = SceneManager.GetCustomSceneCount(_db);
        var quotaText = _blocklist.IsActivated() ? "" : I18n.GetFormat("SceneDialog_Quota", usedQuota);
        var dialog = new ContentDialog
        {
            Title = I18n.GetFormat("SceneDialog_Title", sceneName) + quotaText,
            Content = content,
            PrimaryButtonText = I18n.Get("SceneDialog_Primary", "Save"),
            SecondaryButtonText = I18n.Get("SceneDialog_Secondary", "Reset to Default"),
            CloseButtonText = I18n.Get("SceneDialog_Close", "Cancel"),
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var selectedMinutes = (int)(minutesCombo.SelectedItem ?? 25);
            var selectedCategories = categoryCheckboxes.Where(cb => cb.IsChecked == true)
                .Select(cb => cb.Tag?.ToString() ?? cb.Content?.ToString() ?? "").Where(s => s.Length > 0).ToArray();
            var newWish = I18n.StoreWish(tag, wishBox.Text);
            var newConfig = new SceneManager.SceneConfig(newWish, selectedMinutes, selectedCategories);
            SceneManager.SaveSceneConfig(_db, tag, newConfig);
            App.LogAction("自定义场景", $"{tag} {selectedMinutes}分钟 屏蔽=[{string.Join("/", selectedCategories)}]");

            if (_currentSceneTag == tag)
            {
                WishBox.Text = I18n.DisplayWish(tag, newWish);
                _pendingMinutes = _adhdMode ? 15 : selectedMinutes;
                SessionHint.Text = I18n.GetFormat("Notify_SessionHint", _pendingMinutes);
                _blocklist.SetEnabledCategories(selectedCategories); // 用户在对话框里明确勾了分类
                UpdateSceneHint(tag, selectedMinutes);
            }
        }
        else if (result == ContentDialogResult.Secondary)
        {
            SceneManager.ResetSceneConfig(_db, tag);
            App.LogAction("重置场景", tag);

            if (_currentSceneTag == tag)
            {
                var preset = SceneManager.GetSceneConfig(_db, tag);
                WishBox.Text = I18n.DisplayWish(tag, preset.Wish);
                _pendingMinutes = _adhdMode ? 15 : preset.Minutes;
                SessionHint.Text = I18n.GetFormat("Notify_SessionHint", _pendingMinutes);
                UpdateSceneHint(tag, preset.Minutes);
            }
        }
    }
    private void StartBreathing()
    {
        SetUiState(MainUiState.Breathing);
        BreathText.Text = I18n.Get("MainPage_BreathIn.Text", "Breathe in…");
        _breathTimer.Start();

        var storyboard = BuildBreathStoryboard();
        storyboard.Completed += (_, _) => FinishBreathing();
        storyboard.Begin();
    }

    private void OnBreathTick(object? sender, object e)
    {
        var inhale = I18n.Get("MainPage_BreathIn.Text", "Breathe in…");
        var exhale = I18n.Get("MainPage_BreathOut.Text", "Breathe out…");
        BreathText.Text = BreathText.Text == inhale ? exhale : inhale;
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
                ShowLocalizedFeedback(done);
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
        FeedbackStreak.Text = streak > 1 ? I18n.GetFormat("MainPage_FeedbackStreak", streak) : "";
        FeedbackStreak.Visibility = streak > 1 ? Visibility.Visible : Visibility.Collapsed;

        var encouragement = streak >= 5 ? I18n.Get("MainPage_Encourage5", "Settled and building. Keep going.") :
                           streak >= 3 ? I18n.Get("MainPage_Encourage3", "Finding your rhythm. Keep going.") :
                           streak >= 1 ? I18n.Get("MainPage_Encourage1", "A good start.") : "";
        FeedbackText.Text = I18n.GetFormat("MainPage_FeedbackMinutes", done.ActualMinutes, encouragement);
        FeedbackAgainButton.Content = I18n.Get("MainPage_FeedbackAgain.Content", "Another 15 min");
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
        CooldownText.Text = I18n.GetFormat("MainPage_CooldownText", _cooldownRemaining);
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
            Title = I18n.Get("MainPage_CooldownReleaseTitle", "End blocking early?"),
            Content = I18n.GetFormat("MainPage_CooldownReleaseContent", _cooldownRemaining),
            PrimaryButtonText = I18n.Get("MainPage_CooldownReleasePrimary", "Unblock now"),
            CloseButtonText = I18n.Get("MainPage_CooldownReleaseClose", "Wait"),
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
        SoftLandingText.Text = I18n.GetFormat("MainPage_SoftLandingBody", _blocklist.GetCooldownMinutes());
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
                PauseButton.Content = I18n.Get("MainPage_Pause.Content", "Pause");
                PauseButton.Foreground = (Microsoft.UI.Xaml.Media.Brush)App.Current.Resources["BrushTextSecondary"];
                TimerText.Text = I18n.GetFormat("MainPage_MinutesFmt", (int)_engine.Elapsed.TotalMinutes);
            }
            else
            {
                // ADHD模式：暂停弹确认（防止一去不回）
                if (_adhdMode)
                {
                    var dialog = new ContentDialog
                    {
                        Title = I18n.Get("MainPage_PauseTitle", "Pause focus?"),
                        Content = I18n.Get("MainPage_PauseContent", "In ADHD mode, pausing often means not coming back."),
                        PrimaryButtonText = I18n.Get("MainPage_PausePrimary", "Pause anyway"),
                        CloseButtonText = I18n.Get("MainPage_PauseClose", "Keep focusing"),
                        XamlRoot = XamlRoot
                    };
                    var result = await dialog.ShowAsync();
                    if (result != ContentDialogResult.Primary) return;
                }
                _engine.Pause();
                App.LogAction("暂停专注");
                PauseButton.Content = I18n.Get("MainPage_Resume.Content", "Resume");
                PauseButton.Foreground = (Microsoft.UI.Xaml.Media.Brush)App.Current.Resources["BrushState"];
                TimerText.Text = I18n.Get("MainPage_Paused", "Paused");
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
            var domain = AppServices.Activity.GetAllowableTarget();
            if (domain is null)
            {
                var none = new ContentDialog
                {
                    Title = I18n.Get("MainPage_NoSiteTitle", "No site to allow"),
                    Content = I18n.Get("MainPage_NoSiteContent", "No recent blocked site. Open a blocked page first, or wait for a distraction reminder."),
                    CloseButtonText = I18n.Get("MainPage_NoSiteClose", "Got it"),
                    XamlRoot = XamlRoot
                };
                await none.ShowAsync();
                return;
            }
            AppServices.Blocklist.AddTempAllow(domain, minutes);
            App.LogAction("快捷放行", $"{domain} {minutes}分钟");
            AppServices.Notify(I18n.GetFormat("Notify_DomainAllowed", domain, minutes));
        }
        catch (Exception ex)
        {
            App.LogCrash("MainPage.AllowCurrent", ex);
        }
    }

    private async void EmergencyPass_Click(object sender, RoutedEventArgs e)
    {
        var restMinutes = _adhdMode ? 3 : 5;
        var restLabel = _adhdMode ? I18n.Get("RestLabel_Short", "Quick Break") : I18n.Get("RestLabel_Long", "Away Mode");
        var dialog = new ContentDialog
        {
            Title = restLabel,
            Content = I18n.GetFormat("MainPage_RestContent", restLabel, restMinutes),
            PrimaryButtonText = I18n.Get("MainPage_RestPrimary", "Confirm"),
            CloseButtonText = I18n.Get("Common_Cancel.Content", "Cancel"),
            XamlRoot = Content.XamlRoot
        };
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;
        App.LogAction(restLabel, $"网站+桌面应用暂停拦截{restMinutes}分钟");
        AppServices.StartRestBreak(restMinutes);
    }

    private async void Break_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var remaining = 3;
            var hint = new TextBlock
            {
                Text = I18n.Get("MainPage_BreakHintWait", "Three breaths. Wait 3 seconds."),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)App.Current.Resources["BrushTextPrimary"]
            };
            var endButton = new Button
            {
                Content = I18n.GetFormat("MainPage_BreakEndCountdown", remaining),
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
                Title = I18n.Get("MainPage_BreakTitle", "What happened?"),
                Content = panel,
                PrimaryButtonText = I18n.Get("MainPage_BreakContinue", "Focus more"),
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
                    endButton.Content = I18n.Get("MainPage_BreakEnd", "End");
                    hint.Text = I18n.Get("MainPage_BreakHintReady", "Three breaths. Do you really want to end now?");
                }
                else
                {
                    endButton.Content = I18n.GetFormat("MainPage_BreakEndCountdown", remaining);
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
                    ShowLocalizedFeedback(done);
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
            ? I18n.GetFormat("MainPage_WishShow", w)
            : I18n.Get("MainPage_WishDefault", "One thing in front of you. Nothing else.");
        if (_deepMode)
        {
            // 深度模式：隐藏时间和进度条，只显示愿望；无计划时长，"放下"无意义，隐藏
            WishShow.Text = I18n.Get("MainPage_DeepFocusHint", "Deep mode · follow your rhythm · end manually when done");
            TimerText.Visibility = Visibility.Collapsed;
            FocusProgress.Visibility = Visibility.Collapsed;
            BreakButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            TimerText.Visibility = Visibility.Visible;
            FocusProgress.Visibility = Visibility.Visible;
            BreakButton.Visibility = Visibility.Visible;
            TimerText.Text = I18n.GetFormat("MainPage_MinutesFmt", 0);
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

    private void ShowLocalizedFeedback(ChanJing.Core.Models.FocusSession done)
    {
        var yesterday = DateTime.Today.AddDays(-1);
        var ys = _db.GetSessions(yesterday, DateTime.Today);
        int? avg = ys.Count > 0 ? (int)Math.Round(ys.Average(s => s.ActualMinutes)) : null;
        _timer.Stop();
        SetUiState(MainUiState.Feedback);
        FeedbackText.Text = I18n.FocusFeedback(done.ActualMinutes,
            done.State == ChanJing.Core.Models.FocusSessionState.Completed,
            done.DistractionCount, avg);
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
        var tag = AppServices.CurrentSceneTag;
        if (!string.IsNullOrEmpty(tag))
        {
            var cfg = SceneManager.GetSceneConfig(_db, tag);
            BlockStatus.Text = I18n.SceneSummary(tag, cfg.Minutes, _blocklist.GetEnabledCategories().Count);
        }
        else
        {
            BlockStatus.Text = AppServices.Blocklist.IsApplied()
                ? I18n.Get("Block_On", "On")
                : I18n.Get("Block_Off", "Off");
        }
        var mins = sessions.Sum(s => s.ActualMinutes);
        TodayFeedback.Text = I18n.GetFormat("MainPage_TodayFeedback", mins);
    }

    /// <summary>回到首页时恢复上次选中的场景高亮与摘要（跨页面同步）。</summary>
    private void RestoreSceneHighlight()
    {
        try
        {
            var restoreTag = AppServices.CurrentSceneTag;
            if (string.IsNullOrEmpty(restoreTag)) return;

            _currentSceneTag = restoreTag;
            HighlightSceneButtons(restoreTag);

            var config = SceneManager.GetSceneConfig(_db, restoreTag);
            if (_adhdMode) _pendingMinutes = 15;
            else _pendingMinutes = config.Minutes;
            UpdateSceneHint(restoreTag, config.Minutes);
            WishBox.Text = I18n.DisplayWish(restoreTag, string.IsNullOrWhiteSpace(WishBox.Text) ? config.Wish : WishBox.Text);
        }
        catch (Exception ex)
        {
            App.LogCrash("MainPage.RestoreScene", ex);
        }
    }

    private void HighlightSceneButtons(string tag)
    {
        foreach (var btn in new[] { SceneWork, SceneWrite, SceneStudy, SceneMeeting })
        {
            var isActive = btn.Tag?.ToString() == tag;
            btn.Background = isActive ? new SolidColorBrush(ColorHelper.FromArgb(255, 110, 127, 99)) : new SolidColorBrush(Colors.Transparent);
            btn.Foreground = isActive ? new SolidColorBrush(Colors.White) : (Brush)Application.Current.Resources["BrushTextSecondary"];
            btn.BorderBrush = isActive ? new SolidColorBrush(ColorHelper.FromArgb(255, 110, 127, 99)) : (Brush)Application.Current.Resources["BrushTextSecondary"];
        }
    }

    /// <summary>摘要用屏蔽页当前分类，不用场景里存的分类。</summary>
    private void UpdateSceneHint(string tag, int minutes)
    {
        var live = _blocklist.GetEnabledCategories();
        SceneConfigHint.Text = I18n.GetFormat("Scene_Hint", I18n.SceneName(tag), minutes, live.Count, I18n.CategoriesText(live));
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
                CompanionTitleText.Text = I18n.Get("MainPage_CompanionPaidTitle", "Phone companion (paid)");
                CompanionDescText.Text = I18n.Get("MainPage_CompanionPaidDesc", "After upgrade, scan to view stats and start/end focus from your phone.");
                ConnectUrlText.Text = "";
                CompanionUpgradeText.Text = I18n.Get("MainPage_CompanionUpgrade.Text", "Phone companion is a paid feature · $19 lifetime to unlock");
                CompanionUpgradeText.Visibility = Visibility.Visible;
                App.LogAction("伴侣二维码", "免费版显示升级提示");
                return;
            }

            // 付费版：显示二维码
            QrCodeBorder.Visibility = Visibility.Visible;
            CompanionTitleText.Text = I18n.Get("MainPage_CompanionTitle.Text", "Scan to connect phone");
            CompanionDescText.Text = I18n.Get("MainPage_CompanionDesc.Text", "Phone and PC must be on the same WiFi.");
            CompanionUpgradeText.Visibility = Visibility.Collapsed;

            var server = AppServices.Companion;
            if (server == null || !server.IsRunning)
            {
                ConnectUrlText.Text = I18n.Get("MainPage_CompanionNoServer", "Companion server is not running");
                return;
            }

            string url;
            if (server.IsLanAccess)
            {
                var ip = GetLocalIpAddress();
                if (string.IsNullOrEmpty(ip))
                {
                    ConnectUrlText.Text = I18n.Get("MainPage_CompanionNoNet", "No network detected");
                    return;
                }
                url = $"http://{ip}:{server.Port}/?token={Uri.EscapeDataString(server.AccessToken)}";
                ConnectUrlText.Text = I18n.GetFormat("MainPage_CompanionLanHint", $"http://{ip}:{server.Port}");
            }
            else
            {
                url = $"http://localhost:{server.Port}/?token={Uri.EscapeDataString(server.AccessToken)}";
                ConnectUrlText.Text = I18n.Get("MainPage_CompanionNeedAdmin", "Run as administrator so the phone can connect (localhost only now)");
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
            ConnectUrlText.Text = I18n.Get("MainPage_CompanionQrFail", "Failed to generate QR code");
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
                Title = I18n.Get("AdhdOnboard_T1", "ADHD-friendly mode · 1/3"),
                Content = I18n.Get("AdhdOnboard_C1", "15-min cycles. Kill-process blocking. Positive feedback."),
                PrimaryButtonText = I18n.Get("AdhdOnboard_Next", "Next"),
                XamlRoot = XamlRoot
            };
            await step1.ShowAsync();

            var step2 = new ContentDialog
            {
                Title = I18n.Get("AdhdOnboard_T2", "ADHD-friendly mode · 2/3"),
                Content = I18n.Get("AdhdOnboard_C2", "Cooldown. Soft landing. Pause friction."),
                PrimaryButtonText = I18n.Get("AdhdOnboard_Next", "Next"),
                XamlRoot = XamlRoot
            };
            await step2.ShowAsync();

            var step3 = new ContentDialog
            {
                Title = I18n.Get("AdhdOnboard_T3", "ADHD-friendly mode · 3/3"),
                Content = I18n.Get("AdhdOnboard_C3", "Notes. Ready?"),
                PrimaryButtonText = I18n.Get("AdhdOnboard_Start", "Get started"),
                CloseButtonText = I18n.Get("Common_Close.Content", "Close"),
                XamlRoot = XamlRoot
            };
            var result = await step3.ShowAsync();
            if (result == ContentDialogResult.None) // 用户点"先关掉"
            {
                _adhdMode = false;
                AdhdModeSwitch.IsOn = false;
                _pendingMinutes = _currentSceneTag != null ? SceneManager.GetSceneConfig(_db, _currentSceneTag).Minutes : 25;
                SessionHint.Text = I18n.GetFormat("Notify_SessionHint", _pendingMinutes);
            }
            App.LogAction("ADHD首次引导", result == ContentDialogResult.Primary ? "完成" : "跳过");
        }
        catch (Exception ex)
        {
            App.LogCrash("MainPage.ShowAdhdOnboarding", ex);
        }
    }
}
