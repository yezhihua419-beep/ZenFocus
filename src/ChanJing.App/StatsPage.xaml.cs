using ChanJing.Core.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.ApplicationModel.DataTransfer;
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
    private readonly DispatcherTimer _refreshTimer;

    public StatsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _refreshTimer.Tick += (_, _) => RefreshAll();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _refreshTimer.Stop();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshAll();
        _refreshTimer.Start();
    }

    private void RefreshAll()
    {
        try
        {
            var today = DateTime.Today;
            var sessions = _db.GetSessions(today, today.AddDays(1));
            TodayCount.Text = sessions.Count.ToString();
            var totalMinutes = sessions.Sum(s => s.ActualMinutes);
            TodayMinutes.Text = totalMinutes.ToString();
            InsightText.Text = totalMinutes > 0
                ? $"今日已专注 {totalMinutes} 分钟 · 完成 {sessions.Count} 次定心 · 继续保持"
                : "今日还没有专注记录 · 回到「禅定」页开始第一次定心";
            StreakDays.Text = CalcStreak().ToString();
            var totalDistractions = sessions.Sum(s => s.DistractionCount);
            DistractionCount.Text = totalDistractions.ToString();
            // 专注质量分：时长基础分(每分钟2分，上限100) - 分心扣分(每次5分)，下限0
            var baseScore = Math.Min(100, totalMinutes * 2);
            var qualityScore = Math.Max(0, baseScore - totalDistractions * 5);
            QualityScore.Text = totalMinutes > 0 ? qualityScore.ToString() : "—";
            // 分心模式识别：找出分心最多的时段（基于专注会话的开始时间分布）
            AnalyzeDistractionPattern(sessions);
            // 付费功能预览钩子：免费版且有专注记录时显示
            PremiumPreview.Visibility = (!AppServices.Blocklist.IsActivated() && totalMinutes > 0)
                ? Visibility.Visible : Visibility.Collapsed;

            // 空状态引导：无记录时显示提示并隐藏图表区
            var isEmpty = sessions.Count == 0;
            EmptyHint.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
            WeekSection.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
            HourSection.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
            MonthSection.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
            UsageSection.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
            if (isEmpty) { App.LogAction("统计刷新", "空状态：无专注记录"); return; }
            DrawWeekChart();
            DrawHourlyChart();
            DrawMonthHeatmap();
            LoadUsage();
            App.LogAction("统计刷新", $"专注{sessions.Count}次/{sessions.Sum(s => s.ActualMinutes)}分 分心{sessions.Sum(s => s.DistractionCount)}次");
        }
        catch (Exception ex)
        {
            App.LogCrash("StatsPage.RefreshAll", ex);
        }
    }

    /// <summary>连续定心天数（今天起向前数，中断即停）。</summary>
    /// <summary>分心模式识别：分析近7天专注会话，找出「最易分心时段」并给建议。</summary>
    private void AnalyzeDistractionPattern(List<ChanJing.Core.Models.FocusSession> sessions)
    {
        try
        {
            var last7 = _db.GetSessions(DateTime.Today.AddDays(-6), DateTime.Today.AddDays(1));
            var withDistraction = last7.Where(s => s.DistractionCount > 0).ToList();
            if (withDistraction.Count == 0)
            {
                PatternInsight.Visibility = Visibility.Collapsed;
                return;
            }
            var hourGroups = withDistraction.GroupBy(s => s.StartedAt.Hour)
                .OrderByDescending(g => g.Sum(x => x.DistractionCount))
                .First();
            var bestHour = hourGroups.Key;
            var distCount = hourGroups.Sum(x => x.DistractionCount);
            var totalDist = withDistraction.Sum(x => x.DistractionCount);
            var ratio = (int)Math.Round((double)distCount / totalDist * 100);
            var periodName = bestHour switch
            {
                >= 5 and < 9 => "清晨",
                >= 9 and < 12 => "上午",
                >= 12 and < 14 => "午间",
                >= 14 and < 18 => "下午",
                >= 18 and < 22 => "晚间",
                _ => "深夜"
            };
            PatternInsight.Text = $"观察：你在{periodName}（{bestHour}点前后）最容易起身活动，近7天{ratio}%的活动集中在这个时段。可在屏蔽页为此时段加设每日限额。";
            PatternInsight.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            App.LogCrash("StatsPage.PatternInsight", ex);
        }
    }

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

    // ---------- 今日 24 小时时间轴 ----------

    private void DrawHourlyChart()
    {
        HourlyChart.Children.Clear();
        HourlyChart.ColumnDefinitions.Clear();
        HourlyLabels.Children.Clear();
        HourlyLabels.ColumnDefinitions.Clear();

        var today = DateTime.Today;
        var sessions = _db.GetSessions(today, today.AddDays(1));

        // 按小时分组计算专注分钟数
        var hourly = new int[24];
        foreach (var session in sessions)
        {
            // 简化：按会话开始时间的小时分配（实际应该按时段拆分，但MVP先用开始时间）
            var hour = session.StartedAt.Hour;
            if (hour >= 0 && hour < 24)
            {
                hourly[hour] += session.ActualMinutes;
            }
        }

        // 如果当前有进行中的会话，也计入当前小时
        if (AppServices.Engine.IsRunning && !AppServices.Engine.IsPaused)
        {
            var currentHour = DateTime.Now.Hour;
            hourly[currentHour] += (int)Math.Round(AppServices.Engine.Elapsed.TotalMinutes);
        }

        var max = Math.Max(1, hourly.Max());
        var barBrush = GetBrush("BrushState");
        var labelBrush = GetBrush("BrushTextSecondary");
        var currentHourBrush = new SolidColorBrush(Color.FromArgb(200, 122, 114, 101));

        for (var h = 0; h < 24; h++)
        {
            var col = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
            HourlyChart.ColumnDefinitions.Add(col);
            HourlyLabels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var height = hourly[h] == 0 ? 2 : Math.Max(4, (int)(hourly[h] * 80.0 / max));
            var isCurrentHour = h == DateTime.Now.Hour;
            var bar = new Border
            {
                Height = height,
                CornerRadius = new CornerRadius(2),
                Background = isCurrentHour ? currentHourBrush : barBrush,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(1, 0, 1, 0),
                Opacity = isCurrentHour ? 1.0 : 0.7
            };
            ToolTipService.SetToolTip(bar, $"{h:00}:00 - {hourly[h]} 分钟");
            Grid.SetColumn(bar, h);
            HourlyChart.Children.Add(bar);

            // 只显示 0、6、12、18、23 的标签，避免拥挤
            if (h == 0 || h == 6 || h == 12 || h == 18 || h == 23)
            {
                var label = new TextBlock
                {
                    Text = $"{h:00}",
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = labelBrush
                };
                Grid.SetColumn(label, h);
                HourlyLabels.Children.Add(label);
            }
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
            .Select(kv =>
            {
                var isDistraction = AppServices.Blocklist.MatchBlockedApp(kv.Key) is not null;
                return new UsageItem(
                    isDistraction ? $"{kv.Key}（分心）" : kv.Key,
                    kv.Value,
                    1,
                    string.Empty,
                    isDistraction);
            })
            .OrderByDescending(x => x.Value)
            .Take(8)
            .ToList();
        var max = items.Count > 0 ? items.Select(x => x.Value).Max() : 1;
        UsageList.ItemsSource = items
            .Select(i => new UsageItem(i.Name, i.Value, max, $"{i.Value / 60} 分", i.IsDistraction))
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
        var totalDist = sessions.Sum(s => s.DistractionCount);
        var totalMin = sessions.Sum(s => s.ActualMinutes);
        var baseSc = Math.Min(100, totalMin * 2);
        ShareQuality.Text = (totalMin > 0 ? Math.Max(0, baseSc - totalDist * 5) : 0).ToString();
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

            // 同时复制到剪贴板（P2-3 分享出口：可直接粘贴到微信/朋友圈/飞书）
            try
            {
                stream.Seek(0);
                var package = new DataPackage();
                package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
                Clipboard.SetContent(package);
            }
            catch (Exception ex)
            {
                App.LogAction("分享卡片", "复制剪贴板失败: " + ex.Message);
            }

            var info = new ContentDialog
            {
                Title = "已保存",
                Content = $"卡片已保存到：\n{path}\n且已复制到剪贴板，可直接粘贴分享。",
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

    private sealed record UsageItem(string Name, long Value, long Max, string Text, bool IsDistraction = false)
    {
        public Brush NameBrush => IsDistraction
            ? new SolidColorBrush(Color.FromArgb(255, 160, 86, 59)) // 赭石，与警告色统一
            : (Brush)App.Current.Resources["BrushTextPrimary"];
    }
}
