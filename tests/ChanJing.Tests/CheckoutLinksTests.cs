using ChanJing.Core.Services;
using Xunit;

namespace ChanJing.Tests;

public class CheckoutLinksTests
{
    [Fact]
    public void ZhCn_ReturnsAfdian()
    {
        Assert.Equal(CheckoutLinks.Afdian, CheckoutLinks.ForLanguage("zh-CN"));
        Assert.StartsWith("https://afdian.com/a/", CheckoutLinks.Afdian);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("en-US")]
    [InlineData("ja-JP")]
    public void OtherLanguage_NoCheckout(string? lang)
    {
        Assert.Null(CheckoutLinks.ForLanguage(lang));
    }
}
