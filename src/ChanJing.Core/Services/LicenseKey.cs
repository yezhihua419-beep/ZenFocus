using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ChanJing.Core.Services;

/// <summary>离线激活码。密钥不进仓库：编译时嵌入 license.secret 或环境变量。</summary>
public static class LicenseKey
{
    private static readonly Regex Pattern = new(@"^CJ-[0-9A-F]{4}-[0-9A-F]{6}$", RegexOptions.Compiled);

    /// <summary>签发一枚码（你发给付款用户）。序号 1–65535。</summary>
    public static string Issue(int seq)
    {
        if (seq is < 1 or > 0xFFFF) throw new ArgumentOutOfRangeException(nameof(seq));
        var hex = seq.ToString("X4");
        return $"CJ-{hex}-{Sign(hex)}";
    }

    public static bool Validate(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var k = key.Trim().ToUpperInvariant();
        if (!Pattern.IsMatch(k)) return false;
        var seq = k.Substring(3, 4);
        return string.Equals(Sign(seq), k.Substring(8, 6), StringComparison.OrdinalIgnoreCase);
    }

    private static string Sign(string seqHex)
    {
        var data = Encoding.UTF8.GetBytes("CJ-" + seqHex);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(LicenseSecretHolder.Value.Trim().Trim('\uFEFF')));
        return Convert.ToHexString(hmac.ComputeHash(data)).Substring(0, 6);
    }
}
