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

    /// <summary>提权重启时主动释放单实例锁（仅在管理员进程已启动成功后调用，UAC取消不会走到这里）。</summary>
    public void ReleaseSingleInstanceMutex()
    {
        try { _mutex?.ReleaseMutex(); } catch { }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
        // 全局异常兜底：任何 UI 线程/后台线程异常先落盘 crash.log，再决定是否放行。
        // UI 线程异常若 Handled=false 会以 stowed exception 形式闪退，这里记录后放行。
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
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
                MessageBox(IntPtr.Zero, "禅净已在运行，可在右下角托盘找到它。", "禅净", 0x40);
                Exit();
                return;
            }
        }

        _window = new MainWindow();
        _window.Activate();
        LogAction("应用启动");

        // 启动局域网伴侣服务（手机扫码查看统计+远程控制专注）
        try
        {
            _companionServer = new CompanionHttpServer(AppServices.Engine, AppServices.Db, AppServices.Blocklist);
            AppServices.Companion = _companionServer;
            _companionServer.Start();
        }
        catch (Exception ex)
        {
            LogCrash("伴侣服务启动失败", ex);
        }

        // 提权重启后的自动执行：--apply-shield / --remove-shield / --cleanup
        var cmd = Environment.GetCommandLineArgs();
        if (cmd.Contains("--apply-shield")) { RunElevatedAction("apply", "屏蔽已应用。"); }
        else if (cmd.Contains("--remove-shield")) { RunElevatedAction("remove", "屏蔽已撤销。"); }
        else if (cmd.Contains("--cleanup")) { RunElevatedAction("cleanup", "已清理禅净的 hosts 标记段。"); }
    }

    /// <summary>管理员权限下执行屏蔽动作并提示结果（提权重启后的入口）。</summary>
    private async void RunElevatedAction(string action, string successMessage)
    {
        try
        {
            switch (action)
            {
                case "apply":
                    AppServices.Blocklist.Apply();
                    break;
                default:
                    AppServices.Blocklist.Remove();
                    break;
            }
            LogAction("提权执行", $"{action} 成功");
            if (_window?.Content?.XamlRoot is { } root)
            {
                var dialog = new ContentDialog
                {
                    Title = "禅净",
                    Content = successMessage,
                    CloseButtonText = "好",
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
                    Title = "操作失败",
                    Content = $"未能完成操作：{ex.Message}",
                    CloseButtonText = "好",
                    XamlRoot = root
                };
                await dialog.ShowAsync();
            }
        }
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogCrash("UI", e.Exception);
        e.Handled = true; // 记录后阻止闪退；状态不一致可重启恢复
    }

    private static void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        LogCrash("AppDomain", e.ExceptionObject as Exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogCrash("Task", e.Exception);
        e.SetObserved();
    }

    /// <summary>崩溃日志：%LOCALAPPDATA%\ChanJing\crash.log（仅 DEBUG 构建启用，Release 为空实现）。</summary>
    public static void LogCrash(string source, Exception? ex)
    {
#if DEBUG
        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChanJing");
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, "crash.log");
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex}";
            System.IO.File.AppendAllText(path, line + Environment.NewLine);
            LogAction("异常", $"{source}: {ex?.GetType().Name}: {ex?.Message}");
        }
        catch
        {
            // 日志失败不再抛
        }
#endif
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
