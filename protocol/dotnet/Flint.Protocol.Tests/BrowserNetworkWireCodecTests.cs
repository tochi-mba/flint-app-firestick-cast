using System.Buffers.Binary;
using Shouldly;

namespace Flint.Protocol.Tests;

/// <summary>Contract tests for browser network/VPN messages 30 and 31.</summary>
public sealed class BrowserNetworkWireCodecTests
{
    private const string SampleWireGuardConfig =
        "[Interface]\nPrivateKey = AAA=\n\n[Peer]\nPublicKey = BBB=\n";

    [Theory]
    [MemberData(nameof(ValidCommands))]
    public void EveryNetworkCommandAction_RoundTrips(BrowserNetworkCommandMessage message)
    {
        var frame = new WireFrame(message);

        WireCodec.Decode(WireCodec.Encode(frame)).ShouldBe(frame);
    }

    [Theory]
    [MemberData(nameof(ValidStates))]
    public void EveryNetworkSessionState_RoundTrips(BrowserNetworkStateMessage message)
    {
        var frame = new WireFrame(message);

        WireCodec.Decode(WireCodec.Encode(frame)).ShouldBe(frame);
    }

    [Fact]
    public void NetworkMessages_AreAdditiveV2TlsBrowserTypes()
    {
        ((int)WireMessageType.BrowserNetworkCommand).ShouldBe(30);
        ((int)WireMessageType.BrowserNetworkState).ShouldBe(31);

        WireMessage[] messages =
        [
            new BrowserNetworkCommandMessage(3, 1, BrowserNetworkAction.RequestSnapshot),
            new BrowserNetworkStateMessage(
                3,
                1,
                "family",
                false,
                BrowserVpnProvider.None,
                false,
                false,
                false,
                false,
                "probe",
                BrowserVpnSessionState.Idle),
        ];
        foreach (var message in messages)
        {
            BrowserWireRules.IsBrowserMessage(message).ShouldBeTrue();
            BrowserWireRules.IsBrowserType(message.TypeId).ShouldBeTrue();
            BrowserWireRules.IsForbiddenOnOrdinaryChannel(message).ShouldBeTrue();
            Should.Throw<WireFormatException>(() => WireCodec.Encode(new WireFrame(1, message)))
                .Message.ShouldContain("version 2");
        }

        BrowserWireRules.IsBrowserType(30).ShouldBeTrue();
        BrowserWireRules.IsBrowserType(31).ShouldBeTrue();
        BrowserWireRules.IsBrowserType(32).ShouldBeTrue();
        BrowserWireRules.IsBrowserType(37).ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(InvalidCommands))]
    public void InvalidNetworkCommands_AreRejected(BrowserNetworkCommandMessage message)
    {
        Should.Throw<WireFormatException>(() => WireCodec.Encode(new WireFrame(message)));
    }

    [Theory]
    [MemberData(nameof(InvalidStates))]
    public void InvalidNetworkStates_AreRejected(BrowserNetworkStateMessage message)
    {
        Should.Throw<WireFormatException>(() => WireCodec.Encode(new WireFrame(message)));
    }

    [Fact]
    public void ConfigText_UsesUnsigned32BitLengthPrefix()
    {
        var encoded = WireCodec.Encode(new WireFrame(new BrowserNetworkCommandMessage(
            3,
            1,
            BrowserNetworkAction.Set,
            "family",
            true,
            BrowserVpnProvider.WireGuard,
            true,
            false,
            SampleWireGuardConfig)));
        var payload = encoded.AsSpan(12);
        var offset = 8 + 8 + 1;
        var profileLength = BinaryPrimitives.ReadUInt16BigEndian(payload[offset..]);
        offset += 2 + profileLength;
        offset += 4;
        var configLength = BinaryPrimitives.ReadUInt32BigEndian(payload[offset..]);
        configLength.ShouldBe((uint)System.Text.Encoding.UTF8.GetByteCount(SampleWireGuardConfig));
    }

    [Fact]
    public void NetworkDecoders_RejectUnknownEnumsAndTrailingBytes()
    {
        var command = WireCodec.Encode(new WireFrame(
            new BrowserNetworkCommandMessage(3, 1, BrowserNetworkAction.RequestSnapshot)));
        command[12 + 16] = 0;
        Should.Throw<WireFormatException>(() => WireCodec.Decode(command)).Message.ShouldContain("Unknown");

        var state = WireCodec.Encode(new WireFrame(ValidIdleState()));
        // session state sits after several fixed fields; corrupt the final enum byte.
        state[^3] = 99;
        Should.Throw<WireFormatException>(() => WireCodec.Decode(state)).Message.ShouldContain("Unknown");

        var encoded = WireCodec.Encode(new WireFrame(ValidIdleState()));
        var trailing = encoded.Append((byte)0).ToArray();
        BinaryPrimitives.WriteInt32BigEndian(trailing, encoded.Length - 3);
        Should.Throw<WireFormatException>(() => WireCodec.Decode(trailing)).Message.ShouldContain("Trailing payload");
    }

    public static TheoryData<BrowserNetworkCommandMessage> ValidCommands() =>
    [
        new BrowserNetworkCommandMessage(
            3, 1, BrowserNetworkAction.Set, "family", true, BrowserVpnProvider.WireGuard, true, false, SampleWireGuardConfig),
        new BrowserNetworkCommandMessage(3, 2, BrowserNetworkAction.Set, "kids_2"),
        new BrowserNetworkCommandMessage(3, 3, BrowserNetworkAction.Clear, "family"),
        new BrowserNetworkCommandMessage(3, 4, BrowserNetworkAction.RequestSnapshot),
        new BrowserNetworkCommandMessage(3, 5, BrowserNetworkAction.RequestSnapshot, "family"),
    ];

    public static TheoryData<BrowserNetworkStateMessage> ValidStates()
    {
        var data = new TheoryData<BrowserNetworkStateMessage>();
        foreach (BrowserVpnSessionState session in Enum.GetValues<BrowserVpnSessionState>())
        {
            data.Add(new BrowserNetworkStateMessage(
                3,
                7,
                "family",
                true,
                BrowserVpnProvider.WireGuard,
                true,
                false,
                true,
                true,
                "ok",
                session,
                session == BrowserVpnSessionState.Failed ? "connect failed" : ""));
        }

        data.Add(ValidIdleState());
        return data;
    }

    public static TheoryData<BrowserNetworkCommandMessage> InvalidCommands()
    {
        var valid = new BrowserNetworkCommandMessage(
            3, 1, BrowserNetworkAction.Set, "family", true, BrowserVpnProvider.WireGuard, true, false, SampleWireGuardConfig);
        return new TheoryData<BrowserNetworkCommandMessage>
        {
            valid with { Epoch = 0 },
            valid with { CommandId = 0 },
            valid with { Action = (BrowserNetworkAction)0 },
            valid with { ProfileId = "" },
            valid with { ProfileId = "bad id" },
            valid with { VpnEnabled = true, Provider = BrowserVpnProvider.None },
            valid with { ConfigText = "[Interface]\n" },
            new BrowserNetworkCommandMessage(3, 1, BrowserNetworkAction.Clear, "family", true),
            new BrowserNetworkCommandMessage(
                3, 1, BrowserNetworkAction.RequestSnapshot, "", false, BrowserVpnProvider.WireGuard),
            valid with { ProfileId = null! },
            valid with { ConfigText = null! },
        };
    }

    public static TheoryData<BrowserNetworkStateMessage> InvalidStates()
    {
        var valid = ValidIdleState();
        return new TheoryData<BrowserNetworkStateMessage>
        {
            valid with { Epoch = 0 },
            valid with { Revision = 0 },
            valid with { ProfileId = "bad id" },
            valid with { Provider = (BrowserVpnProvider)9 },
            valid with { SessionState = (BrowserVpnSessionState)9 },
            valid with { CapabilityReason = new string('r', BrowserWireLimits.MaxDetailBytes + 1) },
            valid with { SessionDetail = new string('d', BrowserWireLimits.MaxDetailBytes + 1) },
            valid with { ProfileId = null! },
            valid with { CapabilityReason = null! },
            valid with { SessionDetail = null! },
        };
    }

    private static BrowserNetworkStateMessage ValidIdleState() => new(
        3,
        1,
        "family",
        false,
        BrowserVpnProvider.None,
        false,
        false,
        false,
        false,
        "probe",
        BrowserVpnSessionState.Idle);
}
