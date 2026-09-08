using System.Buffers.Binary;
using Shouldly;

namespace Flint.Protocol.Tests;

/// <summary>Contract tests for browser workspace messages 32 through 34.</summary>
public sealed class BrowserWorkspaceWireCodecTests
{
    [Theory]
    [MemberData(nameof(ValidCommands))]
    public void EveryWorkspaceCommandAction_RoundTrips(BrowserWorkspaceCommandMessage message)
    {
        var frame = new WireFrame(3, message);

        WireCodec.Decode(WireCodec.Encode(frame)).ShouldBe(frame);
    }

    [Theory]
    [MemberData(nameof(ValidStates))]
    public void EveryWorkspaceLayout_RoundTrips(BrowserWorkspaceStateMessage message)
    {
        var frame = new WireFrame(3, message);

        WireCodec.Decode(WireCodec.Encode(frame)).ShouldBe(frame);
    }

    [Theory]
    [MemberData(nameof(ValidInputs))]
    public void EveryWorkspaceInputKind_RoundTrips(BrowserWorkspaceInputMessage message)
    {
        var frame = new WireFrame(3, message);

        WireCodec.Decode(WireCodec.Encode(frame)).ShouldBe(frame);
    }

    [Fact]
    public void WorkspaceMessages_AreAdditiveV3TlsBrowserTypes()
    {
        ((int)WireMessageType.BrowserWorkspaceCommand).ShouldBe(32);
        ((int)WireMessageType.BrowserWorkspaceState).ShouldBe(33);
        ((int)WireMessageType.BrowserWorkspaceInput).ShouldBe(34);

        WireMessage[] messages =
        [
            new BrowserWorkspaceCommandMessage(4, 1, 140, BrowserWorkspaceCommandAction.RequestSnapshot),
            new BrowserWorkspaceStateMessage(
                4,
                1,
                BrowserWorkspaceWireLayout.Single,
                FocusedPaneId: 0,
                BrowserWorkspaceWireInteractionMode.WorkspaceChrome,
                PageFullscreenPaneId: 0,
                TheaterPaneId: 0,
                MaxLiveRenderers: 1,
                MaxOpenPanes: 1,
                ValueList<BrowserWorkspacePaneStateEntry>.Empty),
            new BrowserWorkspaceInputMessage(
                4,
                1,
                140,
                42,
                BrowserWorkspaceInputKind.Text,
                Text: "hello"),
        ];
        foreach (var message in messages)
        {
            BrowserWireRules.IsBrowserMessage(message).ShouldBeTrue();
            BrowserWireRules.IsWorkspaceMessage(message).ShouldBeTrue();
            BrowserWireRules.IsBrowserType(message.TypeId).ShouldBeTrue();
            BrowserWireRules.IsForbiddenOnOrdinaryChannel(message).ShouldBeTrue();
            Should.Throw<WireFormatException>(() => WireCodec.Encode(new WireFrame(2, message)))
                .Message.ShouldContain("version 3");
        }

        foreach (var message in messages)
        {
            var frame = new WireFrame(3, message);
            WireCodec.Decode(WireCodec.Encode(frame)).ShouldBe(frame);
        }

        BrowserWireRules.IsBrowserType(32).ShouldBeTrue();
        BrowserWireRules.IsBrowserType(33).ShouldBeTrue();
        BrowserWireRules.IsBrowserType(34).ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(InvalidCommands))]
    public void InvalidWorkspaceCommands_AreRejected(BrowserWorkspaceCommandMessage message)
    {
        Should.Throw<WireFormatException>(() => WireCodec.Encode(new WireFrame(3, message)));
    }

    [Theory]
    [MemberData(nameof(InvalidStates))]
    public void InvalidWorkspaceStates_AreRejected(BrowserWorkspaceStateMessage message)
    {
        Should.Throw<WireFormatException>(() => WireCodec.Encode(new WireFrame(3, message)));
    }

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public void InvalidWorkspaceInputs_AreRejected(BrowserWorkspaceInputMessage message)
    {
        Should.Throw<WireFormatException>(() => WireCodec.Encode(new WireFrame(3, message)));
    }

    [Fact]
    public void WorkspaceDecoders_RejectUnknownEnumsAndTrailingBytes()
    {
        var command = WireCodec.Encode(new WireFrame(
            3,
            new BrowserWorkspaceCommandMessage(4, 1, 140, BrowserWorkspaceCommandAction.RequestSnapshot)));
        command[12 + 24] = 0;
        Should.Throw<WireFormatException>(() => WireCodec.Decode(command)).Message.ShouldContain("Unknown");

        var state = WireCodec.Encode(new WireFrame(3, ValidSinglePaneState()));
        state[12 + 16] = 0;
        Should.Throw<WireFormatException>(() => WireCodec.Decode(state)).Message.ShouldContain("Unknown");

        var encoded = WireCodec.Encode(new WireFrame(3, ValidSinglePaneState()));
        var trailing = encoded.Append((byte)0).ToArray();
        BinaryPrimitives.WriteInt32BigEndian(trailing, encoded.Length - 3);
        Should.Throw<WireFormatException>(() => WireCodec.Decode(trailing)).Message.ShouldContain("Trailing payload");
    }

    public static TheoryData<BrowserWorkspaceCommandMessage> ValidCommands() =>
    [
        new BrowserWorkspaceCommandMessage(4, 130, 140, BrowserWorkspaceCommandAction.Focus, PaneId: 42),
        new BrowserWorkspaceCommandMessage(
            4,
            128,
            140,
            BrowserWorkspaceCommandAction.OpenPane,
            Url: "https://example.test/new"),
        new BrowserWorkspaceCommandMessage(4, 131, 140, BrowserWorkspaceCommandAction.ClosePane, PaneId: 42),
        new BrowserWorkspaceCommandMessage(
            4,
            132,
            140,
            BrowserWorkspaceCommandAction.SetLayout,
            Value: (byte)BrowserWorkspaceWireLayout.TwoColumns),
        new BrowserWorkspaceCommandMessage(
            4,
            133,
            140,
            BrowserWorkspaceCommandAction.Navigate,
            PaneId: 42,
            Url: "https://example.test/next"),
        new BrowserWorkspaceCommandMessage(4, 134, 140, BrowserWorkspaceCommandAction.Reload, PaneId: 42),
        new BrowserWorkspaceCommandMessage(4, 135, 140, BrowserWorkspaceCommandAction.Back, PaneId: 42),
        new BrowserWorkspaceCommandMessage(4, 136, 140, BrowserWorkspaceCommandAction.Forward, PaneId: 42),
        new BrowserWorkspaceCommandMessage(4, 137, 140, BrowserWorkspaceCommandAction.SetMute, PaneId: 42, Value: 1),
        new BrowserWorkspaceCommandMessage(4, 138, 140, BrowserWorkspaceCommandAction.PlayPause, PaneId: 42),
        new BrowserWorkspaceCommandMessage(
            4,
            139,
            140,
            BrowserWorkspaceCommandAction.SetInteraction,
            Value: (byte)BrowserWorkspaceWireInteractionMode.Page),
        new BrowserWorkspaceCommandMessage(4, 140, 140, BrowserWorkspaceCommandAction.EnterTheater, PaneId: 42),
        new BrowserWorkspaceCommandMessage(4, 141, 140, BrowserWorkspaceCommandAction.ExitTheater),
        new BrowserWorkspaceCommandMessage(4, 142, 140, BrowserWorkspaceCommandAction.RequestSnapshot),
        new BrowserWorkspaceCommandMessage(4, 143, 140, BrowserWorkspaceCommandAction.MovePane, PaneId: 42, Value: 1),
    ];

    public static TheoryData<BrowserWorkspaceStateMessage> ValidStates()
    {
        var data = new TheoryData<BrowserWorkspaceStateMessage>();
        foreach (BrowserWorkspaceWireLayout layout in Enum.GetValues<BrowserWorkspaceWireLayout>())
        {
            data.Add(ValidSinglePaneState() with { Layout = layout });
        }

        data.Add(new BrowserWorkspaceStateMessage(
            4,
            141,
            BrowserWorkspaceWireLayout.Single,
            FocusedPaneId: 0,
            BrowserWorkspaceWireInteractionMode.WorkspaceChrome,
            PageFullscreenPaneId: 0,
            TheaterPaneId: 0,
            MaxLiveRenderers: 1,
            MaxOpenPanes: 1,
            ValueList<BrowserWorkspacePaneStateEntry>.Empty));
        return data;
    }

    public static TheoryData<BrowserWorkspaceInputMessage> ValidInputs() =>
    [
        new BrowserWorkspaceInputMessage(
            4,
            131,
            140,
            42,
            BrowserWorkspaceInputKind.Text,
            Text: "hello"),
        new BrowserWorkspaceInputMessage(
            4,
            132,
            140,
            42,
            BrowserWorkspaceInputKind.Key,
            Key: BrowserSemanticKey.Select),
    ];

    public static TheoryData<BrowserWorkspaceCommandMessage> InvalidCommands()
    {
        var valid = new BrowserWorkspaceCommandMessage(4, 130, 140, BrowserWorkspaceCommandAction.Focus, PaneId: 42);
        return new TheoryData<BrowserWorkspaceCommandMessage>
        {
            valid with { Epoch = 0 },
            valid with { CommandId = 0 },
            valid with { ExpectedRevision = 0 },
            valid with { PaneId = 0 },
            valid with { Action = (BrowserWorkspaceCommandAction)0 },
            valid with { Url = "https://example.test" },
            new BrowserWorkspaceCommandMessage(
                4,
                128,
                140,
                BrowserWorkspaceCommandAction.OpenPane,
                PaneId: 42,
                Url: "https://example.test/new"),
            valid with { Url = null! },
        };
    }

    public static TheoryData<BrowserWorkspaceStateMessage> InvalidStates()
    {
        var valid = ValidSinglePaneState();
        return new TheoryData<BrowserWorkspaceStateMessage>
        {
            valid with { Epoch = 0 },
            valid with { Revision = 0 },
            valid with { MaxLiveRenderers = 0 },
            valid with { MaxOpenPanes = 0 },
            valid with { MaxLiveRenderers = 3, MaxOpenPanes = 2 },
            valid with
            {
                Panes = ValueList<BrowserWorkspacePaneStateEntry>.From(
                [
                    new BrowserWorkspacePaneStateEntry(
                        42,
                        0,
                        BrowserWorkspaceWirePaneResidency.Live,
                        "",
                        "",
                        Loading: false,
                        100,
                        CanGoBack: false,
                        CanGoForward: false,
                        DesiredMuted: false,
                        BrowserWorkspaceWireMuteApplication.NotRequested,
                        BrowserWorkspaceWireObservedPlayback.Unknown),
                ]),
            },
            valid with { FocusedPaneId = 99 },
        };
    }

    public static TheoryData<BrowserWorkspaceInputMessage> InvalidInputs()
    {
        var valid = new BrowserWorkspaceInputMessage(
            4,
            131,
            140,
            42,
            BrowserWorkspaceInputKind.Text,
            Text: "hello");
        return new TheoryData<BrowserWorkspaceInputMessage>
        {
            valid with { Epoch = 0 },
            valid with { CommandId = 0 },
            valid with { ExpectedRevision = 0 },
            valid with { PaneId = 0 },
            valid with { Kind = (BrowserWorkspaceInputKind)0 },
            valid with { Text = "" },
            new BrowserWorkspaceInputMessage(
                4,
                131,
                140,
                42,
                BrowserWorkspaceInputKind.Key,
                Text: "hello"),
            valid with { Text = null! },
        };
    }

    private static BrowserWorkspaceStateMessage ValidSinglePaneState() => new(
        4,
        140,
        BrowserWorkspaceWireLayout.Single,
        FocusedPaneId: 42,
        BrowserWorkspaceWireInteractionMode.WorkspaceChrome,
        PageFullscreenPaneId: 0,
        TheaterPaneId: 0,
        MaxLiveRenderers: 2,
        MaxOpenPanes: 4,
        ValueList<BrowserWorkspacePaneStateEntry>.From(
        [
            new BrowserWorkspacePaneStateEntry(
                42,
                0,
                BrowserWorkspaceWirePaneResidency.Live,
                "https://example.test/new",
                "",
                Loading: false,
                100,
                CanGoBack: false,
                CanGoForward: false,
                DesiredMuted: false,
                BrowserWorkspaceWireMuteApplication.NotRequested,
                BrowserWorkspaceWireObservedPlayback.Unknown),
        ]));
}
