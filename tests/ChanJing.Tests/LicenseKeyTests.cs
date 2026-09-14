using ChanJing.Core.Services;
using Xunit;

namespace ChanJing.Tests;

/// <summary>激活码 HMAC：真码由 Issue 现算，仓库里不放能用的样例。</summary>
public class LicenseKeyTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void Issue_PassesValidation(int seq)
    {
        var key = LicenseKey.Issue(seq);
        Assert.True(LicenseKey.Validate(key));
        Assert.True(BlocklistService.ValidateLicenseKey(key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("CJ-0001")]
    [InlineData("CJ-0001-XXXXXX")]
    [InlineData("XX-0001-AAAAAA")]
    [InlineData("CJ-000G-AAAAAA")]
    public void InvalidKey_FailsValidation(string? key)
    {
        Assert.False(LicenseKey.Validate(key));
    }

    [Fact]
    public void TamperedSignature_Fails()
    {
        var key = LicenseKey.Issue(1);
        var bad = key.Substring(0, key.Length - 1) + (key[^1] == '0' ? '1' : '0');
        Assert.False(LicenseKey.Validate(bad));
    }

    [Fact]
    public void LowercaseKey_Accepted()
    {
        var key = LicenseKey.Issue(1);
        Assert.True(LicenseKey.Validate(key.ToLowerInvariant()));
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
            Assert.True(blocklist.Activate(LicenseKey.Issue(1)));
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
            Assert.False(blocklist.Activate("CJ-0001-XXXXXX"));
            Assert.False(blocklist.IsActivated());
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Issue_RejectsBadSeq()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LicenseKey.Issue(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => LicenseKey.Issue(0x10000));
    }
}
