using System.Buffers.Binary;
using Shouldly;

namespace Flint.Protocol.Tests;

/// <summary>Contract tests for browser profile selection messages 28 and 29.</summary>
public sealed class BrowserProfileWireCodecTests
{
    [Theory]
    [MemberData(nameof(ValidCommands))]
    public void EveryProfileCommandAction_RoundTrips(BrowserProfileCommandMessage message)
    {
        var frame = new WireFrame(message);

        WireCodec.Decode(WireCodec.Encode(frame)).ShouldBe(frame);
    }

    [Theory]
    [MemberData(nameof(ValidStates))]
    public void EveryProfileStateSource_RoundTrips(BrowserProfileStateMessage message)
    {
        var frame = new WireFrame(message);

        WireCodec.Decode(WireCodec.Encode(frame)).ShouldBe(frame);
    }

    [Fact]
    public void ProfileMessages_AreAdditiveV2TlsBrowserTypes()
    {
        ((int)WireMessageType.BrowserProfileCommand).ShouldBe(28);
        ((int)WireMessageType.BrowserProfileState).ShouldBe(29);

        WireMessage[] messages =
        [
            new BrowserProfileCommandMessage(3, 1, BrowserProfileAction.RequestSnapshot),
            new BrowserProfileStateMessage(
                3, 1, BrowserProfileSource.Device, "", "PC", ValueList<BrowserProfileEntry>.Empty),
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
    }

    [Theory]
    [MemberData(nameof(InvalidCommands))]
    public void InvalidProfileCommands_AreRejected(BrowserProfileCommandMessage message)
    {
        Should.Throw<WireFormatException>(() => WireCodec.Encode(new WireFrame(message)));
    }

    [Theory]
    [MemberData(nameof(InvalidStates))]
    public void InvalidProfileStates_AreRejected(BrowserProfileStateMessage message)
    {
        Should.Throw<WireFormatException>(() => WireCodec.Encode(new WireFrame(message)));
    }

    [Fact]
    public void ProfileDecoders_RejectUnknownEnumsAndTrailingBytes()
    {
        var command = WireCodec.Encode(new WireFrame(
            new BrowserProfileCommandMessage(3, 1, BrowserProfileAction.RequestSnapshot)));
        command[12 + 16] = 0;
        Should.Throw<WireFormatException>(() => WireCodec.Decode(command)).Message.ShouldContain("Unknown");

        var state = WireCodec.Encode(new WireFrame(new BrowserProfileStateMessage(
            3, 1, BrowserProfileSource.Device, "", "PC", ValueList<BrowserProfileEntry>.Empty)));
        state[12 + 16] = 0;
        Should.Throw<WireFormatException>(() => WireCodec.Decode(state)).Message.ShouldContain("Unknown");

        var encoded = WireCodec.Encode(new WireFrame(ValidTvState()));
        var trailing = encoded.Append((byte)0).ToArray();
        BinaryPrimitives.WriteInt32BigEndian(trailing, encoded.Length - 3);
        Should.Throw<WireFormatException>(() => WireCodec.Decode(trailing)).Message.ShouldContain("Trailing payload");
    }

    [Fact]
    public void ProfileStateDecoder_RejectsCountAboveEightBeforeAllocatingRows()
    {
        var bytes = WireCodec.Encode(new WireFrame(new BrowserProfileStateMessage(
            3, 1, BrowserProfileSource.Device, "", "", ValueList<BrowserProfileEntry>.Empty)));
        bytes[12 + 21] = BrowserWireLimits.MaxTvProfiles + 1;

        Should.Throw<WireFormatException>(() => WireCodec.Decode(bytes))
            .Message.ShouldContain("profiles");
    }

    public static TheoryData<BrowserProfileCommandMessage> ValidCommands() =>
    [
        new BrowserProfileCommandMessage(3, 1, BrowserProfileAction.SelectTvProfile, "family", ""),
        new BrowserProfileCommandMessage(3, 2, BrowserProfileAction.CreateTvProfile, "", "Kids 🚀"),
        new BrowserProfileCommandMessage(3, 3, BrowserProfileAction.RenameTvProfile, "family_2", "Family room"),
        new BrowserProfileCommandMessage(3, 4, BrowserProfileAction.DeleteTvProfile, "family-2", ""),
        new BrowserProfileCommandMessage(3, 5, BrowserProfileAction.SelectDevice, "", ""),
        new BrowserProfileCommandMessage(3, 6, BrowserProfileAction.RequestSnapshot, "", ""),
    ];

    public static TheoryData<BrowserProfileStateMessage> ValidStates() =>
    [
        ValidTvState(),
        new BrowserProfileStateMessage(
            3,
            8,
            BrowserProfileSource.Device,
            "",
            "Tochukwu's PC",
            ValueList<BrowserProfileEntry>.From([new BrowserProfileEntry("family", "Family")])),
        new BrowserProfileStateMessage(
            3, 9, BrowserProfileSource.Device, "", "", ValueList<BrowserProfileEntry>.Empty),
    ];

    public static TheoryData<BrowserProfileCommandMessage> InvalidCommands()
    {
        var valid = new BrowserProfileCommandMessage(3, 1, BrowserProfileAction.SelectTvProfile, "family", "");
        return new TheoryData<BrowserProfileCommandMessage>
        {
            valid with { Epoch = 0 },
            valid with { CommandId = 0 },
            valid with { Action = (BrowserProfileAction)0 },
            valid with { ProfileId = "" },
            valid with { ProfileId = "bad id" },
            valid with { ProfileId = new string('a', BrowserWireLimits.MaxProfileIdBytes + 1) },
            valid with { Name = "extra" },
            new BrowserProfileCommandMessage(3, 1, BrowserProfileAction.CreateTvProfile, "generated", "Family"),
            new BrowserProfileCommandMessage(3, 1, BrowserProfileAction.CreateTvProfile, "", ""),
            new BrowserProfileCommandMessage(3, 1, BrowserProfileAction.CreateTvProfile, "", " Family"),
            new BrowserProfileCommandMessage(3, 1, BrowserProfileAction.CreateTvProfile, "", "Family\nRoom"),
            new BrowserProfileCommandMessage(3, 1, BrowserProfileAction.CreateTvProfile, "", new string('é', 33)),
            new BrowserProfileCommandMessage(3, 1, BrowserProfileAction.RenameTvProfile, "family", ""),
            new BrowserProfileCommandMessage(3, 1, BrowserProfileAction.DeleteTvProfile, "family", "extra"),
            new BrowserProfileCommandMessage(3, 1, BrowserProfileAction.SelectDevice, "family", ""),
            new BrowserProfileCommandMessage(3, 1, BrowserProfileAction.RequestSnapshot, "", "extra"),
            valid with { ProfileId = null! },
            valid with { Name = null! },
        };
    }

    public static TheoryData<BrowserProfileStateMessage> InvalidStates()
    {
        var profiles = ValueList<BrowserProfileEntry>.From([new BrowserProfileEntry("family", "Family")]);
        var tv = new BrowserProfileStateMessage(3, 1, BrowserProfileSource.Tv, "family", "", profiles);
        var device = new BrowserProfileStateMessage(3, 1, BrowserProfileSource.Device, "", "PC", profiles);
        return new TheoryData<BrowserProfileStateMessage>
        {
            tv with { Epoch = 0 },
            tv with { Revision = 0 },
            tv with { ActiveSource = (BrowserProfileSource)0 },
            tv with { ActiveProfileId = "" },
            tv with { ActiveProfileId = "missing" },
            device with { ActiveProfileId = "family" },
            device with { DeviceName = new string('p', BrowserWireLimits.MaxDeviceProfileNameBytes + 1) },
            device with { DeviceName = "PC\u0000" },
            tv with { Profiles = RepeatProfiles(BrowserWireLimits.MaxTvProfiles + 1) },
            tv with { Profiles = ValueList<BrowserProfileEntry>.From([new BrowserProfileEntry("bad id", "Family")]) },
            tv with { Profiles = ValueList<BrowserProfileEntry>.From([new BrowserProfileEntry("family", " Family")]) },
            tv with { Profiles = ValueList<BrowserProfileEntry>.From([new BrowserProfileEntry("family", "Family\nRoom")]) },
            tv with { Profiles = ValueList<BrowserProfileEntry>.From([new BrowserProfileEntry("family", "Family"), new BrowserProfileEntry("family", "Other")]) },
            tv with { Profiles = ValueList<BrowserProfileEntry>.From([new BrowserProfileEntry("family", "Family"), new BrowserProfileEntry("other", "Family")]) },
            tv with { Profiles = ValueList<BrowserProfileEntry>.From([null!]) },
            tv with { ActiveProfileId = null! },
            tv with { DeviceName = null! },
        };
    }

    private static BrowserProfileStateMessage ValidTvState() => new(
        3,
        7,
        BrowserProfileSource.Tv,
        "family",
        "",
        ValueList<BrowserProfileEntry>.From(
        [
            new BrowserProfileEntry("family", "Family"),
            new BrowserProfileEntry("kids_2", "Kids 🚀"),
        ]));

    private static ValueList<BrowserProfileEntry> RepeatProfiles(int count) =>
        ValueList<BrowserProfileEntry>.From(Enumerable.Range(0, count)
            .Select(index => new BrowserProfileEntry($"p{index}", $"Profile {index}")));
}
