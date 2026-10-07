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

    [Fact]
    public void Parse_TheReadyScreenAndConnectionDetailsTogether_ReadsTheLabelledAddressAndPort()
    {
        const string ready = """
            <hierarchy>
              <node text="Ready to connect" />
              <node content-desc="Pairing code 1 7 8 4 8 9" />
              <node text="Connection details" />
            </hierarchy>
            """;
        const string details = """
            <hierarchy>
              <node text="TV address: 10.214.174.172:47855" />
              <node text="7F3A-1B8F-CAF3" />
              <node text="Browser port: 39087" />
            </hierarchy>
            """;

        TvSurfaceFactParser.HasEndpoint(ready).ShouldBeFalse();
        TvSurfaceFactParser.HasEndpoint(details).ShouldBeTrue();
        var facts = TvSurfaceFactParser.Parse(ready + details);

        facts.ShouldBe(new TvSurfaceFacts("178489", "10.214.174.172", 47855, 39087, "7F3A-1B8F-CAF3"));
    }
}
