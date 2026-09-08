using ChanJing.Core.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ChanJing_App;

/// <summary>
/// 统计页：今日专注卡片、近 7 天专注柱状图、今日使用分布。
/// 全部本地数据，零上传。
/// </summary>
public sealed partial class StatsPage : Page
{
    private readonly AppDatabase _db = AppServices.Db;

    public StatsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var today = DateTime.Today;
        var sessions = _db.GetSessions(today, today.AddDays(1));
        TodayCount.Text = sessions.Count.ToString();
        TodayMinutes.Text = sessions.Sum(s => s.ActualMinutes).ToString();
        StreakDays.Text = CalcStreak().ToString();

        DrawWeekChart();
        LoadUsage();
    }

    /// <summary>连续定心天数（今天起向前数，中断即停）。</summary>
    private int CalcStreak()
    {
        var streak = 0;
        var day = DateTime.Today;
        while (_db.GetSessions(day, day.AddDays(1)).Count > 0)
        {
            streak++;
            day = day.AddDays(-1);
        }
        return streak;
    }

    private void DrawWeekChart()
    {
        WeekChart.Children.Clear();
        WeekChart.ColumnDefinitions.Clear();
        WeekLabels.Children.Clear();
        WeekLabels.ColumnDefinitions.Clear();

        var days = Enumerable.Range(0, 7).Select(i => DateTime.Today.AddDays(i - 6)).ToList();
        var minutes = days
            .Select(d => _db.GetSessions(d, d.AddDays(1)).Sum(s => s.ActualMinutes))
            .ToList();
        var max = Math.Max(1, minutes.Max());
        var barBrush = GetBrush("BrushState");
        var labelBrush = GetBrush("BrushTextSecondary");

        for (var i = 0; i < 7; i++)
        {
            var col = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
            WeekChart.ColumnDefinitions.Add(col);
            WeekLabels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var height = minutes[i] == 0 ? 3 : Math.Max(6, (int)(minutes[i] * 100.0 / max));
            var bar = new Border
            {
                Height = height,
                Width = 26,
                CornerRadius = new CornerRadius(4),
                Background = barBrush,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(bar, i);
            WeekChart.Children.Add(bar);

            var label = new TextBlock
            {
                Text = days[i].ToString("dd"),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = labelBrush
            };
            Grid.SetColumn(label, i);
            WeekLabels.Children.Add(label);
        }
    }

    private void LoadUsage()
    {
        var usage = _db.GetUsageByDay(DateTime.Today.ToString("yyyy-MM-dd"));
        var items = usage
            .Select(kv => new UsageItem(kv.Key, kv.Value, 1, string.Empty))
            .OrderByDescending(x => x.Value)
            .Take(8)
            .ToList();
        var max = items.Count > 0 ? items.Select(x => x.Value).Max() : 1;
        UsageList.ItemsSource = items
            .Select(i => new UsageItem(i.Name, i.Value, max, $"{i.Value / 60} 分"))
            .ToList();
    }

    private static Brush? GetBrush(string key) => App.Current.Resources[key] as Brush;

    private sealed record UsageItem(string Name, long Value, long Max, string Text);
}
