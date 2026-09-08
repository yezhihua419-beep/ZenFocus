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

        AppWindow.Closing += OnClosing;
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item)
        {
            var tag = item.Tag as string;
            if (tag == "shield")
            {
                ContentFrame.Navigate(typeof(ShieldPage));
            }
            else
            {
                ContentFrame.Navigate(typeof(MainPage));
            }
        }
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
