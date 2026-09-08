using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;

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

        Nav.SelectedItem = Nav.MenuItems[0];
        ContentFrame.Navigate(typeof(MainPage));

        // 前台窗口采集常驻（进程名 + 标题哈希，本地存储）。
        AppServices.Activity.Start();

        _tray = new TrayIconService(ShowMain, ExitApp);
        _tray.Show("禅净 — 先管住手，再看清时间");

        AppServices.Activity.LimitExceeded += OnLimitExceeded;
        AppWindow.Closing += OnClosing;
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item)
        {
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

    /// <summary>每日限额超限提醒（事件在后台线程，调度回 UI 弹托盘气泡）。</summary>
    private void OnLimitExceeded(string domain)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _tray.ShowBalloon($"「{domain}」已达今日限额，休息一下吧。", "禅净 · 每日限额");
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
