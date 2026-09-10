using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Dispatching;
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
    private FrictionOverlay? _frictionOverlay;

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

        _tray = new TrayIconService(ShowMain, ExitApp, ToggleFocus, ToggleShield, QuickShield);
        _tray.Show("禅净 — 先管住手，再看清时间");

        AppServices.Activity.LimitExceeded += OnLimitExceeded;
        AppServices.Activity.LimitBlocked += OnLimitBlocked;
        AppServices.Activity.DistractionDetected += OnDistractionDetected;
        AppServices.Activity.AppBlocked += OnAppBlocked;
        AppServices.Activity.CodingDetected += OnCodingDetected;
        AppWindow.Closing += OnClosing;
        // 全局通知承载：页面/服务统一走 AppServices.Notify，由本窗口 InfoBar 显示
        AppServices.NotifyHandler = (message, severity) =>
        {
            try
            {
                DispatcherQueue.TryEnqueue(() => ShowNotify(message, severity));
            }
            catch { }
        };
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

    /// <summary>检测到用户持续在 IDE/编辑器中编码（≥2分钟）→ 弹托盘提示建议开启专注。</summary>
    private void OnCodingDetected(string processName)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                _tray.ShowBalloon($"检测到你在「{processName}」中持续编码2分钟了，建议开启专注模式，屏蔽分心应用。", "禅净 · 编码检测");
                App.LogAction("编码检测提示", processName);
            }
            catch (Exception ex)
            {
                App.LogCrash("MainWindow.CodingBalloon", ex);
            }
        });
    }

    /// <summary>屏蔽生效时命中分心桌面应用（后台线程，调度回 UI 弹托盘气泡）。</summary>
    private void OnAppBlocked(string processName, string category)
    {
        App.LogAction("桌面应用拦截", $"{processName}({category}) 已最小化");
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
                    // 专注中：显示摩擦式拦截全屏遮罩（5秒冷静期，one sec科学背书减少57%分心）
                    // 非专注中：只弹托盘气泡，不打断用户
                    if (AppServices.Engine.IsRunning && _frictionOverlay is null)
                    {
                        _frictionOverlay = new FrictionOverlay(processName, category);
                        _frictionOverlay.ContinueFocus += (_, _) =>
                        {
                            _frictionOverlay = null;
                            App.LogAction("摩擦拦截结果", $"{processName} 用户选择继续专注");
                        };
                        _frictionOverlay.GiveIn += (_, _) =>
                        {
                            _frictionOverlay = null;
                            // 用户选择分心：临时放行该应用5分钟
                            AppServices.Blocklist.AddTempAllow(processName, 5);
                            App.LogAction("摩擦拦截结果", $"{processName} 用户选择分心，临时放行5分钟");
                        };
                        _frictionOverlay.Closed += (_, _) => { _frictionOverlay = null; };
                    }
                    else if (!AppServices.Engine.IsRunning)
                    {
                        _tray.ShowBalloon($"「{processName}」属于{category}，已自动最小化。", "禅净 · 桌面应用拦截");
                    }
                }
                App.LogAction("拦截桌面应用", $"{processName}({category}) 方式={mode}");
            }
            catch (Exception ex)
            {
                App.LogCrash("MainWindow.AppBlocked", ex);
            }
        });
    }

    /// <summary>托盘菜单：快速开始/暂停专注。</summary>
    private void ToggleFocus()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (AppServices.Engine.IsRunning)
                {
                    if (AppServices.Engine.IsPaused)
                    {
                        AppServices.Engine.Resume();
                        App.LogAction("托盘快捷操作", "恢复专注");
                    }
                    else
                    {
                        AppServices.Engine.Pause();
                        App.LogAction("托盘快捷操作", "暂停专注");
                    }
                }
                else
                {
                    // 走当前场景配置（愿望+时长+屏蔽分类），与首页「开始专注」一致
                    var sceneConfig = SceneManager.GetSceneConfig(AppServices.Db, AppServices.CurrentSceneTag);
                    if (!string.IsNullOrEmpty(AppServices.CurrentSceneTag) && !string.IsNullOrEmpty(sceneConfig.Wish))
                    {
                        AppServices.Blocklist.SetEnabledCategories(sceneConfig.Categories);
                        AppServices.Blocklist.Apply(); // 写 hosts.pre，专注开始时同步到系统 hosts
                        AppServices.CurrentWish = sceneConfig.Wish;
                        AppServices.CurrentMinutes = sceneConfig.Minutes;
                        AppServices.Engine.Start(sceneConfig.Wish, sceneConfig.Minutes);
                        App.LogAction("托盘快捷操作", $"开始专注 {sceneConfig.Minutes}分钟（场景 {AppServices.CurrentSceneTag}）");
                    }
                    else
                    {
                        AppServices.CurrentWish = "专注";
                        AppServices.CurrentMinutes = 25;
                        AppServices.Engine.Start("专注", 25);
                        App.LogAction("托盘快捷操作", "开始专注25分钟（未选场景）");
                    }
                }
            }
            catch (Exception ex)
            {
                App.LogCrash("MainWindow.ToggleFocus", ex);
            }
        });
    }

    /// <summary>托盘菜单：快速开启/关闭屏蔽（打开主窗口导航到屏蔽页）。</summary>
    private void ToggleShield()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                ShowMain();
                // 导航到屏蔽页
                ContentFrame.Navigate(typeof(ShieldPage));
                App.LogAction("托盘快捷操作", "打开屏蔽设置");
            }
            catch (Exception ex)
            {
                App.LogCrash("MainWindow.ToggleShield", ex);
            }
        });
    }

    /// <summary>显示全局通知条（InfoBar），4 秒后自动收起；同类消息刷新计时。</summary>
    private DispatcherQueueTimer? _notifyTimer;
    private void ShowNotify(string message, Microsoft.UI.Xaml.Controls.InfoBarSeverity severity)
    {
        try
        {
            NotifyBar.Message = message;
            NotifyBar.Severity = severity;
            NotifyBar.IsOpen = true;
            _notifyTimer ??= DispatcherQueue.CreateTimer();
            _notifyTimer.Interval = TimeSpan.FromSeconds(4);
            _notifyTimer.IsRepeating = false;
            _notifyTimer.Tick += (s, e) => NotifyBar.IsOpen = false;
            _notifyTimer.Stop();
            _notifyTimer.Start();
        }
        catch (Exception ex)
        {
            App.LogCrash("MainWindow.ShowNotify", ex);
        }
    }

    /// <summary>托盘快捷：一键保存屏蔽 抖音/B站（短视频+视频娱乐分类），专注开始后生效。</summary>
    private void QuickShield()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                AppServices.Blocklist.SetEnabledCategories(new[] { "短视频", "视频娱乐" });
                AppServices.Blocklist.Apply();
                App.LogAction("托盘快捷操作", "一键屏蔽抖音/B站（短视频+视频娱乐）");
                AppServices.Notify("已保存：屏蔽 短视频+视频娱乐，开始专注后生效。");
            }
            catch (Exception ex)
            {
                App.LogCrash("MainWindow.QuickShield", ex);
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
