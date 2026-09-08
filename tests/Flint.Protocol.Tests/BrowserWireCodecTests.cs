using System.Buffers.Binary;
using Shouldly;

namespace Flint.Protocol.Tests;

/// <summary>
/// The v2 browser contract is held to strict typed round trips as well as the cross-language
/// corpus. These tests exercise the independent validation branches without relying on a socket
/// or a WebView.
/// </summary>
public sealed class BrowserWireCodecTests
{
    public static TheoryData<WireMessage> ValidMessages() =>
    [
        new BrowserCapabilityMessage(
            BrowserCapabilityStatus.Available, 8443, 25, "120.0", true, 960, 540, 5, 1,
            BrowserWireLimits.MaxPreviewBytes),
        new BrowserCommandMessage(1, 1, BrowserCommandAction.Open, "https://example.com/a"),
        new BrowserInputMessage(1, 1, new BrowserSemanticKeyInput(BrowserSemanticKey.Select)),
        new BrowserStateMessage(
            1, 1, 1, 1, 1, BrowserLoadState.Loaded, "https://example.com/a", "Example", 100,
            CanGoBack: true, CanGoForward: false, 1920, 1080, BrowserPreviewState.Disabled),
        new BrowserPreviewMessage(1, 1, 1, 1, 1, BinaryData.From([0xff, 0xd8, 0xff, 0xd9])),
        new BrowserDialogMessage(1, 1, BrowserDialogType.Confirm, "https://example.com", "Leave?", "", 10_000),
        new BrowserDialogReplyMessage(1, 1, Accepted: true),
        new BrowserTabCommandMessage(1, 1, BrowserTabAction.New, 0, "https://example.com/new"),
        new BrowserTabStateMessage(
            1,
            1,
            3,
            ValueList<BrowserTabStateEntry>.From(
            [
                new BrowserTabStateEntry(
                    3,
                    BrowserLoadState.Loaded,
                    100,
                    CanGoBack: true,
                    CanGoForward: false,
                    Frozen: false,
                    FaviconId: 4,
                    "https://example.com/a",
                    "Example"),
            ])),
        new BrowserViewCommandMessage(1, 1, BrowserViewAction.FindStart, Text: "example"),
        new BrowserViewStateMessage(
            1,
            1,
            125,
            BrowserUserAgentMode.Tv,
            BrowserDarkMode.FollowSystem,
            BrowserInteractionMode.Cursor,
            Fullscreen: false,
            MediaPlaying: false,
            EditingFocused: false,
            FindActive: false,
            FindCurrent: 0,
            FindTotal: 0,
            BrowserSearchEngine.DuckDuckGo),
        new BrowserFaviconMessage(1, 1, 1, 1, BinaryData.From([0x89])),
        new BrowserLibraryCommandMessage(
            1,
            1,
            BrowserLibraryAction.AddBookmark,
            "https://example.com/a",
            "Example"),
        new BrowserLibraryStateMessage(
            1,
            1,
            ValueList<BrowserLibraryEntry>.From(
            [
                new BrowserLibraryEntry(
                    BrowserLibraryEntryKind.Bookmark,
                    FaviconId: 1,
                    LastVisitedMilliseconds: 1,
                    "https://example.com/a",
                    "Example"),
            ]),
            ValueList<BrowserLibraryEntry>.Empty),
    ];

    [Theory]
    [MemberData(nameof(ValidMessages))]
    public void BrowserV2Message_RoundTripsWithoutTrailingOrVersionAmbiguity(WireMessage message)
    {
        var original = new WireFrame(ProtocolVersion.Current, message, Flags: 0x33);

        var decoded = WireCodec.Decode(WireCodec.Encode(original));

        decoded.ShouldBe(original);
    }

    [Theory]
    [MemberData(nameof(ValidMessages))]
    public void BrowserV2Message_CannotBeEncodedAtV1(WireMessage message)
    {
        Should.Throw<WireFormatException>(() => WireCodec.Encode(new WireFrame(1, message)))
            .Message.ShouldContain("version 2");
    }

    [Fact]
    public void BrowserV2Frame_AtV1IsRejectedBeforePayloadDispatch()
    {
        var bytes = WireCodec.Encode(new WireFrame(new BrowserCommandMessage(
            1,
            1,
            BrowserCommandAction.Close)));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6, 2), 1);

        Should.Throw<WireFormatException>(() => WireCodec.Decode(bytes))
            .Message.ShouldContain("version 2");
    }

    [Theory]
    [InlineData(BrowserPointerAction.Down, 0)]
    [InlineData(BrowserPointerAction.Up, 1)]
    [InlineData(BrowserPointerAction.Cancel, 1)]
    public void BrowserPointer_InvalidPrimaryTransitionIsRejected(BrowserPointerAction action, int buttons)
    {
        var message = new BrowserInputMessage(
            1,
            1,
            new BrowserPointerInput(action, 1, 1, 100, 100, buttons));

        Should.Throw<WireFormatException>(() => WireCodec.Encode(new WireFrame(message)));
    }

    [Fact]
    public void BrowserCommand_RejectsAnUnexpectedUrlOrPreviewFlag()
    {
        Should.Throw<WireFormatException>(() => WireCodec.Encode(new WireFrame(
            new BrowserCommandMessage(1, 1, BrowserCommandAction.Close, Url: "https://example.com"))));
        Should.Throw<WireFormatException>(() => WireCodec.Encode(new WireFrame(
            new BrowserCommandMessage(1, 1, BrowserCommandAction.Navigate, PreviewEnabled: true))));
    }

    [Fact]
    public void BrowserRules_ReserveEveryBrowserValueAndBrowserSurfaceForTheTlsRoute()
    {
        BrowserWireRules.IsForbiddenOnOrdinaryChannel(
            new BrowserCommandMessage(1, 1, BrowserCommandAction.Close)).ShouldBeTrue();
        BrowserWireRules.IsForbiddenOnOrdinaryChannel(new SurfaceMessage(SurfaceMode.Browser)).ShouldBeTrue();
        BrowserWireRules.IsForbiddenOnOrdinaryChannel(new SurfaceMessage(SurfaceMode.Player)).ShouldBeFalse();
    }

    [Fact]
    public void BrowserPreview_RejectsTheFixedMemoryEnvelopeOverflow()
    {
        var message = new BrowserPreviewMessage(
            1,
            1,
            1,
            960,
            540,
            BinaryData.From(new byte[BrowserWireLimits.MaxPreviewBytes + 1]));

        Should.Throw<WireFormatException>(() => WireCodec.Encode(new WireFrame(message)));
    }
}
