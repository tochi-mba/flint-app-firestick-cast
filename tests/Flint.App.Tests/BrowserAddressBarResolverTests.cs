using Flint.App.Services;
using Shouldly;

namespace Flint.App.Tests;

public sealed class BrowserAddressBarResolverTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolve_Empty_ReturnsNull(string? raw) =>
        BrowserAddressBarResolver.TryResolve(raw).ShouldBeNull();

    [Theory]
    [InlineData("https://example.com/path", "https://example.com/path")]
    [InlineData("http://example.com/a", "https://example.com/a")]
    [InlineData("example.com", "https://example.com/")]
    [InlineData("www.bbc.co.uk/news", "https://www.bbc.co.uk/news")]
    public void TryResolve_UrlLike_ReturnsHttps(string raw, string expected) =>
        BrowserAddressBarResolver.TryResolve(raw).ShouldBe(expected);

    [Theory]
    [InlineData("weather in london", "weather%20in%20london")]
    [InlineData("fire tv tips", "fire%20tv%20tips")]
    [InlineData("maps", "maps")]
    [InlineData("C#", "C%23")]
    public void TryResolve_SearchText_BecomesGoogleQuery(string raw, string encodedQuery)
    {
        var url = BrowserAddressBarResolver.TryResolve(raw);
        url.ShouldBe($"https://www.google.com/search?q={encodedQuery}");
    }
}
