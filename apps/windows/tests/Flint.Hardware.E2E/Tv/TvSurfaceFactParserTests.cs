using Shouldly;
using Xunit;

namespace Flint.Hardware.E2E.Tests;

public sealed class TvSurfaceFactParserTests
{
    [Fact]
    public void Parse_ReadyPairingPanel_ReadsCodeEndpointBrowserPortAndFingerprint()
    {
        const string dump =
            """
            <?xml version='1.0' encoding='UTF-8'?>
            <hierarchy rotation="0">
              <node content-desc="Pairing code 5 0 1 3 3 7" text="" />
              <node text="10.221.159.172:47855" />
              <node text="BROWSER PORT" />
              <node content-desc="Browser port 33164" text="33164" />
              <node text="BA35-6AA0-4067" />
            </hierarchy>
            """;

        var facts = TvSurfaceFactParser.Parse(dump);

        facts.PairingCode.ShouldBe("501337");
        facts.Address.ShouldBe("10.221.159.172");
        facts.ReceiverPort.ShouldBe(47855);
        facts.BrowserPort.ShouldBe(33164);
        facts.BrowserFingerprint.ShouldBe("BA35-6AA0-4067");
    }

    [Fact]
    public void Parse_MissingBrowserPort_FallsBackToLogcatListenerLine()
    {
        const string dump =
            """
            <hierarchy>
              <node content-desc="Pairing code 0 0 4 2 8 3" />
              <node text="10.230.19.172:47855" />
            </hierarchy>
            """;
        const string logcat =
            "I/BrowserTlsServer: Browser TLS listening on 10.230.19.172:33164";

        var facts = TvSurfaceFactParser.Parse(dump, logcat);

        facts.PairingCode.ShouldBe("004283");
        facts.BrowserPort.ShouldBe(33164);
        facts.BrowserFingerprint.ShouldBeNull();
    }

    [Fact]
    public void Parse_MissingPairingCode_ThrowsActionableError()
    {
        var error = Should.Throw<InvalidOperationException>(() =>
            TvSurfaceFactParser.Parse("<hierarchy><node text=\"hello\" /></hierarchy>"));

        error.Message.ShouldContain("Pairing code");
        error.Message.ShouldContain("READY");
    }
}
