using System.Globalization;
using ChanJing.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChanJing_App;

/// <summary>
/// 禅定首页：今日一愿 → 开始专注（正计时、无倒计时）→ 圆满结束/破功 → 即时反馈。
/// 抗焦虑设计：藏起剩余时间、破功用提问不审判、反馈用进步框架。
/// </summary>
public sealed partial class MainPage : Page
{
    private readonly FocusEngine _engine = AppServices.Engine;
    private readonly AppDatabase _db = AppServices.Db;
    private readonly DispatcherTimer _timer;

    public MainPage()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
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

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        var wish = WishBox.Text?.Trim();
        if (!string.IsNullOrEmpty(wish))
        {
            _db.SetSetting("today_wish", wish);
        }
        _engine.Start(wish);
        EnterFocusView();
    }

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
        IdlePanel.Visibility = Visibility.Visible;
    }

    private void ShowFeedback(string text)
    {
        _timer.Stop();
        FocusPanel.Visibility = Visibility.Collapsed;
        IdlePanel.Visibility = Visibility.Collapsed;
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
