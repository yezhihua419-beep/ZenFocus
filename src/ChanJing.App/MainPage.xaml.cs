using System.Globalization;
using ChanJing.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace ChanJing_App;

/// <summary>
/// 禅定首页：今日一愿 → 呼吸引导（3 秒）→ 正计时专注（无倒计时）→ 圆满/破功 → 即时反馈。
/// 抗焦虑设计：藏起剩余时间、破功用提问不审判、反馈用进步框架。
/// </summary>
public sealed partial class MainPage : Page
{
    private readonly FocusEngine _engine = AppServices.Engine;
    private readonly AppDatabase _db = AppServices.Db;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _breathTimer;
    private string? _pendingWish;
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
        DateText.Text = DateTime.Today.ToString("M 月 d 日 dddd",
            CultureInfo.GetCultureInfo("zh-CN"));
        WishBox.Text = _db.GetSetting("today_wish") ?? string.Empty;
        RefreshTodayStats();

        if (_engine.IsRunning) EnterFocusView();
        else EnterIdleView();
    }

    private void OnTick(object? sender, object e)
    {
        TimerText.Text = FormatElapsed(_engine.Elapsed);
    }

    // ---------- 开始流程：存愿 → 呼吸 → 专注 ----------

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        _pendingWish = WishBox.Text?.Trim();
        if (!string.IsNullOrEmpty(_pendingWish))
        {
            _db.SetSetting("today_wish", _pendingWish);
        }
        StartBreathing();
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
        _engine.Start(_pendingWish);
        EnterFocusView();
    }

    /// <summary>呼吸动画：1.5 秒放大（吸气）+ 1.5 秒缩小（呼气），共 3 秒。</summary>
    private Storyboard BuildBreathStoryboard()
    {
        var sb = new Storyboard();

        void Add(string property, double from, double to, double beginSeconds)
        {
            var animation = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = TimeSpan.FromSeconds(1.5),
                BeginTime = TimeSpan.FromSeconds(beginSeconds),
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(animation, BreathScale);
            Storyboard.SetTargetProperty(animation, property);
            sb.Children.Add(animation);
        }

        Add("ScaleX", 1.0, 1.15, 0);
        Add("ScaleY", 1.0, 1.15, 0);
        Add("ScaleX", 1.15, 1.0, 1.5);
        Add("ScaleY", 1.15, 1.0, 1.5);
        return sb;
    }

    // ---------- 专注态 ----------

    private void StartAgain_Click(object sender, RoutedEventArgs e)
    {
        EnterIdleView();
    }

    private void Complete_Click(object sender, RoutedEventArgs e)
    {
        var done = _engine.Finish(completed: true);
        ShowFeedback(_engine.GenerateFeedback(done));
    }

    /// <summary>摩擦式退出：破功前给一次深呼吸的冷静机会。</summary>
    private async void Break_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "发生了什么？",
            Content = "深呼吸三次。禅净不会拦你——但你真的要现在结束吗？",
            PrimaryButtonText = "再定心一会儿",
            CloseButtonText = "结束",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None) // 用户点了"结束"
        {
            var done = _engine.Finish(completed: false);
            ShowFeedback(_engine.GenerateFeedback(done));
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
}
