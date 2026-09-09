using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace ChanJing_App;

/// <summary>
/// 主窗口：左侧导航（禅定/屏蔽），关闭按钮最小化到托盘。
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly TrayIconService _tray;
    private bool _exiting;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new SizeInt32(1000, 760));

        Nav.SelectedItem = Nav.MenuItems[0];
        ContentFrame.Navigate(typeof(MainPage));

        // 前台窗口采集常驻（进程名 + 标题哈希，本地存储）。
        AppServices.Activity.Start();

        _tray = new TrayIconService(ShowMain, ExitApp);
        _tray.Show("禅净 — 先管住手，再看清时间");

        AppServices.Activity.LimitExceeded += OnLimitExceeded;
        AppServices.Activity.DistractionDetected += OnDistractionDetected;
        AppServices.Activity.AppBlocked += OnAppBlocked;
        AppWindow.Closing += OnClosing;
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        try
        {
            if (args.SelectedItem is NavigationViewItem item)
            {
                App.LogAction("导航", $"{(item.Tag as string)}");
                switch (item.Tag as string)
                {
                    case "shield":
                        ContentFrame.Navigate(typeof(ShieldPage));
                        break;
                    case "stats":
                        ContentFrame.Navigate(typeof(StatsPage));
                        break;
                    default:
                        ContentFrame.Navigate(typeof(MainPage));
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            App.LogCrash("MainWindow.Nav", ex);
        }
    }

    /// <summary>每日限额超限提醒（事件在后台线程，调度回 UI 弹托盘气泡）。</summary>
    private void OnLimitExceeded(string domain)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                _tray.ShowBalloon($"「{domain}」已达今日限额，休息一下吧。", "禅净 · 每日限额");
                App.LogAction("限额提醒", domain);
            }
            catch (Exception ex)
            {
                App.LogCrash("MainWindow.LimitBalloon", ex);
            }
        });
    }

    /// <summary>专注中打开被屏蔽站点（同一会话每域名只提醒一次）。</summary>
    private void OnDistractionDetected(string domain)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                _tray.ShowBalloon($"你打开了「{domain}」。深呼吸，回到眼前的事。", "禅净 · 分心提醒");
                App.LogAction("分心提醒", domain);
            }
            catch (Exception ex)
            {
                App.LogCrash("MainWindow.DistractionBalloon", ex);
            }
        });
    }

    /// <summary>屏蔽生效时命中分心桌面应用（后台线程，调度回 UI 弹托盘气泡）。</summary>
    private void OnAppBlocked(string processName, string category)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                _tray.ShowBalloon($"「{processName}」属于{category}，已自动最小化。", "禅净 · 桌面应用拦截");
                App.LogAction("拦截桌面应用", $"{processName}({category})");
            }
            catch (Exception ex)
            {
                App.LogCrash("MainWindow.AppBlocked", ex);
            }
        });
    }

    /// <summary>关闭按钮 = 最小化到托盘（托盘菜单"退出"才真正退出）。</summary>
    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_exiting) return;
        args.Cancel = true;
        AppWindow.Hide();
    }

    private void ShowMain()
    {
        AppWindow.Show();
        Activate();
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
        Application.Current.Exit();
    }
}
