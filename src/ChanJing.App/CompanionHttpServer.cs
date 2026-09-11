using System.Net;
using System.Text;
using System.Text.Json;
using ChanJing.Core.Services;
using ChanJing.Core.Models;

namespace ChanJing_App;

/// <summary>
/// 局域网Web伴侣页HTTP服务：手机浏览器扫码访问，查看统计+远程控制专注。
/// 零配置：禅净启动时自动启动，手机和电脑同一WiFi即可访问。
/// </summary>
public sealed class CompanionHttpServer : IDisposable
{
    private HttpListener? _listener;
    private readonly FocusEngine _engine;
    private readonly AppDatabase _db;
    private readonly BlocklistService _blocklist;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private bool _disposed;

    public int Port { get; }
    public bool IsRunning { get; private set; }
    /// <summary>实际监听地址（可能是localhost或所有接口）。</summary>
    public string? ListenAddress { get; private set; }
    /// <summary>是否支持局域网访问（非localhost）。</summary>
    public bool IsLanAccess => ListenAddress != null && !ListenAddress.Contains("localhost");

    public CompanionHttpServer(FocusEngine engine, AppDatabase db, BlocklistService blocklist, int port = 8765)
    {
        _engine = engine;
        _db = db;
        _blocklist = blocklist;
        Port = port;
    }

    public void Start()
    {
        if (_disposed || IsRunning) return;

        // 先尝试监听所有网络接口（需要管理员权限），失败则降级为localhost
        var prefixes = new[] { $"http://+:{Port}/", $"http://localhost:{Port}/" };
        foreach (var prefix in prefixes)
        {
            try
            {
                _listener?.Close();
                _listener = new HttpListener();
                _listener.Prefixes.Add(prefix);
                _listener.Start();
                ListenAddress = prefix;
                IsRunning = true;
                _cts = new CancellationTokenSource();
                var listener = _listener;
                _listenTask = Task.Run(() => ListenLoop(listener, _cts.Token));
                App.LogAction("伴侣服务", $"启动成功 prefix={prefix}");
                return;
            }
            catch (HttpListenerException ex) when (ex.ErrorCode == 5) // 拒绝访问
            {
                App.LogAction("伴侣服务", $"prefix={prefix} 权限不足，尝试降级");
                continue;
            }
            catch (Exception ex)
            {
                App.LogCrash($"CompanionHttpServer.Start({prefix})", ex);
                continue;
            }
        }

        IsRunning = false;
        App.LogAction("伴侣服务", "启动失败：所有prefix均不可用");
    }

    public void Stop()
    {
        if (!IsRunning) return;
        try
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener?.Close();
            _listener = null;
            IsRunning = false;
            App.LogAction("伴侣服务", "停止");
        }
        catch (Exception ex)
        {
            App.LogCrash("CompanionHttpServer.Stop", ex);
        }
    }

    private async Task ListenLoop(HttpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var context = await listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(context), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) { break; }
            catch (Exception ex)
            {
                App.LogCrash("CompanionHttpServer.Listen", ex);
            }
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;
            var path = request.Url?.AbsolutePath ?? "/";
            var method = request.HttpMethod;

            App.LogAction("伴侣请求", $"{method} {path}");

            switch (path)
            {
                case "/" when method == "GET":
                    SendHtml(response, CompanionPage.Html);
                    break;
                case "/api/stats" when method == "GET":
                    SendJson(response, GetStats());
                    break;
                case "/api/focus/status" when method == "GET":
                    SendJson(response, GetFocusStatus());
                    break;
                case "/api/focus/start" when method == "POST":
                    SendJson(response, StartFocus());
                    break;
                case "/api/focus/stop" when method == "POST":
                    SendJson(response, StopFocus());
                    break;
                default:
                    response.StatusCode = 404;
                    SendText(response, "Not Found");
                    break;
            }
        }
        catch (Exception ex)
        {
            App.LogCrash("CompanionHttpServer.Handle", ex);
            try { context.Response.StatusCode = 500; } catch { }
        }
        finally
        {
            try { context.Response.OutputStream.Close(); } catch { }
        }
    }

    private object GetStats()
    {
        var today = DateTime.Today;
        var sessions = _db.GetSessions(today, today.AddDays(1));
        var todayTotal = _engine.GetTodayTotalMinutes();

        // 获取今日使用分布（按应用）
        var usage = _db.GetUsageByDay(today.ToString("yyyy-MM-dd"));
        // 分心应用：短视频/视频娱乐/社交/购物/资讯类
        var distractionApps = usage.Where(kv => IsDistractionApp(kv.Key))
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .Select(kv => new { app = kv.Key, count = (int)(kv.Value / 60) })
            .ToList();
        var distractionCount = distractionApps.Sum(d => d.count);

        return new
        {
            date = today.ToString("yyyy-MM-dd"),
            focusMinutes = todayTotal,
            focusSessions = sessions.Count,
            distractionCount = distractionCount,
            topDistractions = distractionApps,
            serverTime = DateTime.Now.ToString("HH:mm:ss")
        };
    }

    private static bool IsDistractionApp(string appName)
    {
        var lower = appName.ToLowerInvariant();
        string[] distractionKeywords = { "douyin", "抖音", "bilibili", "b站", "哔哩哔哩", "kuaishou", "快手",
            "weibo", "微博", "zhihu", "知乎", "xiaohongshu", "小红书", "taobao", "淘宝", "jd", "京东",
            "pinduoduo", "拼多多", "iqiyi", "爱奇艺", "youku", "优酷", "tencentvideo", "腾讯视频",
            "huya", "虎牙", "douyu", "斗鱼", "youtube", "netflix", "chrome", "edge", "firefox" };
        return distractionKeywords.Any(k => lower.Contains(k));
    }

    private object GetFocusStatus()
    {
        var current = _engine.Current;
        return new
        {
            isFocusing = _engine.IsRunning,
            isPaused = _engine.IsPaused,
            wish = current?.Wish,
            startedAt = current?.StartedAt.ToString("HH:mm:ss"),
            elapsedMinutes = _engine.IsRunning ? (int)_engine.Elapsed.TotalMinutes : 0,
            plannedMinutes = current?.PlannedMinutes ?? 0,
            blocklistEnabled = _blocklist.IsApplied(),
            enabledCategories = _blocklist.GetEnabledCategories().ToList()
        };
    }

    private object StartFocus()
    {
        if (_engine.IsRunning)
        {
            return new { success = false, message = "Already focusing" };
        }
        // 统一走 FocusController 开始逻辑（与首页/托盘/快捷键一致）：读当前场景配置、写 hosts.pre、开始计时
        var result = AppServices.Focus.Start();
        App.LogAction("伴侣控制", $"远程开始专注：{result}");
        return new { success = true, message = "Focus started", wish = _engine.Current?.Wish };
    }

    private object StopFocus()
    {
        if (!_engine.IsRunning)
        {
            return new { success = false, message = "Not currently focusing" };
        }
        // 统一走 FocusController 提前结束逻辑（重置暂离模式+标记破功）
        var result = AppServices.Focus.StopEarly();
        App.LogAction("伴侣控制", $"远程停止专注：{result}");
        return new { success = true, message = "Focus stopped" };
    }

    private static void SendJson(HttpListenerResponse response, object data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    private static void SendHtml(HttpListenerResponse response, string html)
    {
        var bytes = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    private static void SendText(HttpListenerResponse response, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        response.ContentType = "text/plain; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _listener?.Close();
        _cts?.Dispose();
    }
}
