using System.Text;

namespace ChanJing.Core.Services;

/// <summary>
/// hosts 文件标记段管理器。
/// 只管理 <c># BEGIN CHANJING</c> 与 <c># END CHANJING</c> 之间的条目，
/// 绝不触碰用户其他配置（广告拦截、SwitchHosts 等），写入前自动备份。
/// </summary>
public static class HostsBlocker
{
    public const string BeginMarker = "# BEGIN CHANJING";
    public const string EndMarker = "# END CHANJING";

    private static readonly string DefaultHostsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "drivers", "etc", "hosts");

    /// <summary>测试注入点：可替换 hosts 文件路径。</summary>
    public static string? HostsPathOverride { get; set; }

    public static string HostsPath => HostsPathOverride ?? DefaultHostsPath;

    /// <summary>测试注入点：可替换 hosts.pre 路径，避免单测改用户真实配置。</summary>
    public static string? PreApplyPathOverride { get; set; }

    /// <summary>预应用临时文件路径（屏蔽配置先存这里，专注开始后才同步到系统hosts）。</summary>
    private static string PreApplyPath =>
        PreApplyPathOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChanJing", "hosts.pre");

    /// <summary>诊断用：能否打开 hosts 写入。不改文件内容。</summary>
    public static bool CanWrite()
    {
        try
        {
            if (!File.Exists(HostsPath)) return false;
            using var fs = new FileStream(HostsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            return fs.CanWrite;
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
    }

    /// <summary>当前是否已应用屏蔽段（系统hosts中）。</summary>
    public static bool IsApplied()
    {
        if (!File.Exists(HostsPath)) return false;
        return File.ReadAllText(HostsPath).Contains(BeginMarker, StringComparison.Ordinal);
    }

    /// <summary>是否已有预应用配置（临时文件中，专注开始后同步到系统hosts）。</summary>
    public static bool IsPreApplied() => File.Exists(PreApplyPath);

    /// <summary>预应用：把屏蔽配置存到临时文件，不写系统hosts（屏蔽页"应用屏蔽"调用）。</summary>
    public static void PreApply(IEnumerable<string> domains)
    {
        var dir = Path.GetDirectoryName(PreApplyPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var lines = new List<string>();
        foreach (var domain in domains)
        {
            var d = domain.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(d)) continue;
            lines.Add(d);
        }
        File.WriteAllLines(PreApplyPath, lines, Encoding.UTF8);
    }

    /// <summary>从预应用临时文件读取域名列表。</summary>
    public static List<string> GetPreAppliedDomains()
    {
        if (!File.Exists(PreApplyPath)) return new List<string>();
        return File.ReadAllLines(PreApplyPath, Encoding.UTF8)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim())
            .ToList();
    }

    /// <summary>清除预应用临时文件。</summary>
    public static void ClearPreApply()
    {
        if (File.Exists(PreApplyPath)) File.Delete(PreApplyPath);
    }

    /// <summary>
    /// 应用屏蔽：将域名列表写入标记段。域名自动补 www 前缀。
    /// </summary>
    public static void Apply(IEnumerable<string> domains)
    {
        var path = HostsPath;
        BackupIfNeeded(path);

        var lines = File.Exists(path)
            ? File.ReadAllLines(path).ToList()
            : new List<string>();

        lines = RemoveBlock(lines);

        var block = new List<string> { BeginMarker };
        foreach (var domain in domains)
        {
            var d = domain.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(d)) continue;
            block.Add("127.0.0.1 " + d);
            block.Add("127.0.0.1 www." + d);
        }
        block.Add(EndMarker);
        lines.AddRange(block);

        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    /// <summary>移除屏蔽段（恢复 hosts）。</summary>
    public static void Remove()
    {
        if (!File.Exists(HostsPath)) return;
        var lines = File.ReadAllLines(HostsPath).ToList();
        var cleaned = RemoveBlock(lines);
        if (cleaned.Count != lines.Count)
        {
            File.WriteAllLines(HostsPath, cleaned, Encoding.UTF8);
        }
    }

    /// <summary>杀进程/崩溃路径用：权限或 IO 失败时 false，不抛。</summary>
    public static bool TryRemove()
    {
        try
        {
            Remove();
            return true;
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
    }

    /// <summary>备份 hosts 到同目录 hosts.chanjing.bak（仅首次写入前）。</summary>
    private static void BackupIfNeeded(string path)
    {
        if (!File.Exists(path)) return;
        var bak = path + ".chanjing.bak";
        if (!File.Exists(bak))
        {
            File.Copy(path, bak);
        }
    }

    /// <summary>
    /// 去掉标记段。崩溃若只写了 BEGIN 没写 END，旧逻辑会吞掉后面全部用户 hosts；
    /// 残缺段只剥 127.0.0.1 行，遇到其它行立刻停。
    /// </summary>
    private static List<string> RemoveBlock(List<string> lines)
    {
        var result = new List<string>();
        var i = 0;
        while (i < lines.Count)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith(BeginMarker, StringComparison.Ordinal))
            {
                i++;
                while (i < lines.Count)
                {
                    var inner = lines[i].Trim();
                    if (inner.StartsWith(EndMarker, StringComparison.Ordinal))
                    {
                        i++;
                        break;
                    }
                    if (inner.StartsWith(BeginMarker, StringComparison.Ordinal))
                        break;
                    if (inner.Length == 0 || IsLoopbackHostsLine(inner))
                    {
                        i++;
                        continue;
                    }
                    break; // 残缺 BEGIN：后面是用户自己的条目
                }
                continue;
            }
            if (trimmed.StartsWith(EndMarker, StringComparison.Ordinal))
            {
                i++;
                continue;
            }
            result.Add(lines[i]);
            i++;
        }
        return result;
    }

    private static bool IsLoopbackHostsLine(string trimmed)
        => trimmed.StartsWith("127.0.0.1 ", StringComparison.Ordinal)
           || trimmed.StartsWith("127.0.0.1\t", StringComparison.Ordinal);
}
