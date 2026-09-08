using System.Globalization;
using ChanJing.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChanJing_App;

/// <summary>
/// 禅净首页：今日一愿 + 开始专注 + 当日概览。
/// </summary>
public sealed partial class MainPage : Page
{
    private readonly AppDatabase _db;

    private static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChanJing", "chanjing.db");

    public MainPage()
    {
        InitializeComponent();
        _db = new AppDatabase(DbPath);
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var culture = CultureInfo.GetCultureInfo("zh-CN");
        DateText.Text = DateTime.Today.ToString("M 月 d 日 dddd", culture);
        WishBox.Text = _db.GetSetting("today_wish") ?? string.Empty;
        RefreshTodayStats();
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        var wish = WishBox.Text?.Trim();
        if (!string.IsNullOrEmpty(wish))
        {
            _db.SetSetting("today_wish", wish);
        }

        // TODO(下个迭代): 启动专注会话 —— 正计时、无倒计时显示、前台窗口采集、屏蔽联动。
        SessionHint.Text = "心已定。专注中…";
    }

    private void RefreshTodayStats()
    {
        var today = DateTime.Today;
        var sessions = _db.GetSessions(today, today.AddDays(1));
        TodayCount.Text = sessions.Count.ToString(CultureInfo.InvariantCulture);
        TodayMinutes.Text = sessions.Sum(s => s.ActualMinutes).ToString(CultureInfo.InvariantCulture);
        BlockStatus.Text = HostsBlocker.IsApplied() ? "已启用" : "未启用";
    }
}
