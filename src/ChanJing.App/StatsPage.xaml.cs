using ChanJing.Core.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;

namespace ChanJing_App;

/// <summary>
/// 统计页：今日专注卡片、近 7 天柱状、本月热力、今日使用分布、今日分享卡片（PNG）。
/// 全部本地数据，零上传。
/// </summary>
public sealed partial class StatsPage : Page
{
    private static readonly string[] Quotes =
    {
        "不积跬步，无以至千里。",
        "心之所向，素履以往。",
        "日拱一卒，功不唐捐。",
        "静水流深，专注致远。",
        "念念不忘，必有回响。",
        "千里之行，始于足下。",
        "知之者不如好之者，好之者不如乐之者。"
    };

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
        DrawMonthHeatmap();
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

    // ---------- 近 7 天柱状 ----------

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

    // ---------- 本月热力 ----------

    private void DrawMonthHeatmap()
    {
        MonthChart.Children.Clear();
        MonthChart.ColumnDefinitions.Clear();
        MonthChart.RowDefinitions.Clear();

        var today = DateTime.Today;
        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
        var startCol = ((int)new DateTime(today.Year, today.Month, 1).DayOfWeek + 6) % 7; // 周一开头
        var rows = (startCol + daysInMonth + 6) / 7;

        for (var c = 0; c < 7; c++)
        {
            MonthChart.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }
        for (var r = 0; r < rows; r++)
        {
            MonthChart.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        var weekdayNames = new[] { "一", "二", "三", "四", "五", "六", "日" };
        var labelBrush = GetBrush("BrushTextSecondary");

        // 周几表头
        for (var c = 0; c < 7; c++)
        {
            var header = new TextBlock
            {
                Text = weekdayNames[c],
                FontSize = 10,
                Width = 26,
                Margin = new Thickness(2, 0, 2, 4),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = labelBrush
            };
            Grid.SetColumn(header, c);
            MonthChart.Children.Add(header);
        }

        var panelBrush = GetBrush("BrushPanel");
        var weak = new SolidColorBrush(Color.FromArgb(64, 110, 127, 99));
        var mid = new SolidColorBrush(Color.FromArgb(140, 110, 127, 99));
        var strong = GetBrush("BrushState");

        for (var day = 1; day <= daysInMonth; day++)
        {
            var date = new DateTime(today.Year, today.Month, day);
            var index = startCol + day - 1;
            var col = index % 7;
            var row = index / 7 + 1; // +1 给表头

            var minutes = _db.GetSessions(date, date.AddDays(1)).Sum(s => s.ActualMinutes);
            var brush = minutes == 0 ? panelBrush : minutes <= 25 ? weak : minutes <= 50 ? mid : strong;

            var cell = new Border
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(5),
                Background = brush
            };
            ToolTipService.SetToolTip(cell, $"{date:M 月 d 日}：{minutes} 分钟");
            Grid.SetColumn(cell, col);
            Grid.SetRow(cell, row);
            MonthChart.Children.Add(cell);
        }
    }

    // ---------- 今日使用分布 ----------

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

    // ---------- 今日分享卡片 ----------

    private async void ShareCard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var today = DateTime.Today;
        var sessions = _db.GetSessions(today, today.AddDays(1));
        ShareCount.Text = sessions.Count.ToString();
        ShareMinutes.Text = sessions.Sum(s => s.ActualMinutes).ToString();
        ShareStreak.Text = CalcStreak().ToString();
        ShareQuote.Text = Quotes[Random.Shared.Next(Quotes.Length)];
        ShareCard.Visibility = Visibility.Visible;

        var dialog = new ContentDialog
        {
            Title = "今日定心卡片",
            Content = ShareCard,
            PrimaryButtonText = "保存图片",
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        dialog.PrimaryButtonClick += async (_, _) =>
        {
            dialog.Hide();
            await SaveCardImageAsync();
        };
        await dialog.ShowAsync();
        ShareCard.Visibility = Visibility.Collapsed;
        App.LogAction("生成分享卡片", $"定心 {ShareCount.Text} 次 / {ShareMinutes.Text} 分钟");
        }
        catch (Exception ex)
        {
            App.LogCrash("StatsPage.ShareCard", ex);
        }
    }

    private async Task SaveCardImageAsync()
    {
        try
        {
            var renderer = new RenderTargetBitmap();
            await renderer.RenderAsync(ShareCard);

            var pixels = (await renderer.GetPixelsAsync()).ToArray();
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "禅净");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"今日定心-{DateTime.Now:yyyyMMdd-HHmmss}.png");

            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                (uint)renderer.PixelWidth, (uint)renderer.PixelHeight, 96, 96, pixels);
            await encoder.FlushAsync();

            await using (var fileStream = File.Create(path))
            {
                await stream.AsStreamForRead().CopyToAsync(fileStream);
            }

            var info = new ContentDialog
            {
                Title = "已保存",
                Content = $"卡片已保存到：\n{path}",
                CloseButtonText = "好",
                XamlRoot = XamlRoot
            };
            await info.ShowAsync();
            App.LogAction("保存分享卡片", path);
        }
        catch (Exception ex)
        {
            var error = new ContentDialog
            {
                Title = "保存失败",
                Content = ex.Message,
                CloseButtonText = "好",
                XamlRoot = XamlRoot
            };
            await error.ShowAsync();
        }
    }

    private static Brush? GetBrush(string key) => App.Current.Resources[key] as Brush;

    private sealed record UsageItem(string Name, long Value, long Max, string Text);
}
