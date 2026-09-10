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
    private bool _breathing;

    public MainPage()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
        _breathTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _breathTimer.Tick += OnBreathTick;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            DateText.Text = DateTime.Today.ToString("M 月 d 日 dddd",
                CultureInfo.GetCultureInfo("zh-CN"));
            WishBox.Text = _db.GetSetting("today_wish") ?? string.Empty;
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

    /// <summary>首启3步引导：专注 → 屏蔽 → 统计与托盘。</summary>
    private async void ShowFirstRunGuideIfNeeded()
    {
        try
        {
            if (_db.GetSetting("onboarded") is not null) return;
            _db.SetSetting("onboarded", "true");
            var step1 = new ContentDialog { Title = "欢迎使用禅净 · 1/3", Content = "先管住手，再看清时间。\n\n【专注】\n在首页写下「今日一愿」→ 点「开始专注」→ 3秒呼吸引导后进入正计时（不显示剩余时间，减少焦虑）。\n\n支持场景快捷选择：工作50分钟 / 写作45分钟 / 学习25分钟 / 会议30分钟。", PrimaryButtonText = "下一步", XamlRoot = XamlRoot };
            await step1.ShowAsync();
            var step2 = new ContentDialog { Title = "欢迎使用禅净 · 2/3", Content = "【屏蔽】\n到「屏蔽」页勾选分类 → 点底部「应用屏蔽」，浏览器将打不开这些网站（含隐身窗口）。\n\n首次会请求管理员权限，个别杀软可能弹窗，属正常。\n\n桌面 App（抖音客户端等）需在「桌面应用拦截」区单独配置，支持自动最小化或结束进程。", PrimaryButtonText = "下一步", XamlRoot = XamlRoot };
            await step2.ShowAsync();
            var step3 = new ContentDialog { Title = "欢迎使用禅净 · 3/3", Content = "【统计与托盘】\n「统计」页查看今日专注次数、分钟数、连续天数、24小时分布、分心来源。\n\n点窗口 ✕ 或 — 是最小化到托盘（不退出），想彻底退出请右键托盘图标选「退出」。\n\n托盘菜单可快速开始/暂停专注、查看今日分钟、打开屏蔽设置。", PrimaryButtonText = "去设置屏蔽", CloseButtonText = "开始使用", DefaultButton = ContentDialogButton.Primary, XamlRoot = XamlRoot };
            var result = await step3.ShowAsync();
            if (result == ContentDialogResult.Primary && Frame is not null) { App.LogAction("首启引导", "去设置屏蔽"); Frame.Navigate(typeof(ShieldPage)); }
            else { App.LogAction("首启引导", "开始使用"); }
        }
        catch (Exception ex) { App.LogCrash("MainPage.FirstRunGuide", ex); }
    }
    private void OnTick(object? sender, object e)
    {
        TimerText.Text = FormatElapsed(_engine.Elapsed);
    }

    // ---------- 开始流程：存愿 → 呼吸 → 专注 ----------

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
            StartBreathing();
        }
        catch (Exception ex)
        {
            App.LogCrash("MainPage.Start", ex);
        }
    }


    /// <summary>场景预设：愿望 + 时长 + 屏蔽分类。点场景按钮一键应用，无需去屏蔽页手动设置。</summary>
    private static readonly IReadOnlyDictionary<string, (string Wish, int Minutes, string[] Categories)> ScenePresets =
        new Dictionary<string, (string, int, string[])>
        {
            ["work"] = ("完成今日工作任务", 50, new[] { "短视频", "视频娱乐", "购物" }),
            ["write"] = ("专注写作，心无旁骛", 45, new[] { "短视频", "视频娱乐", "社交", "购物", "资讯" }),
            ["study"] = ("深度学习，理解透彻", 25, new[] { "短视频", "视频娱乐", "社交", "购物", "资讯" }),
            ["meeting"] = ("专注会议，高效沟通", 30, new[] { "短视频", "视频娱乐" }),
        };

    /// <summary>当前选中的场景标签，用于UI高亮和专注界面显示。</summary>
    private string? _currentSceneTag;

    /// <summary>场景配置记录：愿望+时长+屏蔽分类。</summary>
    private record SceneConfig(string Wish, int Minutes, string[] Categories);

    /// <summary>读取场景配置：优先用户自定义，没有则用预设默认值。</summary>
    private SceneConfig GetSceneConfig(string tag)
    {
        if (ScenePresets.TryGetValue(tag, out var preset))
        {
            var raw = _db.GetSetting("scene_config_" + tag);
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
    private void SaveSceneConfig(string tag, SceneConfig config)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            wish = config.Wish,
            minutes = config.Minutes,
            categories = config.Categories
        });
        _db.SetSetting("scene_config_" + tag, json);
    }

    /// <summary>重置场景配置为预设默认值（删除用户自定义）。</summary>
    private void ResetSceneConfig(string tag)
    {
        _db.SetSetting("scene_config_" + tag, "");
    }

    /// <summary>场景快捷选择：自动填充愿望、预设时长、应用该场景的屏蔽配置。</summary>
    private void Scene_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var tag = (sender as Button)?.Tag?.ToString();
            if (string.IsNullOrEmpty(tag) || !ScenePresets.TryGetValue(tag, out var preset))
            {
                App.LogAction("选择场景", "未知场景: " + tag);
                return;
            }

            _currentSceneTag = tag;
            var config = GetSceneConfig(tag);
            WishBox.Text = config.Wish;
            _pendingMinutes = config.Minutes;

            // 更新时长显示（用户可见）
            SessionHint.Text = $"{config.Minutes} 分钟定心 · 正计时 · 心无旁骛";

            // 一键应用该场景的屏蔽分类（桌面应用拦截实时生效；网站屏蔽需点"应用屏蔽"写hosts）
            _blocklist.SetEnabledCategories(config.Categories);

            // 更新场景按钮高亮状态
            foreach (var child in ScenePanel.Children)
            {
                if (child is Button btn)
                {
                    var isActive = btn.Tag?.ToString() == tag;
                    btn.Background = isActive ? new SolidColorBrush(ColorHelper.FromArgb(255, 110, 127, 99)) : new SolidColorBrush(Colors.Transparent);
                    btn.Foreground = isActive ? new SolidColorBrush(Colors.White) : (Brush)Application.Current.Resources["BrushTextSecondary"];
                    btn.BorderBrush = isActive ? new SolidColorBrush(ColorHelper.FromArgb(255, 110, 127, 99)) : (Brush)Application.Current.Resources["BrushTextSecondary"];
                }
            }

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

            if (!_blocklist.IsActivated())
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
        var sceneName = tag switch
        {
            "work" => "工作",
            "write" => "写作",
            "study" => "学习",
            "meeting" => "会议",
            _ => tag
        };

        var dialog = new ContentDialog
        {
            Title = $"自定义「{sceneName}」场景",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "场景自定义为付费功能", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    new TextBlock { Text = "免费版可使用4个预设场景（一键应用愿望+时长+屏蔽）。", TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = "升级后可自定义每个场景的时长、屏蔽分类和愿望文案。", TextWrapping = TextWrapping.Wrap },
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
        var config = GetSceneConfig(tag);
        var sceneName = tag switch
        {
            "work" => "工作",
            "write" => "写作",
            "study" => "学习",
            "meeting" => "会议",
            _ => tag
        };

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

        var dialog = new ContentDialog
        {
            Title = $"自定义「{sceneName}」场景",
            Content = content,
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
            var newConfig = new SceneConfig(newWish, selectedMinutes, selectedCategories);
            SaveSceneConfig(tag, newConfig);
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
            ResetSceneConfig(tag);
            App.LogAction("重置场景", tag);

            if (_currentSceneTag == tag)
            {
                var preset = GetSceneConfig(tag);
                WishBox.Text = preset.Wish;
                _pendingMinutes = preset.Minutes;
                SessionHint.Text = $"{preset.Minutes} 分钟定心 · 正计时 · 心无旁骛";
                _blocklist.SetEnabledCategories(preset.Categories);
            }
        }
    }
    private void StartBreathing()
    {
        _breathing = true;
        IdlePanel.Visibility = Visibility.Collapsed;
        FocusPanel.Visibility = Visibility.Collapsed;
        FeedbackPanel.Visibility = Visibility.Collapsed;
        BreathingPanel.Visibility = Visibility.Visible;
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
        if (!_breathing) return;
        _breathing = false;
        _breathTimer.Stop();
        _engine.Start(_pendingWish, _pendingMinutes);
        App.LogAction("进入专注");
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
        EnterIdleView();
    }

    private void Complete_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var done = _engine.Finish(completed: true);
            App.LogAction("圆满结束", $"专注 {done.ActualMinutes} 分钟 分心 {done.DistractionCount} 次");
            ShowFeedback(_engine.GenerateFeedback(done));
        }
        catch (Exception ex)
        {
            App.LogCrash("MainPage.Complete", ex);
        }
    }

    /// <summary>临时离开：暂停计时，暂停期间不计入专注时长。</summary>
    private void PauseToggle_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_engine.IsPaused)
            {
                _engine.Resume();
                App.LogAction("继续专注");
                PauseButton.Content = "临时离开";
                PauseButton.Foreground = (Microsoft.UI.Xaml.Media.Brush)App.Current.Resources["BrushTextSecondary"];
                TimerText.Text = FormatElapsed(_engine.Elapsed);
            }
            else
            {
                _engine.Pause();
                App.LogAction("暂停专注");
                PauseButton.Content = "继续专注";
                PauseButton.Foreground = (Microsoft.UI.Xaml.Media.Brush)App.Current.Resources["BrushState"];
                TimerText.Text = "已暂停";
            }
        }
        catch (Exception ex)
        {
            App.LogCrash("MainPage.PauseToggle", ex);
        }
    }

    /// <summary>快捷放行：当前前台网站放行 10 分钟（无需切到屏蔽页）。</summary>
    private async void AllowCurrent_Click(object sender, RoutedEventArgs e)
    {
        try
        {
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
            AppServices.Blocklist.AddTempAllow(domain, 5);
            App.LogAction("快捷放行", $"{domain} 10分钟");
            var ok = new ContentDialog
            {
                Title = "已放行",
                Content = $"{domain} 已临时放行 5 分钟，期间可正常访问。",
                CloseButtonText = "好",
                XamlRoot = XamlRoot
            };
            await ok.ShowAsync();
        }
        catch (Exception ex)
        {
            App.LogCrash("MainPage.AllowCurrent", ex);
        }
    }

    /// <summary>摩擦式退出：破功前给 3 秒冷静期，按钮倒计时后才可结束。</summary>
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
                App.LogAction("破功", $"专注 {_engine.Elapsed.TotalMinutes:0.#} 分钟");
                var done = _engine.Finish(completed: false);
                ShowFeedback(_engine.GenerateFeedback(done));
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
        IdlePanel.Visibility = Visibility.Collapsed;
        FeedbackPanel.Visibility = Visibility.Collapsed;
        BreathingPanel.Visibility = Visibility.Collapsed;
        FocusPanel.Visibility = Visibility.Visible;
        WishShow.Text = _engine.Current?.Wish is { Length: > 0 } w
            ? $"今日一愿：{w}"
            : "心无旁骛，只做眼前这一件事";
        TimerText.Text = "00:00";
    }

    private void EnterIdleView()
    {
        _timer.Stop();
        FocusPanel.Visibility = Visibility.Collapsed;
        FeedbackPanel.Visibility = Visibility.Collapsed;
        BreathingPanel.Visibility = Visibility.Collapsed;
        IdlePanel.Visibility = Visibility.Visible;
    }

    private void ShowFeedback(string text)
    {
        _timer.Stop();
        FocusPanel.Visibility = Visibility.Collapsed;
        IdlePanel.Visibility = Visibility.Collapsed;
        BreathingPanel.Visibility = Visibility.Collapsed;
        FeedbackText.Text = text;
        FeedbackPanel.Visibility = Visibility.Visible;
        RefreshTodayStats();
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
        BlockStatus.Text = AppServices.Blocklist.IsApplied() ? "已启用" : "未启用";
    }

    /// <summary>生成手机伴侣页二维码：根据伴侣服务实际监听状态显示。</summary>
    private void GenerateCompanionQrCode()
    {
        try
        {
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
}
