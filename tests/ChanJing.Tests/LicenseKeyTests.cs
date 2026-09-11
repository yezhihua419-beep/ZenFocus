using ChanJing.Core.Services;
using Xunit;

namespace ChanJing.Tests;

/// <summary>激活码HMAC验证测试。</summary>
public class LicenseKeyTests
{
    // 以下激活码由 tools/gen_license.py 生成（序号1-5）
    [Theory]
    [InlineData("CJ-0001-CDA66D")]
    [InlineData("CJ-0002-049063")]
    [InlineData("CJ-0003-5F046B")]
    [InlineData("CJ-0004-3283AF")]
    [InlineData("CJ-0005-DB8A3F")]
    public void ValidKey_PassesValidation(string key)
    {
        Assert.True(BlocklistService.ValidateLicenseKey(key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("CJ-0001")]           // 缺签名
    [InlineData("CJ-0001-XXXXXX")]    // 格式对但签名错
    [InlineData("CJ-0001-CDA66E")]    // 最后一位错
    [InlineData("XX-0001-CDA66D")]    // 前缀错
    [InlineData("CJ-000G-CDA66D")]    // 序号含非hex
    [InlineData("cj-0001-cda66d")]    // 小写（应通过，因为ToUpper）
    public void InvalidKey_FailsValidation(string? key)
    {
        // 小写应该通过（内部ToUpper），所以单独处理
        if (key == "cj-0001-cda66d")
        {
            Assert.True(BlocklistService.ValidateLicenseKey(key));
        }
        else
        {
            Assert.False(BlocklistService.ValidateLicenseKey(key));
        }
    }

    [Fact]
    public void LowercaseKey_Accepted()
    {
        // 软件端内部ToUpper，小写输入应通过
        Assert.True(BlocklistService.ValidateLicenseKey("cj-0001-cda66d"));
        Assert.True(BlocklistService.ValidateLicenseKey("Cj-0001-CdA66D"));
    }

    [Fact]
    public void Activate_ValidKey_ReturnsTrue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "chanjing-license-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var db = new AppDatabase(Path.Combine(tempDir, "test.db"));
            var blocklist = new BlocklistService(db);
            Assert.False(blocklist.IsActivated());
            var ok = blocklist.Activate("CJ-0001-CDA66D");
            Assert.True(ok);
            Assert.True(blocklist.IsActivated());
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Activate_InvalidKey_ReturnsFalse()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "chanjing-license-test2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var db = new AppDatabase(Path.Combine(tempDir, "test.db"));
            var blocklist = new BlocklistService(db);
            var ok = blocklist.Activate("CJ-0001-XXXXXX");
            Assert.False(ok);
            Assert.False(blocklist.IsActivated());
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
