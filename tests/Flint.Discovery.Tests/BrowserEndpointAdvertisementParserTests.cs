using System.Net;
using Flint.Discovery.Browser;
using Shouldly;

namespace Flint.Discovery.Tests;

/// <summary>
/// Browser endpoint TXT records arrive from an unauthenticated LAN peer. These tests keep the
/// routing parser deliberately stricter than ordinary device discovery: it must never turn an
/// ambiguous advertisement into a connection target.
/// </summary>
public sealed class BrowserEndpointAdvertisementParserTests
{
    private static readonly IPAddress SelectedReceiver = IPAddress.Parse("192.168.50.42");

    [Fact]
    public void TryResolve_CompleteAdvertisementForSelectedReceiver_ReturnsRoutingOnlyEndpoint()
    {
        // Arrange
        var attributes = new[]
        {
            "fn=Living Room",
            "browser_port=8443",
            "browser_protocol=2",
            "md=AFTMM",
        };

        // Act
        var resolved = BrowserEndpointAdvertisementParser.TryResolve(
            SelectedReceiver,
            SelectedReceiver,
            attributes,
            out var endpoint,
            out var error);

        // Assert
        resolved.ShouldBeTrue();
        error.ShouldBe(BrowserEndpointAdvertisementError.None);
        endpoint.ShouldNotBeNull();
        endpoint.ReceiverAddress.ShouldBe(SelectedReceiver);
        endpoint.Port.ShouldBe(8443);
        endpoint.ProtocolVersion.ShouldBe(2);
    }

    [Fact]
    public void TryResolve_KeyValueAttributes_SupportsAParserThatPreservesEveryTxtEntry()
    {
        // A caller with a decoded mDNS record may carry attributes as key-value pairs. This API
        // remains enumerable rather than dictionary-shaped so duplicate browser keys cannot be
        // hidden before this parser sees them.

        // Arrange
        var attributes = new[]
        {
            new KeyValuePair<string, string>("browser_port", "8443"),
            new KeyValuePair<string, string>("browser_protocol", "2"),
        };

        // Act
        var resolved = BrowserEndpointAdvertisementParser.TryResolve(
            SelectedReceiver,
            SelectedReceiver,
            attributes,
            out var endpoint,
            out var error);

        // Assert
        resolved.ShouldBeTrue();
        error.ShouldBe(BrowserEndpointAdvertisementError.None);
        endpoint.ShouldNotBeNull();
        endpoint.Port.ShouldBe(8443);
    }

    [Fact]
    public void TryParse_BareNonBrowserFlag_IsIgnoredWithoutWeakeningRequiredBrowserFields()
    {
        // DNS-SD permits flags such as "supports-video". They must not become browser state.

        // Act
        var parsed = BrowserEndpointAdvertisementParser.TryParse(
            SelectedReceiver,
            ["supports-video", "browser_port=8443", "browser_protocol=2"],
            out var endpoint,
            out var error);

        // Assert
        parsed.ShouldBeTrue();
        error.ShouldBe(BrowserEndpointAdvertisementError.None);
        endpoint.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("browser_protocol=2", BrowserEndpointAdvertisementError.MissingBrowserPort)]
    [InlineData("browser_port=8443", BrowserEndpointAdvertisementError.MissingBrowserProtocol)]
    public void TryParse_MissingRequiredAttribute_IsRejected(
        string onlyAttribute,
        BrowserEndpointAdvertisementError expectedError)
    {
        // Act
        var parsed = BrowserEndpointAdvertisementParser.TryParse(
            SelectedReceiver,
            [onlyAttribute],
            out var endpoint,
            out var error);

        // Assert
        parsed.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(expectedError);
    }

    [Theory]
    [InlineData("browser_port", BrowserEndpointAdvertisementError.MalformedBrowserAttribute)]
    [InlineData("browser_protocol", BrowserEndpointAdvertisementError.MalformedBrowserAttribute)]
    [InlineData("browser_pin", BrowserEndpointAdvertisementError.SensitiveBrowserAttribute)]
    public void TryParse_BareBrowserSpecificAttribute_IsRejected(
        string bareAttribute,
        BrowserEndpointAdvertisementError expectedError)
    {
        // Act
        var parsed = BrowserEndpointAdvertisementParser.TryParse(
            SelectedReceiver,
            [bareAttribute, "browser_port=8443", "browser_protocol=2"],
            out var endpoint,
            out var error);

        // Assert
        parsed.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(expectedError);
    }

    [Theory]
    [InlineData("browser_port=0")]
    [InlineData("browser_port=65536")]
    [InlineData("browser_port=-1")]
    [InlineData("browser_port=not-a-port")]
    [InlineData("browser_port= 8443")]
    [InlineData("browser_port=8443 ")]
    [InlineData("browser_port=+8443")]
    [InlineData("browser_port=8443.0")]
    public void TryParse_MalformedOrOutOfRangePort_IsRejected(string portAttribute)
    {
        // Act
        var parsed = BrowserEndpointAdvertisementParser.TryParse(
            SelectedReceiver,
            [portAttribute, "browser_protocol=2"],
            out var endpoint,
            out var error);

        // Assert
        parsed.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(BrowserEndpointAdvertisementError.InvalidBrowserPort);
    }

    [Theory]
    [InlineData("browser_protocol=0")]
    [InlineData("browser_protocol=1")]
    public void TryParse_BrowserProtocolOlderThanV2_IsRejected(string protocolAttribute)
    {
        // Act
        var parsed = BrowserEndpointAdvertisementParser.TryParse(
            SelectedReceiver,
            ["browser_port=8443", protocolAttribute],
            out var endpoint,
            out var error);

        // Assert
        parsed.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(BrowserEndpointAdvertisementError.UnsupportedBrowserProtocol);
    }

    [Theory]
    [InlineData("browser_protocol=not-a-version")]
    [InlineData("browser_protocol=65536")]
    [InlineData("browser_protocol=2.0")]
    [InlineData("browser_protocol= 2")]
    public void TryParse_MalformedOrOutOfRangeProtocol_IsRejected(string protocolAttribute)
    {
        // Act
        var parsed = BrowserEndpointAdvertisementParser.TryParse(
            SelectedReceiver,
            ["browser_port=8443", protocolAttribute],
            out var endpoint,
            out var error);

        // Assert
        parsed.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(BrowserEndpointAdvertisementError.InvalidBrowserProtocol);
    }

    [Theory]
    [InlineData("browser_port=8443", "browser_port=8443")]
    [InlineData("browser_protocol=2", "BROWSER_PROTOCOL=2")]
    public void TryParse_DuplicateBrowserAttribute_IsRejected(string first, string duplicate)
    {
        // Arrange
        var attributes = first.StartsWith("browser_port", StringComparison.OrdinalIgnoreCase)
            ? new[] { first, duplicate, "browser_protocol=2" }
            : new[] { "browser_port=8443", first, duplicate };

        // Act
        var parsed = BrowserEndpointAdvertisementParser.TryParse(
            SelectedReceiver,
            attributes,
            out var endpoint,
            out var error);

        // Assert
        parsed.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(BrowserEndpointAdvertisementError.DuplicateBrowserAttribute);
    }

    [Theory]
    [InlineData("browser_port=8443", "browser_port=9443")]
    [InlineData("browser_protocol=2", "browser_protocol=3")]
    public void TryParse_ConflictingBrowserAttribute_IsRejected(string first, string conflict)
    {
        // Arrange
        var attributes = first.StartsWith("browser_port", StringComparison.OrdinalIgnoreCase)
            ? new[] { first, conflict, "browser_protocol=2" }
            : new[] { "browser_port=8443", first, conflict };

        // Act
        var parsed = BrowserEndpointAdvertisementParser.TryParse(
            SelectedReceiver,
            attributes,
            out var endpoint,
            out var error);

        // Assert
        parsed.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(BrowserEndpointAdvertisementError.ConflictingBrowserAttribute);
    }

    [Theory]
    [InlineData("browser_port=")]
    [InlineData("browser_port=   ")]
    [InlineData("browser_protocol=")]
    [InlineData("browser_protocol=   ")]
    public void TryParse_BlankBrowserAttributeValue_IsRejected(string blankAttribute)
    {
        // Arrange
        var attributes = blankAttribute.StartsWith("browser_port", StringComparison.Ordinal)
            ? new[] { blankAttribute, "browser_protocol=2" }
            : new[] { "browser_port=8443", blankAttribute };

        // Act
        var parsed = BrowserEndpointAdvertisementParser.TryParse(
            SelectedReceiver,
            attributes,
            out var endpoint,
            out var error);

        // Assert
        parsed.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(BrowserEndpointAdvertisementError.BlankBrowserAttributeValue);
    }

    [Fact]
    public void TryParse_OversizedBrowserAttributeValue_IsRejectedBeforeNumericParsing()
    {
        // Arrange
        var oversizedValue = new string('2', BrowserEndpointAdvertisementParser.MaximumBrowserValueBytes + 1);

        // Act
        var parsed = BrowserEndpointAdvertisementParser.TryParse(
            SelectedReceiver,
            ["browser_port=8443", $"browser_protocol={oversizedValue}"],
            out var endpoint,
            out var error);

        // Assert
        parsed.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(BrowserEndpointAdvertisementError.OversizedBrowserAttributeValue);
    }

    [Fact]
    public void TryParse_OversizedNonBrowserTextAttribute_IsRejectedBeforeItCanBeIgnored()
    {
        // A non-browser key is not a reason to accept an arbitrarily large input from the LAN.

        // Arrange
        var oversized = new string('x', BrowserEndpointAdvertisementParser.MaximumTextAttributeBytes + 1);

        // Act
        var parsed = BrowserEndpointAdvertisementParser.TryParse(
            SelectedReceiver,
            [oversized, "browser_port=8443", "browser_protocol=2"],
            out var endpoint,
            out var error);

        // Assert
        parsed.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(BrowserEndpointAdvertisementError.OversizedTextAttribute);
    }

    [Fact]
    public void TryParse_EmptyTextAttribute_IsRejected()
    {
        // Act
        var parsed = BrowserEndpointAdvertisementParser.TryParse(
            SelectedReceiver,
            [string.Empty],
            out var endpoint,
            out var error);

        // Assert
        parsed.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(BrowserEndpointAdvertisementError.MalformedTextAttribute);
    }

    [Fact]
    public void TryParse_NullTextAttribute_IsRejected()
    {
        // Act
        var parsed = BrowserEndpointAdvertisementParser.TryParse(
            SelectedReceiver,
            new string[] { null! },
            out var endpoint,
            out var error);

        // Assert
        parsed.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(BrowserEndpointAdvertisementError.MalformedTextAttribute);
    }

    [Fact]
    public void TryParse_InvalidUtf16TextAttribute_IsRejectedWithoutReplacingCharacters()
    {
        // A strict UTF-8 boundary must not silently repair a malformed managed input either.

        // Act
        var parsed = BrowserEndpointAdvertisementParser.TryParse(
            SelectedReceiver,
            [new string('\ud800', 1)],
            out var endpoint,
            out var error);

        // Assert
        parsed.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(BrowserEndpointAdvertisementError.MalformedTextAttribute);
    }

    [Fact]
    public void TryParse_AttributeWithoutAKey_IsRejected()
    {
        // Act
        var parsed = BrowserEndpointAdvertisementParser.TryParse(
            SelectedReceiver,
            ["=value", "browser_port=8443", "browser_protocol=2"],
            out var endpoint,
            out var error);

        // Assert
        parsed.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(BrowserEndpointAdvertisementError.MalformedTextAttribute);
    }

    [Fact]
    public void TryParse_TooManyTxtAttributes_IsRejectedWithoutEnumeratingUnboundedInput()
    {
        // Arrange
        var attributes = Enumerable
            .Repeat("fn=Living Room", BrowserEndpointAdvertisementParser.MaximumTextAttributeCount + 1)
            .Append("browser_port=8443")
            .Append("browser_protocol=2");

        // Act
        var parsed = BrowserEndpointAdvertisementParser.TryParse(
            SelectedReceiver,
            attributes,
            out var endpoint,
            out var error);

        // Assert
        parsed.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(BrowserEndpointAdvertisementError.TooManyTextAttributes);
    }

    [Fact]
    public void TryParse_TooManyKeyValueAttributes_IsRejectedBeforeInspectingTheList()
    {
        // Arrange
        IReadOnlyList<KeyValuePair<string, string>> attributes = Enumerable
            .Repeat(new KeyValuePair<string, string>("fn", "Living Room"),
                BrowserEndpointAdvertisementParser.MaximumTextAttributeCount + 1)
            .ToArray();

        // Act
        var parsed = BrowserEndpointAdvertisementParser.TryParse(
            SelectedReceiver,
            attributes,
            out var endpoint,
            out var error);

        // Assert
        parsed.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(BrowserEndpointAdvertisementError.TooManyTextAttributes);
    }

    [Theory]
    [InlineData("", "value")]
    [InlineData("browser_port", "")]
    public void TryParse_MalformedKeyValueAttribute_IsRejected(string key, string value)
    {
        // Act
        var parsed = BrowserEndpointAdvertisementParser.TryParse(
            SelectedReceiver,
            new[] { new KeyValuePair<string, string>(key, value) },
            out var endpoint,
            out var error);

        // Assert
        parsed.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(key.Length == 0
            ? BrowserEndpointAdvertisementError.MalformedTextAttribute
            : BrowserEndpointAdvertisementError.BlankBrowserAttributeValue);
    }

    [Fact]
    public void TryParse_NullKeyValueAttributeValue_IsRejected()
    {
        // Act
        var parsed = BrowserEndpointAdvertisementParser.TryParse(
            SelectedReceiver,
            new[] { new KeyValuePair<string, string>("browser_port", null!) },
            out var endpoint,
            out var error);

        // Assert
        parsed.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(BrowserEndpointAdvertisementError.MalformedTextAttribute);
    }

    [Fact]
    public void TryParse_OversizedKeyValueAttribute_IsRejected()
    {
        // Act
        var parsed = BrowserEndpointAdvertisementParser.TryParse(
            SelectedReceiver,
            new[]
            {
                new KeyValuePair<string, string>(new string('x', BrowserEndpointAdvertisementParser.MaximumTextAttributeBytes), "y"),
            },
            out var endpoint,
            out var error);

        // Assert
        parsed.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(BrowserEndpointAdvertisementError.OversizedTextAttribute);
    }

    [Fact]
    public void TryParse_KeyValueAttributesRejectInvalidSourceAddress()
    {
        // Act
        var parsed = BrowserEndpointAdvertisementParser.TryParse(
            IPAddress.Any,
            new[]
            {
                new KeyValuePair<string, string>("browser_port", "8443"),
                new KeyValuePair<string, string>("browser_protocol", "2"),
            },
            out var endpoint,
            out var error);

        // Assert
        parsed.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(BrowserEndpointAdvertisementError.InvalidSourceAddress);
    }

    [Fact]
    public void TryParse_NullKeyValueSourceAddress_IsRejected()
    {
        // Act
        var parsed = BrowserEndpointAdvertisementParser.TryParse(
            null,
            new[]
            {
                new KeyValuePair<string, string>("browser_port", "8443"),
                new KeyValuePair<string, string>("browser_protocol", "2"),
            },
            out var endpoint,
            out var error);

        // Assert
        parsed.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(BrowserEndpointAdvertisementError.InvalidSourceAddress);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("127.0.0.1")]
    [InlineData("224.0.0.251")]
    [InlineData("255.255.255.255")]
    public void TryParse_NonUnicastIpv4SourceAddress_IsRejected(string source)
    {
        // Act
        var parsed = BrowserEndpointAdvertisementParser.TryParse(
            IPAddress.Parse(source),
            ["browser_port=8443", "browser_protocol=2"],
            out var endpoint,
            out var error);

        // Assert
        parsed.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(BrowserEndpointAdvertisementError.InvalidSourceAddress);
    }

    [Fact]
    public void TryParse_Ipv6SourceAddress_IsRejectedBecauseTheReceiverAdvertisesIpv4Endpoints()
    {
        // Act
        var parsed = BrowserEndpointAdvertisementParser.TryParse(
            IPAddress.IPv6Loopback,
            ["browser_port=8443", "browser_protocol=2"],
            out var endpoint,
            out var error);

        // Assert
        parsed.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(BrowserEndpointAdvertisementError.InvalidSourceAddress);
    }

    [Fact]
    public void TryParse_NullRawSourceAddress_IsRejected()
    {
        // Act
        var parsed = BrowserEndpointAdvertisementParser.TryParse(
            null,
            ["browser_port=8443", "browser_protocol=2"],
            out var endpoint,
            out var error);

        // Assert
        parsed.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(BrowserEndpointAdvertisementError.InvalidSourceAddress);
    }

    [Fact]
    public void TryResolve_AdvertisementForAnotherReceiver_IsRejected()
    {
        // Act
        var resolved = BrowserEndpointAdvertisementParser.TryResolve(
            SelectedReceiver,
            IPAddress.Parse("192.168.50.43"),
            ["browser_port=8443", "browser_protocol=2"],
            out var endpoint,
            out var error);

        // Assert
        resolved.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(BrowserEndpointAdvertisementError.SelectedReceiverAddressMismatch);
    }

    [Theory]
    [InlineData("0.0.0.0", BrowserEndpointAdvertisementError.InvalidSelectedReceiverAddress)]
    [InlineData("127.0.0.1", BrowserEndpointAdvertisementError.InvalidSelectedReceiverAddress)]
    public void TryResolve_InvalidSelectedReceiver_IsRejectedBeforeTxtParsing(
        string selectedAddress,
        BrowserEndpointAdvertisementError expectedError)
    {
        // Act
        var resolved = BrowserEndpointAdvertisementParser.TryResolve(
            IPAddress.Parse(selectedAddress),
            SelectedReceiver,
            ["browser_port=8443", "browser_protocol=2"],
            out var endpoint,
            out var error);

        // Assert
        resolved.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(expectedError);
    }

    [Fact]
    public void TryResolve_InvalidAdvertisedSource_IsRejectedBeforeTxtParsing()
    {
        // Act
        var resolved = BrowserEndpointAdvertisementParser.TryResolve(
            SelectedReceiver,
            IPAddress.Any,
            ["browser_port=8443", "browser_protocol=2"],
            out var endpoint,
            out var error);

        // Assert
        resolved.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(BrowserEndpointAdvertisementError.InvalidSourceAddress);
    }

    [Fact]
    public void TryResolve_NullSelectedReceiver_IsRejected()
    {
        // Act
        var resolved = BrowserEndpointAdvertisementParser.TryResolve(
            null,
            SelectedReceiver,
            ["browser_port=8443", "browser_protocol=2"],
            out var endpoint,
            out var error);

        // Assert
        resolved.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(BrowserEndpointAdvertisementError.InvalidSelectedReceiverAddress);
    }

    [Fact]
    public void TryResolve_NullAdvertisedSource_IsRejected()
    {
        // Act
        var resolved = BrowserEndpointAdvertisementParser.TryResolve(
            SelectedReceiver,
            null,
            ["browser_port=8443", "browser_protocol=2"],
            out var endpoint,
            out var error);

        // Assert
        resolved.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(BrowserEndpointAdvertisementError.InvalidSourceAddress);
    }

    [Fact]
    public void TryResolve_KeyValueAdvertisementForAnotherReceiver_IsRejected()
    {
        // Act
        var resolved = BrowserEndpointAdvertisementParser.TryResolve(
            SelectedReceiver,
            IPAddress.Parse("192.168.50.43"),
            new[]
            {
                new KeyValuePair<string, string>("browser_port", "8443"),
                new KeyValuePair<string, string>("browser_protocol", "2"),
            },
            out var endpoint,
            out var error);

        // Assert
        resolved.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(BrowserEndpointAdvertisementError.SelectedReceiverAddressMismatch);
    }

    [Theory]
    [InlineData("browser_pin=deadbeef")]
    [InlineData("browser_certificate=certificate-bytes")]
    [InlineData("browser_pairing_code=123456")]
    [InlineData("browser_session_token=token")]
    public void TryParse_SensitiveBrowserMetadata_IsRejectedRatherThanCarriedForward(string sensitiveAttribute)
    {
        // Act
        var parsed = BrowserEndpointAdvertisementParser.TryParse(
            SelectedReceiver,
            ["browser_port=8443", "browser_protocol=2", sensitiveAttribute],
            out var endpoint,
            out var error);

        // Assert
        parsed.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(BrowserEndpointAdvertisementError.SensitiveBrowserAttribute);
    }

    [Fact]
    public void Advertisement_ContainsRoutingFactsOnly()
    {
        // Discovery may select a route, but it never establishes receiver identity or carries
        // authorization material. Keep the model narrow so a future secret field is caught here.

        // Act
        var propertyNames = typeof(BrowserEndpointAdvertisement)
            .GetProperties()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Assert
        propertyNames.ShouldBe(["Port", "ProtocolVersion", "ReceiverAddress"]);
    }

    [Fact]
    public void Advertisement_ConstructorRejectsNonUnicastAddress()
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() =>
            new BrowserEndpointAdvertisement(IPAddress.Loopback, 8443, 2));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(65536, 2)]
    [InlineData(8443, 1)]
    [InlineData(8443, 65536)]
    public void Advertisement_ConstructorRejectsOutOfRangeRoutingValues(int port, int protocolVersion)
    {
        // Act & Assert
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new BrowserEndpointAdvertisement(SelectedReceiver, port, protocolVersion));
    }

    [Fact]
    public void Advertisement_ConstructorRejectsNullAddress()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() =>
            new BrowserEndpointAdvertisement(null!, 8443, 2));
    }

    [Fact]
    public void Advertisement_AddressValidatorRejectsNull()
    {
        BrowserEndpointAdvertisement.IsUsableUnicastIpv4(null).ShouldBeFalse();
    }
}
