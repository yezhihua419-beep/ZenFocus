using ChanJing.Core.Services;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ChanJing_App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;
    private System.Threading.Mutex? _mutex;
    private CompanionHttpServer? _companionServer;

    /// <summary>主窗口引用（供 FileSavePicker 等需要窗口句柄的场景使用）。</summary>
    public static Window? MainWindow { get; private set; }

    /// <summary>提权重启 / 切语言重启会自己接着写 hosts，ProcessExit 不得先剥掉。</summary>
    internal static bool SuppressHostsCleanupOnProcessExit { get; set; }

    /// <summary>提权重启时主动释放单实例锁（仅在管理员进程已启动成功后调用，UAC取消不会走到这里）。</summary>
    public void ReleaseSingleInstanceMutex()
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        try { _mutex?.Dispose(); } catch { }
        _mutex = null;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        // 未打包进程必须用 WASDK API；Windows.Globalization 那套需要包身份，会 0xC000027B。
        // 必须在任何资源（InitializeComponent / ResourceLoader）加载之前设置。
        ApplyLanguageOverride();

        InitializeComponent();

        // 全局异常兜底：任何 UI 线程/后台线程异常先落盘 crash.log，再决定是否放行。
        // UI 线程异常若 Handled=false 会以 stowed exception 形式闪退，这里记录后放行。
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        // 杀进程/崩溃时 ExitApp 走不到；ProcessExit 能跑就尽量剥标记段（提权/切语言重启要跳过）
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    private static string LanguageFilePath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChanJing", "language.txt");

    /// <summary>读取语言偏好。只读独立小文件，启动路径禁止碰 SQLite。</summary>
    public static string GetLanguage()
    {
        try
        {
            if (File.Exists(LanguageFilePath))
            {
                var saved = File.ReadAllText(LanguageFilePath).Trim();
                if (saved is "en-US" or "zh-CN") return saved;
            }
        }
        catch { }
        return "en-US";
    }

    /// <summary>保存语言偏好。未打包下 WASDK 覆盖不持久化，必须自己落盘。</summary>
    public static void SetLanguage(string lang)
    {
        if (lang is not ("en-US" or "zh-CN")) lang = "en-US";
        try
        {
            var dir = System.IO.Path.GetDirectoryName(LanguageFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(LanguageFilePath, lang);
        }
        catch { }
        try { AppServices.Db.SetSetting("app_language", lang); } catch { }
    }

    /// <summary>切换语言并重启，供托盘/设置共用。</summary>
    public static void SwitchLanguage(string lang)
    {
        SetLanguage(lang);
        LogAction("语言切换重启", lang);
        if (Current is App app)
            app.RestartForLanguageChange();
    }

    private static void ApplyLanguageOverride()
    {
        var lang = GetLanguage();
        try
        {
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = lang;
            ChanJing.Core.Services.FocusContext.ResolveWish = I18n.DisplayWish;
            // 名单语言独立落盘：切界面语言不得把抖音名单换成 TikTok
            ChanJing.Core.Services.BlocklistService.ResolveLocale = () =>
                ChanJing.Core.Services.BlocklistService.ReadCatalogLocale(GetLanguage());
            LogAction("语言覆盖", lang + " catalog=" + ChanJing.Core.Services.BlocklistService.ReadCatalogLocale(lang));
        }
        catch (Exception ex)
        {
            LogCrash("LanguageOverride", ex);
        }
    }

    /// <summary>语言切换后重启：先放单实例锁，再拉起新进程。</summary>
    public void RestartForLanguageChange()
    {
        try
        {
            try
            {
                if (AppServices.Engine.IsRunning)
                    AppServices.Engine.Finish(completed: false);
                AppServices.Blocklist.EmergencyPass = false;
                if (!AppServices.Blocklist.IsManualShieldActive())
                    AppServices.Blocklist.PauseSystemHosts();
            }
            catch (Exception ex) { LogCrash("LanguageRestart.Cleanup", ex); }

            ReleaseSingleInstanceMutex();
            SuppressHostsCleanupOnProcessExit = true;
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
                {
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            LogCrash("LanguageRestart", ex);
        }
        Environment.Exit(0);
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // 单实例：防双开并发写 hosts / 限额计时冲突
        _mutex = new System.Threading.Mutex(true, @"Local\ChanJing.App.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            // 提权重启时旧进程可能还没完全退出，等待 1.5 秒后重试一次
            System.Threading.Thread.Sleep(1500);
            _mutex = new System.Threading.Mutex(true, @"Local\ChanJing.App.SingleInstance", out createdNew);
            if (!createdNew)
            {
                MessageBox(IntPtr.Zero, I18n.Get("App_AlreadyRunning", "ZenFocus is already running. Find it in the system tray."), I18n.Get("App_AlreadyRunningTitle", "ZenFocus"), 0x40);
                Exit();
                return;
            }
        }

#if DEBUG
        // 虚拟测试钩子：写偏好并走与托盘相同的重启路径（子进程不继承此变量）
        var smokeRestart = Environment.GetEnvironmentVariable("CHANJING_SMOKE_RESTART");
        if (smokeRestart is "en-US" or "zh-CN")
        {
            Environment.SetEnvironmentVariable("CHANJING_SMOKE_RESTART", null);
            SwitchLanguage(smokeRestart);
            return;
        }
#endif

        _window = new MainWindow();
        MainWindow = _window;
        try { AppServices.LoadUi(); } catch (Exception ex) { LogCrash("LoadUi", ex); }
        _window.Activate();
        LogAction("应用启动");

        // 启动前台窗口活动追踪（记录应用使用时间+分心检测+桌面App拦截）
        try
        {
            AppServices.Activity.Start();
            LogAction("活动追踪启动");
        }
        catch (Exception ex)
        {
            LogCrash("活动追踪启动失败", ex);
        }

        // 启动局域网伴侣服务（手机扫码查看统计+远程控制专注）——付费功能，免费版不启动
        try
        {
            if (AppServices.Blocklist.IsActivated())
            {
                _companionServer = new CompanionHttpServer(AppServices.Engine, AppServices.Db, AppServices.Blocklist);
                AppServices.Companion = _companionServer;
                _companionServer.Start();
                LogAction("伴侣服务", "已启动（付费版）");
            }
            else
            {
                AppServices.Companion = null;
                LogAction("伴侣服务", "免费版不启动（付费功能）");
            }
        }
        catch (Exception ex)
        {
            LogCrash("伴侣服务启动失败", ex);
        }

        try
        {
            if (AppServices.Blocklist.TryClearOrphanSystemHosts(AppServices.Engine.IsRunning))
            {
                LogAction("启动清残留hosts", "已清除");
                AppServices.Notify(I18n.Get("Notify_OrphanHostsCleared", "Cleared leftover website blocks from last exit."));
            }
            else if (!AppServices.Engine.IsRunning
                     && !AppServices.Blocklist.IsManualShieldActive()
                     && ChanJing.Core.Services.HostsBlocker.IsApplied())
            {
                LogAction("启动清残留hosts", "需要管理员，已跳过");
                AppServices.Notify(I18n.Get("Notify_OrphanHostsNeedAdmin", "Leftover website blocks found. Run as administrator to clear them."), Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
            }
            else if (AppServices.Blocklist.IsManualShieldActive()
                     && !ChanJing.Core.Services.HostsBlocker.IsApplied())
            {
                // 手动屏蔽应跨重启仍在；ProcessExit/杀进程若已剥段，这里补写
                AppServices.Blocklist.RestoreSystemHostsIfNeeded(true);
                LogAction("启动清残留hosts", "手动屏蔽，已重写");
            }
        }
        catch (Exception ex) { LogCrash("启动清残留hosts", ex); }

        // 屏蔽绑定专注：开始专注时把预应用配置同步到系统hosts，结束专注时清除系统hosts
        AppServices.Engine.FocusStarted += () =>
        {
            try
            {
                // 标记专注中（用于FocusOnlyCommunication判断）
                AppServices.Blocklist.IsFocusRunning = true;
                // 重置暂离模式（防止上一次专注未正确重置）
                AppServices.Blocklist.EmergencyPass = false;
                // 写hosts异步执行，避免UAC提权等待阻塞UI线程（桌面App屏蔽由WindowActivityService同步立即执行）
                if (!AppServices.Blocklist.IsManualShieldActive())
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            var domains = ChanJing.Core.Services.HostsBlocker.GetPreAppliedDomains();
                            if (domains.Count > 0)
                            {
                                // Apply 先剥旧 BEGIN/END（含崩溃残缺段）再写
                                ChanJing.Core.Services.HostsBlocker.Apply(domains);
                                LogAction("专注开始", $"自动应用网站屏蔽（{domains.Count}个域名）");
                            }
                        }
                        catch (UnauthorizedAccessException)
                        {
                            LogAction("专注开始", "网站屏蔽需要管理员权限，已跳过（桌面App屏蔽仍生效）");
                            AppServices.Notify(I18n.Get("Notify_HostsSkipped", "Website blocking skipped (needs administrator). Desktop app blocking is still on."), Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
                        }
                    });
                }

                // 方案B：开始专注时统一保存当前配置到场景（只对已自定义的场景自动记忆，避免消耗免费额度）
                // 所有开始专注路径（首页按钮/托盘/伴侣页/快捷键）都经过 FocusStarted，保证逻辑一致
                var sceneTag = AppServices.CurrentSceneTag;
                if (!string.IsNullOrEmpty(sceneTag) && SceneManager.IsCustomized(AppServices.Db, sceneTag))
                {
                    var cats = AppServices.Blocklist.GetEnabledCategories().ToArray();
                    SceneManager.SaveSceneConfig(AppServices.Db, sceneTag, new SceneManager.SceneConfig(I18n.StoreWish(sceneTag, AppServices.CurrentWish), AppServices.CurrentMinutes, cats));
                    LogAction("场景自动记忆", $"{sceneTag} {AppServices.CurrentMinutes}分钟 屏蔽=[{string.Join("/", cats)}]");
                }
            }
            catch (UnauthorizedAccessException)
            {
                LogAction("专注开始", "网站屏蔽需要管理员权限，已跳过（桌面App屏蔽仍生效）");
                AppServices.Notify(I18n.Get("Notify_HostsSkipped", "Website blocking skipped (needs administrator). Desktop app blocking is still on."), Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
            }
            catch (Exception ex)
            {
                LogCrash("专注开始-应用屏蔽失败", ex);
            }
        };
        AppServices.Engine.FocusFinished += (completed) =>
        {
            try
            {
                // 判断是否ADHD模式（事件触发时Current还在）
                var isAdhd = AppServices.Engine.Current?.IsAdhd ?? false;
                // 标记非专注中
                AppServices.Blocklist.IsFocusRunning = false;
                // 重置暂离模式（所有结束专注路径统一重置）
                AppServices.Blocklist.EmergencyPass = false;
                // 清除所有临时放行（"本次专注期间"放行的应用在专注结束后重新生效屏蔽）
                AppServices.Blocklist.ClearAllTempAllows();
                if (AppServices.Blocklist.IsManualShieldActive())
                {
                    LogAction("focus-finish", "manual shield active, keep hosts");
                }
                else if (isAdhd)
                {
                    // ADHD模式：不立即解除屏蔽，进入缓冲期（10分钟后由UI层软着陆解除）
                    LogAction("focus-finish", $"adhd mode, keep hosts for cooldown, completed={completed}");
                }
                else if (ChanJing.Core.Services.HostsBlocker.IsApplied())
                {
                    ChanJing.Core.Services.HostsBlocker.Remove();
                    LogAction("focus-finish", completed ? "completed, remove hosts" : "broken, remove hosts");
                }
            }
            catch (UnauthorizedAccessException)
            {
                LogAction("focus-finish", "need admin, skipped");
            }
            catch (Exception ex)
            {
                LogCrash("focus-finish error", ex);
            }
        };
        // 提权重启后的自动执行：--apply-shield / --remove-shield / --cleanup
        var cmd = Environment.GetCommandLineArgs();
        if (cmd.Contains("--apply-shield")) { RunElevatedAction("apply", I18n.Get("Elevated_Apply", "Blocking applied.")); }
        else if (cmd.Contains("--remove-shield")) { RunElevatedAction("remove", I18n.Get("Elevated_Remove", "Blocking removed.")); }
        else if (cmd.Contains("--cleanup")) { RunElevatedAction("cleanup", I18n.Get("Elevated_Cleanup", "ZenFocus hosts markers cleaned.")); }
    }

    /// <summary>管理员权限下执行屏蔽动作并提示结果（提权重启后的入口）。</summary>
    private async void RunElevatedAction(string action, string successMessage)
    {
        try
        {
            switch (action)
            {
                case "apply":
                    var domains = ChanJing.Core.Services.HostsBlocker.GetPreAppliedDomains();
                    if (domains.Count > 0)
                    {
                        ChanJing.Core.Services.HostsBlocker.Apply(domains);
                    }
                    break;
                default:
                    ChanJing.Core.Services.HostsBlocker.Remove();
                    break;
            }
            LogAction("提权执行", $"{action} 成功");
            if (_window?.Content?.XamlRoot is { } root)
            {
                var dialog = new ContentDialog
                {
                    Title = I18n.Get("App_AlreadyRunningTitle", "ZenFocus"),
                    Content = successMessage,
                    CloseButtonText = I18n.Get("Common_OK.Content", "OK"),
                    XamlRoot = root
                };
                await dialog.ShowAsync();
            }
        }
        catch (Exception ex)
        {
            LogCrash("ElevatedAction", ex);
            if (_window?.Content?.XamlRoot is { } root)
            {
                var dialog = new ContentDialog
                {
                    Title = I18n.Get("Stats_ShareFail", "Operation failed"),
                    Content = I18n.GetFormat("Elevated_Fail", ex.Message),
                    CloseButtonText = I18n.Get("Common_OK.Content", "OK"),
                    XamlRoot = root
                };
                await dialog.ShowAsync();
            }
        }
    }

    private static int _crashPrompted;

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogCrash("UI", e.Exception);
        e.Handled = true; // 记录后阻止闪退；状态不一致可重启恢复
        PromptCrashOnce();
    }

    private static void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        LogCrash("AppDomain", e.ExceptionObject as Exception);
        if (e.IsTerminating)
            TryClearHostsOnAbnormalExit();
        PromptCrashOnce();
    }

    private static void OnProcessExit(object? sender, EventArgs e)
        => TryClearHostsOnAbnormalExit();

    /// <summary>托盘退出之外的死法：尽量剥标记段。手动屏蔽跨重启靠启动时重写。</summary>
    private static void TryClearHostsOnAbnormalExit()
    {
        if (SuppressHostsCleanupOnProcessExit) return;
        try { HostsBlocker.TryRemove(); }
        catch { }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogCrash("Task", e.Exception);
        e.SetObserved();
    }

    /// <summary>Release 也写：客服要看得见。人话弹窗只出一次，避免连环框。</summary>
    private static void PromptCrashOnce()
    {
        if (System.Threading.Interlocked.Exchange(ref _crashPrompted, 1) != 0) return;
        try
        {
            var path = CrashLogPath;
            var msg = "ZenFocus hit a problem and wrote a log. Restart if the app looks wrong.\n\n" + path;
            try
            {
                var localized = I18n.GetFormat("Crash_Prompt", path);
                if (!string.IsNullOrWhiteSpace(localized) && localized != "Crash_Prompt")
                    msg = localized;
            }
            catch { }
            MessageBox(IntPtr.Zero, msg, "ZenFocus", 0x40);
        }
        catch { }
    }

    public static string CrashLogPath =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChanJing", "crash.log");

    /// <summary>崩溃日志：Release 也写 %LOCALAPPDATA%\ChanJing\crash.log。例行 catch 只记文件，不弹窗。</summary>
    public static void LogCrash(string source, Exception? ex)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(CrashLogPath);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex}";
            System.IO.File.AppendAllText(CrashLogPath, line + Environment.NewLine);
            LogAction("异常", $"{source}: {ex?.GetType().Name}: {ex?.Message}");
        }
        catch
        {
            // 日志失败不再抛
        }
    }

    /// <summary>操作流水日志：%LOCALAPPDATA%\ChanJing\actions.log（仅 DEBUG 构建启用，Release 为空实现）。</summary>
    public static void LogAction(string action, string? detail = null)
    {
#if DEBUG
        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChanJing");
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, "actions.log");
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {action}" + (detail is null ? "" : $" | {detail}");
            System.IO.File.AppendAllText(path, line + Environment.NewLine);
        }
        catch
        {
            // 日志失败不再抛
        }
#endif
    }
}
