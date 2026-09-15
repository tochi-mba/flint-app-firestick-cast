using System.Text;
using Flint.Discovery.Browser;
using Shouldly;

namespace Flint.Discovery.Tests;

/// <summary>
/// The mDNS codec reads packets from unauthenticated peers on the local network, so its failure
/// modes matter as much as its happy path.
/// </summary>
public sealed class MulticastDnsCodecTests
{
    private const string ServiceType = MulticastDnsCodec.FireTvServiceType;
    private const string Instance = "Living Room._amzn-wplay._tcp.local";
    private const string Host = "living-room.local";

    [Fact]
    public void BuildQuery_ProducesAWellFormedPointerQuestion()
    {
        // Act
        var query = MulticastDnsCodec.BuildQuery(ServiceType);

        // Assert
        query.Length.ShouldBeGreaterThan(12);
        query[0].ShouldBe((byte)0);           // Transaction id must be zero for mDNS.
        query[1].ShouldBe((byte)0);
        query[5].ShouldBe((byte)1);           // Exactly one question.
        query[^3].ShouldBe((byte)DnsRecordType.Ptr);
    }

    [Fact]
    public void BuildQuery_BlankServiceType_Throws()
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() => MulticastDnsCodec.BuildQuery("  "));
    }

    [Fact]
    public void ReadInstances_CompleteAnswer_ResolvesTheDevice()
    {
        // Arrange
        var packet = new DnsPacketBuilder()
            .AddPointer(ServiceType, Instance)
            .AddService(Instance, Host, 8009)
            .AddAddress(Host, "192.168.1.42")
            .Build();

        // Act
        var instances = MulticastDnsCodec.ReadInstances(packet, ServiceType);

        // Assert
        var instance = instances.ShouldHaveSingleItem();
        instance.InstanceName.ShouldBe("Living Room");
        instance.Address.ToString().ShouldBe("192.168.1.42");
        instance.Port.ShouldBe(8009);
    }

    [Fact]
    public void ReadInstances_TextRecord_ParsesAttributes()
    {
        // Arrange
        var packet = new DnsPacketBuilder()
            .AddPointer(ServiceType, Instance)
            .AddService(Instance, Host, 8009)
            .AddAddress(Host, "192.168.1.42")
            .AddText(Instance, "fn=Front Room TV", "md=AFTKA", "flag")
            .Build();

        // Act
        var instance = MulticastDnsCodec.ReadInstances(packet, ServiceType).ShouldHaveSingleItem();

        // Assert
        instance.Attribute("fn").ShouldBe("Front Room TV");
        instance.Attribute("md").ShouldBe("AFTKA");
        instance.Attribute("flag").ShouldBeNull();   // A bare flag has no value to report.
        instance.Attribute("absent").ShouldBeNull();
        instance.RawTextAttributes.ShouldBe(["fn=Front Room TV", "md=AFTKA", "flag"]);
        instance.HasMalformedTextAttributes.ShouldBeFalse();
    }

    [Fact]
    public void ReadInstances_TextRecords_PreserveRawOrderSoBrowserDuplicatesCannotBeHidden()
    {
        // The legacy dictionary retains its last value, but browser endpoint validation needs the
        // original sequence to distinguish an ordinary update from a conflicting announcement.

        // Arrange
        var packet = new DnsPacketBuilder()
            .AddPointer(ServiceType, Instance)
            .AddService(Instance, Host, 8009)
            .AddAddress(Host, "192.168.1.42")
            .AddText(Instance, "browser_port=8443")
            .AddText(Instance, "browser_protocol=2", "browser_port=9443")
            .Build();

        // Act
        var instance = MulticastDnsCodec.ReadInstances(packet, ServiceType).ShouldHaveSingleItem();
        var resolved = instance.TryResolveBrowserEndpoint(instance.Address, out var endpoint, out var error);

        // Assert
        instance.Attribute("browser_port").ShouldBe("9443");
        instance.RawTextAttributes.ShouldBe(
            ["browser_port=8443", "browser_protocol=2", "browser_port=9443"]);
        instance.HasMalformedTextAttributes.ShouldBeFalse();
        resolved.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(BrowserEndpointAdvertisementError.ConflictingBrowserAttribute);
    }

    [Theory]
    [InlineData((byte)3, (byte)'a')]
    [InlineData((byte)2, (byte)0xC3, (byte)0x28)]
    public void ReadInstances_MalformedTxt_IsMarkedAndCannotResolveBrowserEndpoint(params byte[] textData)
    {
        // The outer DNS record remains structurally valid. TXT content is deliberately surfaced
        // as evidence rather than making discovery discard an otherwise visible receiver.

        // Arrange
        var packet = new DnsPacketBuilder()
            .AddPointer(ServiceType, Instance)
            .AddService(Instance, Host, 8009)
            .AddAddress(Host, "192.168.1.42")
            .AddRawText(Instance, textData)
            .Build();

        // Act
        var instance = MulticastDnsCodec.ReadInstances(packet, ServiceType).ShouldHaveSingleItem();
        var resolved = instance.TryResolveBrowserEndpoint(instance.Address, out var endpoint, out var error);

        // Assert
        instance.HasMalformedTextAttributes.ShouldBeTrue();
        resolved.ShouldBeFalse();
        endpoint.ShouldBeNull();
        error.ShouldBe(BrowserEndpointAdvertisementError.MalformedTextAttribute);
    }

    [Fact]
    public void ReadInstances_PointerWithoutAddress_IsDroppedRatherThanHalfReported()
    {
        // A device Flint cannot connect to is not a device Flint found.

        // Arrange
        var packet = new DnsPacketBuilder()
            .AddPointer(ServiceType, Instance)
            .AddService(Instance, Host, 8009)
            .Build();

        // Act
        var instances = MulticastDnsCodec.ReadInstances(packet, ServiceType);

        // Assert
        instances.ShouldBeEmpty();
    }

    [Fact]
    public void ReadInstances_PointerWithoutService_IsDropped()
    {
        // Arrange
        var packet = new DnsPacketBuilder()
            .AddPointer(ServiceType, Instance)
            .AddAddress(Host, "192.168.1.42")
            .Build();

        // Act
        MulticastDnsCodec.ReadInstances(packet, ServiceType).ShouldBeEmpty();
    }

    [Fact]
    public void ReadInstances_DifferentServiceType_IsIgnored()
    {
        // A home network advertises printers, speakers and thermostats on the same group.

        // Arrange
        var packet = new DnsPacketBuilder()
            .AddPointer("_ipp._tcp.local", "Printer._ipp._tcp.local")
            .AddService("Printer._ipp._tcp.local", "printer.local", 631)
            .AddAddress("printer.local", "192.168.1.9")
            .Build();

        // Act
        MulticastDnsCodec.ReadInstances(packet, ServiceType).ShouldBeEmpty();
    }

    [Fact]
    public void ReadInstances_QueryRatherThanResponse_ReturnsNothing()
    {
        // Flint sees its own queries and other hosts' queries on the group.

        // Arrange
        var query = MulticastDnsCodec.BuildQuery(ServiceType);

        // Act
        MulticastDnsCodec.ReadInstances(query, ServiceType).ShouldBeEmpty();
    }

    [Fact]
    public void ReadInstances_DuplicateAnswers_ReportTheDeviceOnce()
    {
        // Responders repeat announcements; the same television must not appear twice.

        // Arrange
        var packet = new DnsPacketBuilder()
            .AddPointer(ServiceType, Instance)
            .AddPointer(ServiceType, Instance)
            .AddService(Instance, Host, 8009)
            .AddAddress(Host, "192.168.1.42")
            .Build();

        // Act
        MulticastDnsCodec.ReadInstances(packet, ServiceType).Count.ShouldBe(1);
    }

    [Fact]
    public void ReadInstances_PacketShorterThanAHeader_Throws()
    {
        // Act & Assert
        Should.Throw<DnsFormatException>(
            () => MulticastDnsCodec.ReadInstances(new byte[5], ServiceType));
    }

    [Fact]
    public void ReadInstances_TruncatedRecordData_Throws()
    {
        // Arrange
        var packet = new DnsPacketBuilder()
            .AddPointer(ServiceType, Instance)
            .Build();
        var truncated = packet[..^4];

        // Act & Assert
        Should.Throw<DnsFormatException>(
            () => MulticastDnsCodec.ReadInstances(truncated, ServiceType));
    }

    [Fact]
    public void ReadInstances_MoreAnswersClaimedThanPresent_StopsAtTheEndOfTheBuffer()
    {
        // A lying header must not walk the parser off the end of the packet.

        // Arrange
        var packet = new DnsPacketBuilder()
            .AddPointer(ServiceType, Instance)
            .AddService(Instance, Host, 8009)
            .AddAddress(Host, "192.168.1.42")
            .BuildWithAnswerCount(claimed: 50);

        // Act
        var instances = MulticastDnsCodec.ReadInstances(packet, ServiceType);

        // Assert
        instances.ShouldHaveSingleItem();
    }

    [Fact]
    public void ReadPointerTargets_ServiceEnumeration_ListsTypesWithoutNeedingAddresses()
    {
        // Arrange
        var packet = new DnsPacketBuilder()
            .AddPointer(MulticastDnsCodec.ServiceEnumerationType, ServiceType)
            .AddPointer(MulticastDnsCodec.ServiceEnumerationType, "_ipp._tcp.local")
            .Build();

        // Act
        var targets = MulticastDnsCodec.ReadPointerTargets(packet);

        // Assert
        targets.ShouldBe([ServiceType, "_ipp._tcp.local"]);
    }
}

/// <summary>
/// Name reading is the part of DNS parsing that an adversary on the local link can attack, so its
/// bounds are tested directly rather than only through the codec.
/// </summary>
public sealed class DnsNameTests
{
    [Fact]
    public void Read_SimpleName_ReturnsDottedForm()
    {
        // Arrange
        var bytes = new List<byte>();
        DnsName.Write(bytes, "living-room.local");
        var offset = 0;

        // Act
        var name = DnsName.Read(bytes.ToArray(), ref offset);

        // Assert
        name.ShouldBe("living-room.local");
        offset.ShouldBe(bytes.Count);
    }

    [Fact]
    public void Read_CompressionPointer_FollowsItAndResumesAfterThePointer()
    {
        // "b.local" written out, then a name that is one label plus a pointer back to "local".

        // Arrange
        var packet = new List<byte>();
        packet.AddRange([1, (byte)'b', 5, (byte)'l', (byte)'o', (byte)'c', (byte)'a', (byte)'l', 0]);
        var localOffset = 2;
        var start = packet.Count;
        packet.AddRange([1, (byte)'a']);
        packet.AddRange([(byte)(0xC0 | (localOffset >> 8)), (byte)localOffset]);

        var offset = start;

        // Act
        var name = DnsName.Read(packet.ToArray(), ref offset);

        // Assert
        name.ShouldBe("a.local");
        offset.ShouldBe(packet.Count);
    }

    [Fact]
    public void Read_PointerToItself_ThrowsRatherThanLooping()
    {
        // The classic decompression bomb. A forward or self pointer must be rejected outright.

        // Arrange
        var packet = new byte[] { 0xC0, 0x00 };
        var offset = 0;

        // Act & Assert
        Should.Throw<DnsFormatException>(() => DnsName.Read(packet, ref offset));
    }

    [Fact]
    public void Read_ForwardPointer_Throws()
    {
        // Arrange
        var packet = new byte[] { 0x00, 0x00, 0xC0, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00 };
        var offset = 2;

        // Act & Assert
        Should.Throw<DnsFormatException>(() => DnsName.Read(packet, ref offset));
    }

    [Fact]
    public void Read_LabelRunningPastTheBuffer_Throws()
    {
        // Arrange
        var packet = new byte[] { 40, (byte)'a', (byte)'b' };
        var offset = 0;

        // Act & Assert
        Should.Throw<DnsFormatException>(() => DnsName.Read(packet, ref offset));
    }

    [Fact]
    public void Read_TruncatedPointer_Throws()
    {
        // Arrange
        var packet = new byte[] { 0xC0 };
        var offset = 0;

        // Act & Assert
        Should.Throw<DnsFormatException>(() => DnsName.Read(packet, ref offset));
    }

    [Fact]
    public void Write_OverlongLabel_Throws()
    {
        // Arrange
        var label = new string('a', DnsName.MaxLabelLength + 1);

        // Act & Assert
        Should.Throw<DnsFormatException>(() => DnsName.Write(new List<byte>(), label));
    }

    [Theory]
    [InlineData("local", "local.", true)]
    [InlineData("LOCAL", "local", true)]
    [InlineData("a.local", "b.local", false)]
    public void Equal_IgnoresCaseAndTrailingDot(string left, string right, bool expected)
    {
        // Act & Assert
        DnsName.Equal(left, right).ShouldBe(expected);
    }

    [Fact]
    public void Write_ThenRead_RoundTripsUnicodeLabels()
    {
        // Device names are user-supplied and routinely contain emoji.

        // Arrange
        var bytes = new List<byte>();
        DnsName.Write(bytes, "Front Room 📺.local");
        var offset = 0;

        // Act
        var name = DnsName.Read(bytes.ToArray(), ref offset);

        // Assert
        name.ShouldBe("Front Room 📺.local");
    }
}
