using System.Buffers.Binary;
using Shouldly;

namespace Flint.Protocol.Tests;

/// <summary>Strict validation and routing coverage for additive browser messages 21 through 27.</summary>
public sealed class BrowserWirePhase2CodecTests
{
    [Theory]
    [MemberData(nameof(EveryValidCommandAction))]
    public void EveryPhaseTwoCommandAction_RoundTrips(string _, WireMessage message)
    {
        var frame = new WireFrame(message);

        WireCodec.Decode(WireCodec.Encode(frame)).ShouldBe(frame);
    }

    [Theory]
    [MemberData(nameof(EveryValidStateShape))]
    public void EveryPhaseTwoStateShape_RoundTrips(string _, WireMessage message)
    {
        var frame = new WireFrame(message);

        WireCodec.Decode(WireCodec.Encode(frame)).ShouldBe(frame);
    }

    [Theory]
    [MemberData(nameof(PhaseTwoMessages))]
    public void EveryPhaseTwoMessage_IsV2OnlyAndReservedForTheTlsBrowserChannel(WireMessage message)
    {
        BrowserWireRules.IsBrowserMessage(message).ShouldBeTrue();
        BrowserWireRules.IsBrowserType(message.TypeId).ShouldBeTrue();
        BrowserWireRules.IsForbiddenOnOrdinaryChannel(message).ShouldBeTrue();
        BrowserWireRules.IsAllowedAtVersion(1, message).ShouldBeFalse();
        BrowserWireRules.IsAllowedAtVersion(2, message).ShouldBeTrue();
        Should.Throw<WireFormatException>(() => WireCodec.Encode(new WireFrame(1, message)))
            .Message.ShouldContain("version 2");
    }

    [Fact]
    public void BrowserTypeIds_IncludeTheAdditiveRangeThrough34()
    {
        ((int)WireMessageType.BrowserTabCommand).ShouldBe(21);
        ((int)WireMessageType.BrowserTabState).ShouldBe(22);
        ((int)WireMessageType.BrowserViewCommand).ShouldBe(23);
        ((int)WireMessageType.BrowserViewState).ShouldBe(24);
        ((int)WireMessageType.BrowserFavicon).ShouldBe(25);
        ((int)WireMessageType.BrowserLibraryCommand).ShouldBe(26);
        ((int)WireMessageType.BrowserLibraryState).ShouldBe(27);

        foreach (var typeId in Enumerable.Range(14, 21))
        {
            BrowserWireRules.IsBrowserType(typeId).ShouldBeTrue();
        }

        BrowserWireRules.IsBrowserType(13).ShouldBeFalse();
        BrowserWireRules.IsBrowserType(37).ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(PhaseTwoMessages))]
    public void EveryPhaseTwoDecoder_RejectsTrailingPayloadBytes(WireMessage message)
    {
        var encoded = WireCodec.Encode(new WireFrame(message));
        var withTrailingPayload = encoded.Append((byte)0).ToArray();
        BinaryPrimitives.WriteInt32BigEndian(withTrailingPayload.AsSpan(0, 4), encoded.Length - 3);

        Should.Throw<WireFormatException>(() => WireCodec.Decode(withTrailingPayload))
            .Message.ShouldContain("Trailing payload");
    }

    [Theory]
    [MemberData(nameof(InvalidMessages))]
    public void Encoder_RejectsEveryPhaseTwoInvariant(string _, WireMessage message)
    {
        Should.Throw<WireFormatException>(() => WireCodec.Encode(new WireFrame(message)));
    }

    [Theory]
    [MemberData(nameof(UnknownEnumPayloads))]
    public void Decoder_RejectsUnknownPhaseTwoEnumValues(string _, byte[] bytes)
    {
        Should.Throw<WireFormatException>(() => WireCodec.Decode(bytes))
            .Message.ShouldContain("Unknown");
    }

    [Fact]
    public void Decoder_RejectsTabCountAboveTheEightTabContractBeforeAllocatingEntries()
    {
        var bytes = WireCodec.Encode(new WireFrame(new BrowserTabStateMessage(
            1,
            1,
            0,
            ValueList<BrowserTabStateEntry>.Empty)));
        bytes[12 + 24] = BrowserWireLimits.MaxTabs + 1;

        Should.Throw<WireFormatException>(() => WireCodec.Decode(bytes))
            .Message.ShouldContain("tabs");
    }

    [Fact]
    public void UnknownTypeImmediatelyAfterBrowserRange_RemainsForwardCompatible()
    {
        var frame = new WireFrame(2, new UnknownMessage(37, BinaryData.From([1, 2, 3])));

        WireCodec.Decode(WireCodec.Encode(frame)).ShouldBe(frame);
    }

    public static TheoryData<WireMessage> PhaseTwoMessages() =>
    [
        new BrowserTabCommandMessage(1, 1, BrowserTabAction.New, 0),
        new BrowserTabStateMessage(1, 1, 0, ValueList<BrowserTabStateEntry>.Empty),
        new BrowserViewCommandMessage(1, 1, BrowserViewAction.SetZoom, 100),
        ValidViewState(),
        new BrowserFaviconMessage(1, 1, 1, 1, BinaryData.From([0x89])),
        new BrowserLibraryCommandMessage(1, 1, BrowserLibraryAction.RequestSnapshot),
        new BrowserLibraryStateMessage(
            1,
            1,
            ValueList<BrowserLibraryEntry>.Empty,
            ValueList<BrowserLibraryEntry>.Empty),
    ];

    public static TheoryData<string, WireMessage> EveryValidCommandAction()
    {
        var data = new TheoryData<string, WireMessage>
        {
            { "tab-new", new BrowserTabCommandMessage(1, 1, BrowserTabAction.New, 0, "https://example.test") },
            { "tab-close", new BrowserTabCommandMessage(1, 2, BrowserTabAction.Close, 1) },
            { "tab-select", new BrowserTabCommandMessage(1, 3, BrowserTabAction.Select, 1) },
            { "tab-move", new BrowserTabCommandMessage(1, 4, BrowserTabAction.Move, 1) },
            { "tab-duplicate", new BrowserTabCommandMessage(1, 5, BrowserTabAction.Duplicate, 1) },
            { "view-zoom", new BrowserViewCommandMessage(1, 6, BrowserViewAction.SetZoom, 125) },
            { "view-fullscreen-off", new BrowserViewCommandMessage(1, 7, BrowserViewAction.SetFullscreen, 0) },
            { "view-fullscreen-on", new BrowserViewCommandMessage(1, 8, BrowserViewAction.SetFullscreen, 1) },
            { "view-find-start", new BrowserViewCommandMessage(1, 9, BrowserViewAction.FindStart, 0, "needle") },
            { "view-find-next", new BrowserViewCommandMessage(1, 10, BrowserViewAction.FindNext) },
            { "view-find-prev", new BrowserViewCommandMessage(1, 11, BrowserViewAction.FindPrev) },
            { "view-find-clear", new BrowserViewCommandMessage(1, 12, BrowserViewAction.FindClear) },
            { "library-add", new BrowserLibraryCommandMessage(1, 13, BrowserLibraryAction.AddBookmark, "https://example.test", "Example") },
            { "library-remove", new BrowserLibraryCommandMessage(1, 14, BrowserLibraryAction.RemoveBookmark, "https://example.test") },
            { "library-clear-history", new BrowserLibraryCommandMessage(1, 15, BrowserLibraryAction.ClearHistory) },
            { "library-clear-bookmarks", new BrowserLibraryCommandMessage(1, 16, BrowserLibraryAction.ClearBookmarks) },
            { "library-request", new BrowserLibraryCommandMessage(1, 17, BrowserLibraryAction.RequestSnapshot) },
        };

        var commandId = 20L;
        foreach (var mode in Enum.GetValues<BrowserUserAgentMode>())
        {
            data.Add($"view-UA-{mode}", new BrowserViewCommandMessage(
                1, commandId++, BrowserViewAction.SetUa, (int)mode));
        }

        foreach (var mode in Enum.GetValues<BrowserDarkMode>())
        {
            data.Add($"view-dark-{mode}", new BrowserViewCommandMessage(
                1, commandId++, BrowserViewAction.SetDark, (int)mode));
        }

        foreach (var mode in Enum.GetValues<BrowserInteractionMode>())
        {
            data.Add($"view-input-{mode}", new BrowserViewCommandMessage(
                1, commandId++, BrowserViewAction.SetInputMode, (int)mode));
        }

        foreach (var engine in Enum.GetValues<BrowserSearchEngine>())
        {
            data.Add($"view-search-{engine}", new BrowserViewCommandMessage(
                1,
                commandId++,
                BrowserViewAction.SetSearchEngine,
                (int)engine,
                engine == BrowserSearchEngine.Custom ? "https://search.example/?q={q}" : ""));
        }

        return data;
    }

    public static TheoryData<string, WireMessage> EveryValidStateShape()
    {
        var tabsMixed = new BrowserTabStateMessage(
                1,
                2,
                2,
                ValueList<BrowserTabStateEntry>.From(
                [
                    new BrowserTabStateEntry(
                        1,
                        BrowserLoadState.Idle,
                        0,
                        CanGoBack: false,
                        CanGoForward: true,
                        Frozen: true,
                        FaviconId: 0,
                        "",
                        "New tab"),
                    new BrowserTabStateEntry(
                        2,
                        BrowserLoadState.Failed,
                        50,
                        CanGoBack: true,
                        CanGoForward: false,
                        Frozen: false,
                        FaviconId: 1,
                        "https://example.test",
                        "Failed"),
                ]));
        var viewActiveNoResults = ValidViewState() with
        {
            Fullscreen = true,
            MediaPlaying = true,
            EditingFocused = true,
            FindActive = true,
        };
        var libraryEmpty = new BrowserLibraryStateMessage(
                1,
                1,
                ValueList<BrowserLibraryEntry>.Empty,
                ValueList<BrowserLibraryEntry>.Empty);
        var libraryPopulated = new BrowserLibraryStateMessage(
                1,
                2,
                ValueList<BrowserLibraryEntry>.From([ValidLibraryEntry(BrowserLibraryEntryKind.Bookmark)]),
                ValueList<BrowserLibraryEntry>.From([ValidLibraryEntry(BrowserLibraryEntryKind.History)]));

        return new TheoryData<string, WireMessage>
        {
            { "tabs-empty", new BrowserTabStateMessage(1, 1, 0, ValueList<BrowserTabStateEntry>.Empty) },
            { "tabs-mixed", tabsMixed },
            { "view-inactive", ValidViewState() },
            { "view-active-no-results", viewActiveNoResults },
            { "view-active-results", ValidViewState() with { FindActive = true, FindCurrent = 2, FindTotal = 5 } },
            { "library-empty", libraryEmpty },
            { "library-populated", libraryPopulated },
        };
    }

    public static TheoryData<string, WireMessage> InvalidMessages()
    {
        var validTab = ValidTab();
        var bookmark = ValidLibraryEntry(BrowserLibraryEntryKind.Bookmark);
        var history = ValidLibraryEntry(BrowserLibraryEntryKind.History);
        return new TheoryData<string, WireMessage>
        {
            { "tab-command-epoch", new BrowserTabCommandMessage(0, 1, BrowserTabAction.New, 0) },
            { "tab-command-id", new BrowserTabCommandMessage(1, 0, BrowserTabAction.New, 0) },
            { "tab-command-enum", new BrowserTabCommandMessage(1, 1, (BrowserTabAction)0, 0) },
            { "new-tab-id", new BrowserTabCommandMessage(1, 1, BrowserTabAction.New, 1) },
            { "named-tab-id", new BrowserTabCommandMessage(1, 1, BrowserTabAction.Close, 0) },
            { "non-new-url", new BrowserTabCommandMessage(1, 1, BrowserTabAction.Close, 1, "https://example.test") },
            { "blank-new-url", new BrowserTabCommandMessage(1, 1, BrowserTabAction.New, 0, " ") },
            { "long-new-url", new BrowserTabCommandMessage(1, 1, BrowserTabAction.New, 0, new string('a', BrowserWireLimits.MaxUrlBytes + 1)) },
            { "too-many-tabs", new BrowserTabStateMessage(1, 1, 1, RepeatedTabs(BrowserWireLimits.MaxTabs + 1)) },
            { "empty-tabs-active", new BrowserTabStateMessage(1, 1, 1, ValueList<BrowserTabStateEntry>.Empty) },
            { "tabs-no-active", new BrowserTabStateMessage(1, 1, 0, ValueList<BrowserTabStateEntry>.From([validTab])) },
            { "active-not-present", new BrowserTabStateMessage(1, 1, 2, ValueList<BrowserTabStateEntry>.From([validTab])) },
            { "duplicate-tabs", new BrowserTabStateMessage(1, 1, 1, ValueList<BrowserTabStateEntry>.From([validTab, validTab])) },
            { "tab-progress", new BrowserTabStateMessage(1, 1, 1, ValueList<BrowserTabStateEntry>.From([validTab with { Progress = 101 }])) },
            { "tab-progress-negative", new BrowserTabStateMessage(1, 1, 1, ValueList<BrowserTabStateEntry>.From([validTab with { Progress = -1 }])) },
            { "tab-favicon", new BrowserTabStateMessage(1, 1, 1, ValueList<BrowserTabStateEntry>.From([validTab with { FaviconId = -1 }])) },
            { "tab-active-url", new BrowserTabStateMessage(1, 1, 1, ValueList<BrowserTabStateEntry>.From([validTab with { Url = "" }])) },
            { "tab-load-enum", new BrowserTabStateMessage(1, 1, 1, ValueList<BrowserTabStateEntry>.From([validTab with { LoadState = (BrowserLoadState)0 }])) },
            { "tab-null", new BrowserTabStateMessage(1, 1, 1, ValueList<BrowserTabStateEntry>.From([null!])) },
            { "view-action-enum", new BrowserViewCommandMessage(1, 1, (BrowserViewAction)0) },
            { "zoom-low", new BrowserViewCommandMessage(1, 1, BrowserViewAction.SetZoom, BrowserWireLimits.MinZoomPercent - 1) },
            { "zoom-high", new BrowserViewCommandMessage(1, 1, BrowserViewAction.SetZoom, BrowserWireLimits.MaxZoomPercent + 1) },
            { "zoom-text", new BrowserViewCommandMessage(1, 1, BrowserViewAction.SetZoom, 100, "text") },
            { "ua-value", new BrowserViewCommandMessage(1, 1, BrowserViewAction.SetUa, 0) },
            { "ua-value-negative", new BrowserViewCommandMessage(1, 1, BrowserViewAction.SetUa, -1) },
            { "ua-value-too-large", new BrowserViewCommandMessage(1, 1, BrowserViewAction.SetUa, 256) },
            { "dark-value", new BrowserViewCommandMessage(1, 1, BrowserViewAction.SetDark, 4) },
            { "input-value", new BrowserViewCommandMessage(1, 1, BrowserViewAction.SetInputMode, 3) },
            { "fullscreen-value", new BrowserViewCommandMessage(1, 1, BrowserViewAction.SetFullscreen, 2) },
            { "find-start-value", new BrowserViewCommandMessage(1, 1, BrowserViewAction.FindStart, 1, "needle") },
            { "find-start-blank", new BrowserViewCommandMessage(1, 1, BrowserViewAction.FindStart) },
            { "find-start-long", new BrowserViewCommandMessage(1, 1, BrowserViewAction.FindStart, 0, new string('a', BrowserWireLimits.MaxFindTextBytes + 1)) },
            { "find-next-text", new BrowserViewCommandMessage(1, 1, BrowserViewAction.FindNext, 0, "needle") },
            { "find-next-value", new BrowserViewCommandMessage(1, 1, BrowserViewAction.FindNext, 1) },
            { "search-enum", new BrowserViewCommandMessage(1, 1, BrowserViewAction.SetSearchEngine, 0) },
            { "search-custom-blank", new BrowserViewCommandMessage(1, 1, BrowserViewAction.SetSearchEngine, (int)BrowserSearchEngine.Custom) },
            { "search-preset-text", new BrowserViewCommandMessage(1, 1, BrowserViewAction.SetSearchEngine, (int)BrowserSearchEngine.Google, "template") },
            { "view-state-zoom", ValidViewState() with { ZoomPercent = 49 } },
            { "view-state-UA", ValidViewState() with { UaMode = (BrowserUserAgentMode)0 } },
            { "view-state-dark", ValidViewState() with { DarkMode = (BrowserDarkMode)0 } },
            { "view-state-input", ValidViewState() with { InputMode = (BrowserInteractionMode)0 } },
            { "view-state-search", ValidViewState() with { SearchEngine = (BrowserSearchEngine)0 } },
            { "view-state-negative-find", ValidViewState() with { FindCurrent = -1 } },
            { "view-state-inactive-find", ValidViewState() with { FindCurrent = 1, FindTotal = 1 } },
            { "view-state-active-find-current", ValidViewState() with { FindActive = true, FindCurrent = 2, FindTotal = 1 } },
            { "view-state-active-find-zero-current", ValidViewState() with { FindActive = true, FindCurrent = 0, FindTotal = 1 } },
            { "view-state-active-find-zero-total", ValidViewState() with { FindActive = true, FindCurrent = 1, FindTotal = 0 } },
            { "favicon-id", new BrowserFaviconMessage(1, 0, 1, 1, BinaryData.From([1])) },
            { "favicon-width-zero", new BrowserFaviconMessage(1, 1, 0, 1, BinaryData.From([1])) },
            { "favicon-width-high", new BrowserFaviconMessage(1, 1, BrowserWireLimits.MaxFaviconDimension + 1, 1, BinaryData.From([1])) },
            { "favicon-height-zero", new BrowserFaviconMessage(1, 1, 1, 0, BinaryData.From([1])) },
            { "favicon-height-high", new BrowserFaviconMessage(1, 1, 1, BrowserWireLimits.MaxFaviconDimension + 1, BinaryData.From([1])) },
            { "favicon-empty", new BrowserFaviconMessage(1, 1, 1, 1, BinaryData.Empty) },
            { "favicon-long", new BrowserFaviconMessage(1, 1, 1, 1, BinaryData.From(new byte[BrowserWireLimits.MaxFaviconBytes + 1])) },
            { "library-action-enum", new BrowserLibraryCommandMessage(1, 1, (BrowserLibraryAction)0) },
            { "add-url", new BrowserLibraryCommandMessage(1, 1, BrowserLibraryAction.AddBookmark) },
            { "remove-title", new BrowserLibraryCommandMessage(1, 1, BrowserLibraryAction.RemoveBookmark, "https://example.test", "title") },
            { "clear-text", new BrowserLibraryCommandMessage(1, 1, BrowserLibraryAction.ClearHistory, "https://example.test") },
            { "too-many-bookmarks", new BrowserLibraryStateMessage(1, 1, RepeatedEntries(BrowserLibraryEntryKind.Bookmark, BrowserWireLimits.MaxBookmarks + 1), ValueList<BrowserLibraryEntry>.Empty) },
            { "too-many-history", new BrowserLibraryStateMessage(1, 1, ValueList<BrowserLibraryEntry>.Empty, RepeatedEntries(BrowserLibraryEntryKind.History, BrowserWireLimits.MaxHistoryEntries + 1)) },
            { "bookmark-kind", new BrowserLibraryStateMessage(1, 1, ValueList<BrowserLibraryEntry>.From([history]), ValueList<BrowserLibraryEntry>.Empty) },
            { "history-kind", new BrowserLibraryStateMessage(1, 1, ValueList<BrowserLibraryEntry>.Empty, ValueList<BrowserLibraryEntry>.From([bookmark])) },
            { "library-entry-favicon", new BrowserLibraryStateMessage(1, 1, ValueList<BrowserLibraryEntry>.From([bookmark with { FaviconId = -1 }]), ValueList<BrowserLibraryEntry>.Empty) },
            { "library-entry-time", new BrowserLibraryStateMessage(1, 1, ValueList<BrowserLibraryEntry>.From([bookmark with { LastVisitedMilliseconds = -1 }]), ValueList<BrowserLibraryEntry>.Empty) },
            { "library-entry-url", new BrowserLibraryStateMessage(1, 1, ValueList<BrowserLibraryEntry>.From([bookmark with { Url = "" }]), ValueList<BrowserLibraryEntry>.Empty) },
            { "library-entry-null", new BrowserLibraryStateMessage(1, 1, ValueList<BrowserLibraryEntry>.From([null!]), ValueList<BrowserLibraryEntry>.Empty) },
        };
    }

    public static TheoryData<string, byte[]> UnknownEnumPayloads()
    {
        var tabCommand = WireCodec.Encode(new WireFrame(
            new BrowserTabCommandMessage(1, 1, BrowserTabAction.Close, 1)));
        tabCommand[12 + 16] = 0;

        var tabState = WireCodec.Encode(new WireFrame(new BrowserTabStateMessage(
            1, 1, 1, ValueList<BrowserTabStateEntry>.From([ValidTab()]))));
        tabState[12 + 24 + 1 + 8] = 0;

        var viewCommand = WireCodec.Encode(new WireFrame(
            new BrowserViewCommandMessage(1, 1, BrowserViewAction.SetZoom, 100)));
        viewCommand[12 + 16] = 0;

        var viewStateUa = WireCodec.Encode(new WireFrame(ValidViewState()));
        viewStateUa[12 + 20] = 0;
        var viewStateDark = WireCodec.Encode(new WireFrame(ValidViewState()));
        viewStateDark[12 + 21] = 0;
        var viewStateInput = WireCodec.Encode(new WireFrame(ValidViewState()));
        viewStateInput[12 + 22] = 0;
        var viewStateSearch = WireCodec.Encode(new WireFrame(ValidViewState()));
        viewStateSearch[^1] = 0;

        var libraryCommand = WireCodec.Encode(new WireFrame(
            new BrowserLibraryCommandMessage(1, 1, BrowserLibraryAction.RequestSnapshot)));
        libraryCommand[12 + 16] = 0;

        var libraryState = WireCodec.Encode(new WireFrame(new BrowserLibraryStateMessage(
            1,
            1,
            ValueList<BrowserLibraryEntry>.From([ValidLibraryEntry(BrowserLibraryEntryKind.Bookmark)]),
            ValueList<BrowserLibraryEntry>.Empty)));
        libraryState[12 + 18] = 0;

        return new TheoryData<string, byte[]>
        {
            { "tab-command-action", tabCommand },
            { "tab-load-state", tabState },
            { "view-command-action", viewCommand },
            { "view-state-UA", viewStateUa },
            { "view-state-dark", viewStateDark },
            { "view-state-input", viewStateInput },
            { "view-state-search", viewStateSearch },
            { "library-command-action", libraryCommand },
            { "library-entry-kind", libraryState },
        };
    }

    private static BrowserTabStateEntry ValidTab(long tabId = 1) => new(
        tabId,
        BrowserLoadState.Loaded,
        100,
        CanGoBack: false,
        CanGoForward: false,
        Frozen: false,
        FaviconId: 0,
        "https://example.test",
        "Example");

    private static ValueList<BrowserTabStateEntry> RepeatedTabs(int count) =>
        ValueList<BrowserTabStateEntry>.From(Enumerable.Range(1, count).Select(index => ValidTab(index)));

    private static BrowserViewStateMessage ValidViewState() => new(
        1,
        1,
        100,
        BrowserUserAgentMode.Tv,
        BrowserDarkMode.FollowSystem,
        BrowserInteractionMode.Cursor,
        Fullscreen: false,
        MediaPlaying: false,
        EditingFocused: false,
        FindActive: false,
        FindCurrent: 0,
        FindTotal: 0,
        BrowserSearchEngine.DuckDuckGo);

    private static BrowserLibraryEntry ValidLibraryEntry(BrowserLibraryEntryKind kind) => new(
        kind,
        FaviconId: 0,
        LastVisitedMilliseconds: 0,
        "https://example.test",
        "Example");

    private static ValueList<BrowserLibraryEntry> RepeatedEntries(BrowserLibraryEntryKind kind, int count) =>
        ValueList<BrowserLibraryEntry>.From(Enumerable.Repeat(ValidLibraryEntry(kind), count));
}
