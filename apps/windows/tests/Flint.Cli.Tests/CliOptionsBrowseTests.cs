using Shouldly;

namespace Flint.Cli.Tests;

/// <summary>
/// The <c>--browse</c> option, which drives a Fire TV-resident browser from the command line.
/// </summary>
/// <remarks>
/// It exists mostly so the browser feature can be exercised end to end without driving the desktop
/// UI by hand — a scripted run against a real television is worth more than any number of fakes,
/// and it needs something to run.
/// </remarks>
public sealed class CliOptionsBrowseTests
{
    [Fact]
    public void Browse_WithAddressAndPairingCode_Parses()
    {
        var parsed = CliOptions.TryParse(
            ["--address", "10.46.161.42", "--pairing-code", "123456", "--browse", "https://example.test/"],
            out var options,
            out var error);

        parsed.ShouldBeTrue(error);
        options.ShouldNotBeNull();
        options.BrowseUrl.ShouldBe("https://example.test/");
    }

    [Fact]
    public void Browse_WithoutPairingCode_IsRefused()
    {
        // The browser session is separately authenticated; without the code there is nothing to
        // verify the television against.
        var parsed = CliOptions.TryParse(
            ["--address", "10.46.161.42", "--browse", "https://example.test/"],
            out _,
            out var error);

        parsed.ShouldBeFalse();
        error.ShouldNotBeNull();
        error.ShouldContain("--pairing-code");
    }

    [Fact]
    public void Browse_WithAPlainHttpAddress_IsRefused()
    {
        // Refused here rather than at the receiver, so the message lands next to the typo.
        var parsed = CliOptions.TryParse(
            ["--address", "10.46.161.42", "--pairing-code", "123456", "--browse", "http://example.test/"],
            out _,
            out var error);

        parsed.ShouldBeFalse();
        error.ShouldNotBeNull();
        error.ShouldContain("https://");
    }

    [Fact]
    public void Browse_CombinedWithMirror_IsRefused()
    {
        // Both own the television's screen; running them together leaves whichever finished second
        // showing over the other with no way to tell which won.
        var parsed = CliOptions.TryParse(
            ["--address", "10.46.161.42", "--pairing-code", "123456", "--browse", "https://example.test/", "--mirror"],
            out _,
            out var error);

        parsed.ShouldBeFalse();
        error.ShouldNotBeNull();
        error.ShouldContain("--mirror");
    }

    [Fact]
    public void Browse_TwiceOver_IsRefused()
    {
        var parsed = CliOptions.TryParse(
            [
                "--address", "10.46.161.42", "--pairing-code", "123456",
                "--browse", "https://a.test/", "--browse", "https://b.test/",
            ],
            out _,
            out var error);

        parsed.ShouldBeFalse();
        error.ShouldNotBeNull();
        error.ShouldContain("only once");
    }

    [Fact]
    public void Browse_WithNoAddressAfterIt_IsRefused()
    {
        var parsed = CliOptions.TryParse(
            ["--address", "10.46.161.42", "--pairing-code", "123456", "--browse"],
            out _,
            out var error);

        parsed.ShouldBeFalse();
        error.ShouldNotBeNull();
    }

    [Fact]
    public void WithoutBrowse_TheUrlIsAbsent()
    {
        var parsed = CliOptions.TryParse(
            ["--address", "10.46.161.42", "--pairing-code", "123456", "--mirror"],
            out var options,
            out var error);

        parsed.ShouldBeTrue(error);
        options.ShouldNotBeNull();
        options.BrowseUrl.ShouldBeNull();
    }
}
