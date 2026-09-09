using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace ChanJing_App;

/// <summary>
/// 主窗口：左侧导航（禅定/屏蔽），关闭按钮和最小化按钮均最小化到托盘。
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly TrayIconService _tray;
    private bool _exiting;

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

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
        AppServices.Activity.LimitBlocked += OnLimitBlocked;
        AppServices.Activity.DistractionDetected += OnDistractionDetected;
        AppServices.Activity.AppBlocked += OnAppBlocked;
        AppWindow.Closing += OnClosing;
        AppWindow.Changed += OnAppWindowChanged;
    }

    /// <summary>窗口状态变化：最小化时隐藏窗口（任务栏图标消失），只留托盘图标。</summary>
    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        try
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (IsIconic(hWnd))
            {
                sender.Hide();
                App.LogAction("最小化到托盘");
            }
        }
        catch (Exception ex)
        {
            App.LogCrash("MainWindow.WindowState", ex);
        }
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

    /// <summary>每日限额超限强制最小化（后台线程，调度回 UI 弹托盘气泡）。</summary>
    private void OnLimitBlocked(string domain)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                _tray.ShowBalloon($"{domain} 已达今日上限，窗口已自动最小化。明天再会。", "禅净 · 限额阻断");
                App.LogAction("限额强制阻断", domain);
            }
            catch (Exception ex)
            {
                App.LogCrash("MainWindow.LimitBlockBalloon", ex);
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
                var mode = AppServices.Blocklist.GetAppBlockMode();
                if (mode == "kill")
                {
                    // 先弹提示给3秒保存时间，然后后台杀进程
                    _tray.ShowBalloon($"「{processName}」属于{category}，3秒后将强制结束，请尽快保存未保存内容。", "禅净 · 桌面应用拦截");
                    _ = Task.Run(() =>
                    {
                        Thread.Sleep(3000); // 给用户3秒保存时间
                        var killed = 0;
                        var failed = 0;
                        var procs = System.Diagnostics.Process.GetProcessesByName(processName);
                        foreach (var proc in procs)
                        {
                            try
                            {
                                proc.Kill();
                                killed++;
                            }
                            catch (Exception killEx)
                            {
                                failed++;
                                App.LogAction("结束进程失败", $"{proc.ProcessName} PID={proc.Id} {killEx.GetType().Name}: {killEx.Message}");
                            }
                        }
                        Thread.Sleep(500);
                        var remaining = System.Diagnostics.Process.GetProcessesByName(processName).Length;
                        App.LogAction("结束进程结果", $"{processName} 找到{procs.Length}个 杀掉{killed}个 失败{failed}个 残留{remaining}个");
                    });
                }
                else
                {
                    _tray.ShowBalloon($"「{processName}」属于{category}，已自动最小化。", "禅净 · 桌面应用拦截");
                }
                App.LogAction("拦截桌面应用", $"{processName}({category}) 方式={mode}");
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
        // 从最小化/隐藏状态恢复窗口
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ShowWindow(hWnd, SW_RESTORE);
        Activate();
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
        Application.Current.Exit();
    }
}
