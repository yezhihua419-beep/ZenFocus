using ChanJing.Core.Services;
using Microsoft.UI;
using Microsoft.UI.Text;
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

            // 付费功能：高效时段分析 + 连续纪录历史
            var isActivated = AppServices.Blocklist.IsActivated();
            PeakHourSection.Visibility = isActivated ? Visibility.Visible : Visibility.Collapsed;
            StreakHistorySection.Visibility = isActivated ? Visibility.Visible : Visibility.Collapsed;
            if (isActivated)
            {
                AnalyzePeakHours();
                AnalyzeStreakHistory();
            }

            // 空状态引导：无记录时显示提示并隐藏图表区
            var isEmpty = sessions.Count == 0;
            EmptyHint.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
            ChartExpander.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
            WeekSection.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
            HourSection.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
            MonthSection.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
            UsageSection.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
            DistractionSourcesSection.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
            LoadDistractionSources();
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

    // ---------- 分心来源TOP3 ----------

    private void LoadDistractionSources()
    {
        try
        {
            var week = _db.GetSessions(DateTime.Today.AddDays(-6), DateTime.Today.AddDays(1));
            var totals = new Dictionary<string, int>();
            foreach (var session in week)
            {
                if (string.IsNullOrWhiteSpace(session.DistractionSources)) continue;
                foreach (var pair in session.DistractionSources.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = pair.Split(':', 2);
                    if (kv.Length == 2 && int.TryParse(kv[1], out var count))
                    {
                        totals[kv[0]] = totals.GetValueOrDefault(kv[0]) + count;
                    }
                }
            }
            var top3 = totals.OrderByDescending(kv => kv.Value).Take(3).ToList();
            DistractionSourcesText.Text = top3.Count == 0
                ? "近7天没有分心记录，定心状态很好"
                : string.Join(" · ", top3.Select(kv => $"{kv.Key} {kv.Value}次"));
        }
        catch (Exception ex)
        {
            App.LogCrash("StatsPage.LoadDistractionSources", ex);
            DistractionSourcesText.Text = "";
        }
    }

    // ---------- 今日分享卡片 ----------

    /// <summary>导出近90天专注数据（CSV/JSON），保存位置由用户选择。</summary>
    private async void ExportData_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("CSV 表格", new List<string> { ".csv" });
            picker.FileTypeChoices.Add("JSON 数据", new List<string> { ".json" });
            picker.SuggestedFileName = $"禅净专注数据_{DateTime.Today:yyyyMMdd}";
            if (App.MainWindow is not null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;

            var sessions = _db.GetSessions(DateTime.Today.AddDays(-89), DateTime.Today.AddDays(1));
            var isCsv = file.FileType.Equals(".csv", StringComparison.OrdinalIgnoreCase);
            var content = isCsv ? BuildCsv(sessions) : BuildJson(sessions);
            await Windows.Storage.FileIO.WriteTextAsync(file, content);
            App.LogAction("数据导出", $"{file.Name}（{(isCsv ? "CSV" : "JSON")}，{sessions.Count}条会话）");
            AppServices.Notify(I18n.GetFormat("Notify_ExportSuccess", sessions.Count, file.Name));
        }
        catch (Exception ex)
        {
            App.LogCrash("StatsPage.ExportData", ex);
            AppServices.Notify(I18n.Get("Notify_ExportFailed", "Export failed. Please retry."), Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error);
        }
    }

    private static string BuildCsv(IReadOnlyList<ChanJing.Core.Models.FocusSession> sessions)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("开始时间,结束时间,计划分钟,实际分钟,状态,今日一愿,分心次数,分心来源TOP3,ADHD");
        foreach (var s in sessions)
        {
            var state = s.State switch
            {
                ChanJing.Core.Models.FocusSessionState.Completed => "圆满结束",
                ChanJing.Core.Models.FocusSessionState.Broken => "破功",
                _ => "进行中"
            };
            sb.Append('"').Append(s.StartedAt.ToString("yyyy-MM-dd HH:mm")).Append("\",");
            sb.Append('"').Append(s.EndedAt?.ToString("yyyy-MM-dd HH:mm") ?? "").Append("\",");
            sb.Append(s.PlannedMinutes).Append(',');
            sb.Append(s.ActualMinutes).Append(',');
            sb.Append('"').Append(state).Append("\",");
            sb.Append('"').Append((s.Wish ?? "").Replace("\"", "\"\"")).Append("\",");
            sb.Append(s.DistractionCount).Append(',');
            sb.Append('"').Append((s.DistractionSources ?? "").Replace("\"", "\"\"")).Append("\",");
            sb.Append(s.IsAdhd ? "是" : "否");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string BuildJson(IReadOnlyList<ChanJing.Core.Models.FocusSession> sessions)
    {
        var list = sessions.Select(s => new
        {
            startedAt = s.StartedAt.ToString("yyyy-MM-dd HH:mm"),
            endedAt = s.EndedAt?.ToString("yyyy-MM-dd HH:mm"),
            plannedMinutes = s.PlannedMinutes,
            actualMinutes = s.ActualMinutes,
            state = s.State.ToString(),
            wish = s.Wish,
            distractionCount = s.DistractionCount,
            distractionSources = s.DistractionSources,
            isAdhd = s.IsAdhd
        });
        return System.Text.Json.JsonSerializer.Serialize(
            new { exportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), count = sessions.Count, sessions = list },
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

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
            Title = "Today's Focus Card",
            Content = ShareCard,
            PrimaryButtonText = "Save Image",
            CloseButtonText = "Close",
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
                Title = "Saved",
                Content = $"卡片已保存到：\n{path}\n且已复制到剪贴板，可直接粘贴分享。",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            };
            await info.ShowAsync();
            App.LogAction("保存分享卡片", path);
        }
        catch (Exception ex)
        {
            var error = new ContentDialog
            {
                Title = "Save Failed",
                Content = ex.Message,
                CloseButtonText = "好",
                XamlRoot = XamlRoot
            };
            await error.ShowAsync();
        }
    }

    /// <summary>高效时段分析：近30天按时段统计专注时长，找出最高效时段。</summary>
    private void AnalyzePeakHours()
    {
        try
        {
            var start = DateTime.Today.AddDays(-29);
            var sessions = _db.GetSessions(start, DateTime.Today.AddDays(1));
            if (sessions.Count == 0)
            {
                PeakHourText.Text = "近30天暂无专注记录";
                return;
            }
            var morning = sessions.Where(s => s.StartedAt.Hour >= 6 && s.StartedAt.Hour < 12).Sum(s => s.ActualMinutes);
            var afternoon = sessions.Where(s => s.StartedAt.Hour >= 12 && s.StartedAt.Hour < 18).Sum(s => s.ActualMinutes);
            var evening = sessions.Where(s => s.StartedAt.Hour >= 18 && s.StartedAt.Hour < 24).Sum(s => s.ActualMinutes);
            var night = sessions.Where(s => s.StartedAt.Hour >= 0 && s.StartedAt.Hour < 6).Sum(s => s.ActualMinutes);
            var periods = new (string Name, long Minutes)[] { ("上午6-12点", morning), ("下午12-18点", afternoon), ("晚上18-24点", evening), ("凌晨0-6点", night) };
            var best = periods.OrderByDescending(p => p.Minutes).First();
            var total = periods.Sum(p => p.Minutes);
            var percent = total > 0 ? (int)(best.Minutes * 100.0 / total) : 0;
            PeakHourText.Text = $"你在{best.Name}最专注，近30天共{best.Minutes}分钟（占{percent}%）。建议把重要工作安排在这个时段。";
        }
        catch (Exception ex)
        {
            App.LogCrash("StatsPage.AnalyzePeakHours", ex);
            PeakHourText.Text = "时段分析暂不可用";
        }
    }

    /// <summary>连续纪录历史：计算当前连续天数、最长连续天数、本月专注天数。</summary>
    private void AnalyzeStreakHistory()
    {
        try
        {
            var allSessions = _db.GetSessions(DateTime.Today.AddDays(-365), DateTime.Today.AddDays(1));
            if (allSessions.Count == 0)
            {
                StreakHistoryText.Text = "暂无专注纪录";
                return;
            }
            var currentStreak = 0;
            for (var d = DateTime.Today; d >= DateTime.Today.AddDays(-365); d = d.AddDays(-1))
            {
                if (allSessions.Any(s => s.StartedAt.Date == d.Date))
                {
                    currentStreak++;
                }
                else if (d < DateTime.Today)
                {
                    break;
                }
            }
            var daysWithSessions = allSessions.Select(s => s.StartedAt.Date).Distinct().OrderBy(d => d).ToList();
            var longestStreak = 0;
            var tempStreak = 1;
            for (var i = 1; i < daysWithSessions.Count; i++)
            {
                if ((daysWithSessions[i] - daysWithSessions[i - 1]).Days == 1)
                {
                    tempStreak++;
                }
                else
                {
                    longestStreak = Math.Max(longestStreak, tempStreak);
                    tempStreak = 1;
                }
            }
            longestStreak = Math.Max(longestStreak, tempStreak);
            var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var monthDays = allSessions.Where(s => s.StartedAt.Date >= monthStart).Select(s => s.StartedAt.Date).Distinct().Count();
            StreakHistoryText.Text = $"当前连续{currentStreak}天 · 最长连续{longestStreak}天 · 本月专注{monthDays}天";
        }
        catch (Exception ex)
        {
            App.LogCrash("StatsPage.AnalyzeStreakHistory", ex);
            StreakHistoryText.Text = "纪录统计暂不可用";
        }
    }

    private static Brush? GetBrush(string key) => App.Current.Resources[key] as Brush;

    /// <summary>立即升级按钮：弹升级说明弹窗。</summary>
    private async void Upgrade_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new ContentDialog
            {
                Title = "Upgrade to Pro",
                XamlRoot = XamlRoot,
                PrimaryButtonText = "Enter License Key",
                CloseButtonText = "Later",
                DefaultButton = ContentDialogButton.Primary,
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = "$19 Lifetime, one-time payment forever", FontSize = 16, FontWeight = FontWeights.SemiBold },
                        new TextBlock { Text = "Paid features:", FontSize = 13, FontWeight = FontWeights.SemiBold },
                        new TextBlock { Text = "• Unlimited custom domains (free: 3 max)", FontSize = 12 },
                        new TextBlock { Text = "• All 4 scenes customizable (free: 1 max)", FontSize = 12 },
                        new TextBlock { Text = "• Phone companion (scan to view stats + remote control)", FontSize = 12 },
                        new TextBlock { Text = "• Data export (CSV/JSON)", FontSize = 12 },
                        new TextBlock { Text = "• Advanced stats (peak hour analysis + streak history)", FontSize = 12 },
                        new TextBlock { Text = "• ADHD cooldown 20/30 min (free: 5/10/15)", FontSize = 12 },
                        new TextBlock { Text = "\nTo purchase: email yezhihua_yzh@163.com, license key sent manually after payment.", FontSize = 12, Foreground = GetBrush("BrushTextSecondary") },
                    }
                }
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await ShowActivationDialog();
            }
        }
        catch (Exception ex) { App.LogCrash("StatsPage.Upgrade_Click", ex); }
    }

    /// <summary>激活码输入弹窗：验证通过后写本地 activated=true。</summary>
    private async System.Threading.Tasks.Task ShowActivationDialog()
    {
        try
        {
            var input = new TextBox
            {
                PlaceholderText = "请输入购买后收到的激活码",
                FontSize = 14,
                Padding = new Thickness(12, 8, 12, 8)
            };
            var dialog = new ContentDialog
            {
                Title = I18n.Get("ActivateDialog_Title", "Activate Pro"),
                XamlRoot = XamlRoot,
                PrimaryButtonText = I18n.Get("ActivateDialog_Primary", "Activate"),
                CloseButtonText = I18n.Get("ActivateDialog_Close", "Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = I18n.Get("ActivateDialog_Hint", "Enter license key to unlock all paid features."), FontSize = 12, Foreground = GetBrush("BrushTextSecondary") },
                        input
                    }
                }
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var code = input.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(code))
                {
                    AppServices.Notify(I18n.Get("Notify_KeyEmpty", "License key cannot be empty."));
                    return;
                }
                // HMAC离线验证：格式 CJ-XXXX-XXXXXX
                var ok = AppServices.Blocklist.Activate(code);
                if (ok)
                {
                    App.LogAction("激活正式版", $"激活码={code.Substring(0, Math.Min(6, code.Length))}***");
                    AppServices.Notify(I18n.Get("Notify_KeySuccess", "Activated! All paid features unlocked."));
                    // 刷新当前页面
                    RefreshAll();
                }
                else
                {
                    AppServices.Notify(I18n.Get("Notify_KeyInvalid", "Invalid license key. Format: CJ-XXXX-XXXXXX"));
                }
            }
        }
        catch (Exception ex) { App.LogCrash("StatsPage.ShowActivationDialog", ex); }
    }

    private sealed record UsageItem(string Name, long Value, long Max, string Text, bool IsDistraction = false)
    {
        public Brush NameBrush => IsDistraction
            ? new SolidColorBrush(Color.FromArgb(255, 160, 86, 59)) // 赭石，与警告色统一
            : (Brush)App.Current.Resources["BrushTextPrimary"];
    }
}
