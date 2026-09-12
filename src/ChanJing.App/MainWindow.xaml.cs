using ChanJing.Core.Services;
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
    private bool _hotkeyPressed;
    private FrictionOverlay? _frictionOverlay;

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

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
#if DEBUG
        var smokeNav = Environment.GetEnvironmentVariable("CHANJING_SMOKE_NAV");
        if (smokeNav == "shield")
        {
            Nav.SelectedItem = NavBlock;
            ContentFrame.Navigate(typeof(ShieldPage));
        }
        else if (smokeNav == "stats")
        {
            Nav.SelectedItem = NavStats;
            ContentFrame.Navigate(typeof(StatsPage));
        }
#endif

        // 前台窗口采集常驻（进程名 + 标题哈希，本地存储）。
        AppServices.Activity.Start();

        var title = I18n.Get("MainPage_Title.Text", "ZenFocus");
        Title = title;
        AppTitleBar.Title = title;
        I18n.SetContent(NavFocus, "MainWindow_NavFocus.Content", "Focus");
        I18n.SetContent(NavBlock, "MainWindow_NavBlock.Content", "Block");
        I18n.SetContent(NavStats, "MainWindow_NavStats.Content", "Stats");
        _tray = new TrayIconService(ShowMain, ExitApp, ToggleFocus, ToggleShield, QuickShield, RestBreak);
        _tray.Show(I18n.Get("Tray_Tooltip", "ZenFocus — First block, then see your time"));

        // 全局快捷键检测：Ctrl+Alt+F 开始/结束专注
        var hotkeyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        hotkeyTimer.Tick += (_, _) =>
        {
            // Ctrl+Alt+F 开始/结束专注；Ctrl+Alt+P 暂停/恢复；Ctrl+Alt+R 休息3分钟；Ctrl+Alt+S 打开设置
            // VK_CONTROL=0x11, VK_MENU=0x12(Alt), VK_F=0x46, VK_P=0x50, VK_R=0x52, VK_S=0x53
            bool ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0;
            bool alt = (GetAsyncKeyState(0x12) & 0x8000) != 0;
            bool f = (GetAsyncKeyState(0x46) & 0x8000) != 0;
            bool p = (GetAsyncKeyState(0x50) & 0x8000) != 0;
            bool r = (GetAsyncKeyState(0x52) & 0x8000) != 0;
            bool s = (GetAsyncKeyState(0x53) & 0x8000) != 0;
            if (ctrl && alt && !_hotkeyPressed)
            {
                if (f) { _hotkeyPressed = true; ToggleFocus(); }
                else if (p) { _hotkeyPressed = true; TogglePause(); }
                else if (r) { _hotkeyPressed = true; RestBreak(); }
                else if (s) { _hotkeyPressed = true; ShowSettings(); }
            }
            else if (!(ctrl && alt) || (!f && !p && !r && !s))
            {
                _hotkeyPressed = false;
            }
        };
        hotkeyTimer.Start();

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
            // 最小窗口尺寸：防止缩放过小导致页面内容叠加
            if (args.DidSizeChange)
            {
                var s = sender.Size;
                if (s.Width < 780 || s.Height < 600)
                {
                    sender.Resize(new SizeInt32(Math.Max(s.Width, 780), Math.Max(s.Height, 600)));
                }
            }

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
                _tray.ShowBalloon(I18n.GetFormat("Balloon_Limit", domain), I18n.Get("Balloon_LimitTitle", "ZenFocus · Daily limit"));
                App.LogAction("限额提醒", domain);
            }
            catch (Exception ex)
            {
                App.LogCrash("MainWindow.LimitBalloon", ex);
            }
        });
    }

    /// <summary>每日限额超限强制最小化（后台线程，调度回 UI 弹对话框让用户选择）。</summary>
    private bool _limitDialogOpen;
    private void OnLimitBlocked(string domain)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (_limitDialogOpen) return;
                _limitDialogOpen = true;
                App.LogAction("限额强制阻断", domain);
                _ = ShowLimitDialogAsync(domain);
            }
            catch (Exception ex)
            {
                App.LogCrash("MainWindow.LimitBlockDialog", ex);
            }
        });
    }

    /// <summary>限额超限对话框：可选择临时放行5分钟或就此打住。</summary>
    private async Task ShowLimitDialogAsync(string domain)
    {
        try
        {
            ShowMain();
            var dialog = new ContentDialog
            {
                Title = I18n.Get("LimitDialog_Title", "Daily Limit Reached"),
                Content = I18n.GetFormat("LimitDialog_Content", domain),
                PrimaryButtonText = I18n.Get("LimitDialog_Primary", "Continue anyway (5 min)"),
                CloseButtonText = I18n.Get("LimitDialog_Close", "That's it for today"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = ContentFrame.XamlRoot
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                AppServices.DailyLimits.AddTempAllow(domain, 5);
                AppServices.Notify(I18n.GetFormat("Notify_LimitAllowed", domain));
                App.LogAction("限额临时放行", $"{domain} 5分钟");
            }
            else
            {
                App.LogAction("限额就此打住", domain);
            }
        }
        catch (Exception ex)
        {
            App.LogCrash("MainWindow.ShowLimitDialog", ex);
        }
        finally
        {
            _limitDialogOpen = false;
        }
    }

    /// <summary>专注中打开被屏蔽站点（同一会话每域名只提醒一次）。</summary>
    private void OnDistractionDetected(string domain)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                _tray.ShowBalloon(I18n.GetFormat("Balloon_Dist", domain), I18n.Get("Balloon_DistTitle", "ZenFocus · Distraction"));
                App.LogAction("分心提醒", domain);
            }
            catch (Exception ex)
            {
                App.LogCrash("MainWindow.DistractionBalloon", ex);
            }
        });
    }

    /// <summary>检测到用户持续在 IDE/编辑器中编码（≥2分钟）→ 弹托盘提示建议开启专注。</summary>
    private int _sceneRecommendCount;

    /// <summary>检测到持续编码 → 推荐「工作」场景；推荐满3次后自动切换场景。</summary>
    private void OnCodingDetected(string processName)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                _sceneRecommendCount++;
                if (_sceneRecommendCount >= 3)
                {
                    _sceneRecommendCount = 0;
                    if (AppServices.Engine.IsRunning)
                    {
                        AppServices.Notify(I18n.Get("Notify_CodingDetected", "Detected continuous coding. Suggest Work scene after this session."));
                        return;
                    }
                    AppServices.ApplyScenePreset("work");
                    App.LogAction("场景自动推荐", "第3次触发，已切换工作场景（分类未改）");
                    AppServices.Notify(I18n.Get("Notify_CodingSwitched", "Switched to Work scene (wish/time). Categories stay as on the Block page."));
                    return;
                }
                _tray.ShowBalloon(I18n.GetFormat("Balloon_Coding", processName, _sceneRecommendCount), I18n.Get("Balloon_CodingTitle", "ZenFocus · Scene tip"));
                App.LogAction("场景推荐提示", $"{processName} 第{_sceneRecommendCount}次");
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
                // 商店壳不能杀进程，否则计算器/设置会一起没
                if (mode == "kill" && BlocklistService.IsStoreHostProcess(processName))
                    mode = "minimize";
                if (mode == "kill")
                {
                    // 先弹提示给3秒保存时间，然后后台杀进程
                    _tray.ShowBalloon(I18n.GetFormat("Balloon_Kill", processName, I18n.CategoryName(category)), I18n.Get("Balloon_AppTitle", "ZenFocus · App block"));
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
                        _frictionOverlay.GiveIn += (_, minutes) =>
                        {
                            _frictionOverlay = null;
                            var desc = minutes == 0 ? I18n.Get("Allow_Session", "this session") : I18n.GetFormat("Allow_Minutes", minutes);
                            // 本次专注期间：用24小时（足够长），专注结束时统一清除
                            var actualMinutes = minutes == 0 ? 1440 : minutes;
                            var allowKey = BlocklistService.IsStoreHostProcess(processName)
                                && AppServices.Activity.LastBlockedTarget is { Length: > 0 } t
                                && !BlocklistService.IsStoreHostProcess(t)
                                ? t : processName;
                            AppServices.Blocklist.AddTempAllow(allowKey, actualMinutes);
                            AppServices.Notify(I18n.GetFormat("Notify_AppAllowed", allowKey, desc));
                            App.LogAction("摩擦拦截结果", $"{processName} 用户选择分心，临时放行{desc}");
                        };
                        _frictionOverlay.Closed += (_, _) => { _frictionOverlay = null; };
                    }
                    else if (!AppServices.Engine.IsRunning)
                    {
                        _tray.ShowBalloon(I18n.GetFormat("Balloon_Min", processName, I18n.CategoryName(category)), I18n.Get("Balloon_AppTitle", "ZenFocus · App block"));
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
                // 统一通过FocusController处理开始/结束逻辑，避免散落在多处导致改漏
                var result = AppServices.Focus.Toggle();
                App.LogAction("快捷操作", result);
                // 导航到首页，让用户看到专注界面/反馈
                ContentFrame.Navigate(typeof(MainPage));
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
                if (AppServices.Blocklist.IsManualShieldActive())
                {
                    AppServices.Blocklist.DisableManualShield(AppServices.Engine.IsRunning);
                    AppServices.Notify(I18n.Get("Notify_ShieldOff", "Blocking disabled"));
                }
                else
                {
                    AppServices.Blocklist.EnableManualShield();
                    AppServices.Activity.ApplyShieldNow();
                    AppServices.Notify(I18n.Get("Notify_ShieldOn", "Blocking enabled (untimed)"));
                }
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
                AppServices.Notify(I18n.Get("Notify_ShieldSaved", "Saved: blocking Short Video + Video Entertainment. Applies when focus starts."));
            }
            catch (Exception ex)
            {
                App.LogCrash("MainWindow.QuickShield", ex);
            }
        });
    }

    /// <summary>快捷键：暂停/恢复专注（暂停期间不计入定心时长）。</summary>
    private void TogglePause()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (!AppServices.Engine.IsRunning) return;
                if (AppServices.Engine.IsPaused)
                {
                    AppServices.Engine.Resume();
                    AppServices.Notify(I18n.Get("Notify_Resumed", "Focus resumed"));
                    App.LogAction("快捷键", "恢复专注");
                }
                else
                {
                    AppServices.Engine.Pause();
                    AppServices.Notify(I18n.Get("Notify_Paused", "Paused · paused time not counted"));
                    App.LogAction("快捷键", "暂停专注");
                }
            }
            catch (Exception ex)
            {
                App.LogCrash("MainWindow.TogglePause", ex);
            }
        });
    }

    /// <summary>快捷键：打开设置（显示主窗口并导航到屏蔽页）。</summary>
    private void ShowSettings()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                ShowMain();
                ContentFrame.Navigate(typeof(ShieldPage));
                App.LogAction("快捷键", "打开设置");
            }
            catch (Exception ex)
            {
                App.LogCrash("MainWindow.ShowSettings", ex);
            }
        });
    }

    /// <summary>托盘快捷：休息3分钟（暂离），桌面应用暂停拦截。</summary>
    private void RestBreak()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (!AppServices.Engine.IsRunning) return;
                AppServices.StartRestBreak(3);
                ContentFrame.Navigate(typeof(MainPage));
            }
            catch (Exception ex)
            {
                App.LogCrash("MainWindow.RestBreak", ex);
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
        // 退出时清除系统hosts中的屏蔽条目（防止退出后网站仍被屏蔽）
        try { ChanJing.Core.Services.HostsBlocker.Remove(); }
        catch (UnauthorizedAccessException) { /* 普通权限写不了hosts，跳过 */ }
        catch (Exception ex) { App.LogCrash("ExitApp清理hosts", ex); }
        Close();
        Application.Current.Exit();
    }
}
