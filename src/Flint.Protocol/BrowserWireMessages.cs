namespace Flint.Protocol;

/// <summary>Hard limits for v2 browser values before any receiver UI is involved.</summary>
/// <remarks>
/// These caps are part of the wire contract. They intentionally constrain data before it reaches
/// WebView, an image decoder, a log, or a UI collection. They are not presentation preferences.
/// </remarks>
public static class BrowserWireLimits
{
    /// <summary>Maximum UTF-8 bytes in a navigable URL.</summary>
    public const int MaxUrlBytes = 4 * 1024;

    /// <summary>Maximum UTF-8 bytes in an explicit text-input or prompt-reply value.</summary>
    public const int MaxTextBytes = 4 * 1024;

    /// <summary>Maximum UTF-8 bytes in a safe page title.</summary>
    public const int MaxTitleBytes = 512;

    /// <summary>Maximum UTF-8 bytes in a safe, non-secret error/detail value.</summary>
    public const int MaxDetailBytes = 1024;

    /// <summary>Maximum UTF-8 bytes in a top-level dialog origin.</summary>
    public const int MaxOriginBytes = 2 * 1024;

    /// <summary>Maximum UTF-8 bytes in a dialog message/default value.</summary>
    public const int MaxDialogBytes = 4 * 1024;

    /// <summary>Largest permitted encoded JPEG preview payload.</summary>
    public const int MaxPreviewBytes = 768 * 1024;

    /// <summary>Maximum preview width advertised or accepted by the protocol.</summary>
    public const int MaxPreviewWidth = 960;

    /// <summary>Maximum preview height advertised or accepted by the protocol.</summary>
    public const int MaxPreviewHeight = 540;

    /// <summary>Maximum requested interactive preview rate.</summary>
    public const int MaxInteractivePreviewFramesPerSecond = 5;

    /// <summary>Maximum requested idle preview rate.</summary>
    public const int MaxIdlePreviewFramesPerSecond = 1;

    /// <summary>Maximum lifetime of a native dialog before it expires locally.</summary>
    public const int MaxDialogTimeoutMilliseconds = 60_000;

    /// <summary>Maximum number of tabs carried by one tab-state snapshot.</summary>
    public const int MaxTabs = 8;

    /// <summary>Largest permitted encoded PNG favicon payload.</summary>
    public const int MaxFaviconBytes = 16 * 1024;

    /// <summary>Maximum favicon width or height.</summary>
    public const int MaxFaviconDimension = 64;

    /// <summary>Maximum UTF-8 bytes in a find-in-page query.</summary>
    public const int MaxFindTextBytes = 512;

    /// <summary>Smallest browser text zoom accepted by the protocol.</summary>
    public const int MinZoomPercent = 50;

    /// <summary>Largest browser text zoom accepted by the protocol.</summary>
    public const int MaxZoomPercent = 200;

    /// <summary>Maximum bookmarks in one library snapshot; the count is a wire byte.</summary>
    public const int MaxBookmarks = byte.MaxValue;

    /// <summary>Maximum history rows in one library snapshot; the count is a wire byte.</summary>
    public const int MaxHistoryEntries = byte.MaxValue;

    /// <summary>Maximum UTF-8 bytes in a stable TV browser-profile identifier.</summary>
    public const int MaxProfileIdBytes = 64;

    /// <summary>Maximum UTF-8 bytes in a user-visible TV browser-profile name.</summary>
    public const int MaxProfileNameBytes = 64;

    /// <summary>Maximum UTF-8 bytes in the connected device display name.</summary>
    public const int MaxDeviceProfileNameBytes = 64;

    /// <summary>Maximum TV-resident profiles in one bounded catalog.</summary>
    public const int MaxTvProfiles = 8;

    /// <summary>Maximum UTF-8 bytes in a WireGuard config text blob.</summary>
    public const int MaxConfigUtf8Bytes = 64 * 1024;

    /// <summary>Maximum independently browsing panes in one workspace snapshot.</summary>
    public const int MaxWorkspacePanes = 8;
}

/// <summary>Receiver browser capability state, sent only after secure-session authentication.</summary>
public enum BrowserCapabilityStatus
{
    /// <summary>The receiver can host the constrained browser feature.</summary>
    Available = 1,

    /// <summary>The secure endpoint is absent or has not been paired.</summary>
    SecureEndpointUnavailable = 2,

    /// <summary>The receiver cannot create a compatible WebView.</summary>
    WebViewUnavailable = 3,

    /// <summary>The device/API baseline cannot satisfy the browser security profile.</summary>
    UnsupportedPlatform = 4,

    /// <summary>The build/channel does not permit browser use.</summary>
    DistributionRestricted = 5,
}

/// <summary>A receiver diagnostic snapshot for the secure browser capability.</summary>
/// <remarks>
/// This message does not carry a certificate, a token, cookies, a URL, or an endpoint secret.
/// The port is discovery metadata and is validated again by the TLS handshake/pin in Slice 03.
/// </remarks>
public sealed record BrowserCapabilityMessage(
    BrowserCapabilityStatus Status,
    int SecureEndpointPort,
    int ApiLevel,
    string WebViewVersion,
    bool PreviewSupported,
    int PreviewMaxWidth,
    int PreviewMaxHeight,
    int InteractivePreviewFramesPerSecond,
    int IdlePreviewFramesPerSecond,
    int PreviewMaxBytes,
    string Detail = "") : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.BrowserCapability;
}

/// <summary>Browser navigation/lifecycle command kinds.</summary>
public enum BrowserCommandAction
{
    /// <summary>Begin a new browser epoch at the supplied HTTPS URL.</summary>
    Open = 1,

    /// <summary>Navigate within the current browser epoch to the supplied HTTPS URL.</summary>
    Navigate = 2,

    /// <summary>Move to the previous history entry.</summary>
    Back = 3,

    /// <summary>Move to the next history entry.</summary>
    Forward = 4,

    /// <summary>Reload the current page.</summary>
    Reload = 5,

    /// <summary>Stop the current load.</summary>
    Stop = 6,

    /// <summary>Close the browser surface and release browser state.</summary>
    Close = 7,

    /// <summary>Enable or disable the passive preview feature for this secure session.</summary>
    SetPreviewEnabled = 8,

    /// <summary>Clear bounded browser data only after a separately confirmed native dialog.</summary>
    ClearData = 9,
}

/// <summary>A host-owned, ordered browser command.</summary>
/// <param name="Epoch">The positive browser epoch this command belongs to.</param>
/// <param name="CommandId">The positive, host-owned monotonically increasing command identifier.</param>
/// <param name="Action">The command kind.</param>
/// <param name="Url">Present only for <see cref="BrowserCommandAction.Open"/> and Navigate.</param>
/// <param name="PreviewEnabled">Present only for <see cref="BrowserCommandAction.SetPreviewEnabled"/>.</param>
public sealed record BrowserCommandMessage(
    long Epoch,
    long CommandId,
    BrowserCommandAction Action,
    string? Url = null,
    bool? PreviewEnabled = null) : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.BrowserCommand;
}

/// <summary>Pointer action values deliberately independent of Android/Windows input constants.</summary>
public enum BrowserPointerAction
{
    /// <summary>The pointer moved without a button transition.</summary>
    Move = 1,

    /// <summary>A pointer button went down.</summary>
    Down = 2,

    /// <summary>A pointer button went up.</summary>
    Up = 3,

    /// <summary>Cancel an active primary-pointer gesture without producing a click.</summary>
    Cancel = 4,
}

/// <summary>Portable keys accepted by the browser input contract.</summary>
public enum BrowserSemanticKey
{
    /// <summary>Directional up.</summary>
    Up = 1,

    /// <summary>Directional down.</summary>
    Down = 2,

    /// <summary>Directional left.</summary>
    Left = 3,

    /// <summary>Directional right.</summary>
    Right = 4,

    /// <summary>Primary/select action.</summary>
    Select = 5,

    /// <summary>Navigate back.</summary>
    Back = 6,

    /// <summary>Move forward through focus.</summary>
    Tab = 7,

    /// <summary>Move backward through focus.</summary>
    ShiftTab = 8,

    /// <summary>Dismiss/cancel.</summary>
    Escape = 9,

    /// <summary>Page upward.</summary>
    PageUp = 10,

    /// <summary>Page downward.</summary>
    PageDown = 11,

    /// <summary>Move to start.</summary>
    Home = 12,

    /// <summary>Move to end.</summary>
    End = 13,

    /// <summary>Refresh the current page.</summary>
    Refresh = 14,
}

/// <summary>One browser input variant.</summary>
public abstract record BrowserInputEvent
{
    /// <summary>The stable union tag on the wire.</summary>
    public abstract int EventId { get; }
}

/// <summary>
/// Pointer input tied to the visual reference from which the user acted.
/// </summary>
/// <remarks>
/// Coordinates are unsigned-16 fixed normalized values: 0 is the left/top edge and 65,535 is the
/// right/bottom edge. A receiver must reject references to a stale navigation/frame instead of
/// applying a click to a different page.
/// </remarks>
public sealed record BrowserPointerInput(
    BrowserPointerAction Action,
    long NavigationId,
    long FrameId,
    int X,
    int Y,
    int Buttons = 0) : BrowserInputEvent
{
    /// <inheritdoc />
    public override int EventId => 1;
}

/// <summary>Scroll input tied to the visual reference from which the user acted.</summary>
public sealed record BrowserScrollInput(
    long NavigationId,
    long FrameId,
    int X,
    int Y,
    int DeltaX,
    int DeltaY) : BrowserInputEvent
{
    /// <inheritdoc />
    public override int EventId => 2;
}

/// <summary>A semantic key without a host-specific virtual/scancode.</summary>
public sealed record BrowserSemanticKeyInput(BrowserSemanticKey Key) : BrowserInputEvent
{
    /// <inheritdoc />
    public override int EventId => 3;
}

/// <summary>Explicit text input; never echoed in state, telemetry, or diagnostics.</summary>
public sealed record BrowserTextInput(string Text) : BrowserInputEvent
{
    /// <inheritdoc />
    public override int EventId => 4;
}

/// <summary>A host-owned, ordered browser input event.</summary>
public sealed record BrowserInputMessage(long Epoch, long Sequence, BrowserInputEvent Event) : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.BrowserInput;
}

/// <summary>Safe high-level page state, never DOM/cookies/headers/form values/source.</summary>
public enum BrowserLoadState
{
    /// <summary>No browser page is active.</summary>
    Idle = 1,

    /// <summary>The receiver is loading a page.</summary>
    Loading = 2,

    /// <summary>A page completed successfully.</summary>
    Loaded = 3,

    /// <summary>The receiver reported a safe, bounded loading failure.</summary>
    Failed = 4,

    /// <summary>The browser surface is closing or has been closed.</summary>
    Closed = 5,
}

/// <summary>Passive preview availability for the current secure browser session.</summary>
public enum BrowserPreviewState
{
    /// <summary>The user has not opted in or disabled preview.</summary>
    Disabled = 1,

    /// <summary>Preview is enabled under its bounded policy.</summary>
    Enabled = 2,

    /// <summary>The receiver cannot capture the WebView safely.</summary>
    Unavailable = 3,
}

/// <summary>A bounded, safe page-status snapshot from receiver to host.</summary>
public sealed record BrowserStateMessage(
    long Epoch,
    long Revision,
    long NavigationId,
    long LastAcceptedCommandId,
    long LastAcceptedInputSequence,
    BrowserLoadState LoadState,
    string Url,
    string Title,
    int Progress,
    bool CanGoBack,
    bool CanGoForward,
    int ViewportWidth,
    int ViewportHeight,
    BrowserPreviewState PreviewState,
    string ErrorDetail = "") : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.BrowserState;
}

/// <summary>A latest-only, bounded JPEG preview sent from the receiver to the host.</summary>
public sealed record BrowserPreviewMessage(
    long Epoch,
    long NavigationId,
    long FrameId,
    int Width,
    int Height,
    BinaryData Jpeg) : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.BrowserPreview;
}

/// <summary>Native browser-dialog types that the remote may observe and explicitly answer.</summary>
public enum BrowserDialogType
{
    /// <summary>An informational JavaScript alert.</summary>
    Alert = 1,

    /// <summary>A JavaScript confirm request.</summary>
    Confirm = 2,

    /// <summary>A JavaScript prompt request.</summary>
    Prompt = 3,

    /// <summary>A before-unload confirmation.</summary>
    BeforeUnload = 4,
}

/// <summary>An expiring, native receiver dialog observation.</summary>
public sealed record BrowserDialogMessage(
    long Epoch,
    long DialogId,
    BrowserDialogType Type,
    string Origin,
    string Message,
    string DefaultValue,
    int TimeoutMilliseconds) : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.BrowserDialog;
}

/// <summary>An explicit answer to the current native browser dialog.</summary>
public sealed record BrowserDialogReplyMessage(
    long Epoch,
    long DialogId,
    bool Accepted,
    string? PromptText = null) : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.BrowserDialogReply;
}

/// <summary>Protocol-level browser security and compatibility predicates.</summary>
public static class BrowserWireRules
{
    /// <summary>Whether a message is reserved for the authenticated TLS BrowserSession.</summary>
    public static bool IsBrowserMessage(WireMessage message) => message is BrowserCapabilityMessage
        or BrowserCommandMessage
        or BrowserInputMessage
        or BrowserStateMessage
        or BrowserPreviewMessage
        or BrowserDialogMessage
        or BrowserDialogReplyMessage
        or BrowserTabCommandMessage
        or BrowserTabStateMessage
        or BrowserViewCommandMessage
        or BrowserViewStateMessage
        or BrowserFaviconMessage
        or BrowserLibraryCommandMessage
        or BrowserLibraryStateMessage
        or BrowserProfileCommandMessage
        or BrowserProfileStateMessage
        or BrowserNetworkCommandMessage
        or BrowserNetworkStateMessage
        or BrowserWorkspaceCommandMessage
        or BrowserWorkspaceStateMessage
        or BrowserWorkspaceInputMessage or BrowserWorkspaceResizeMessage or BrowserWorkspaceGeometryMessage;

    /// <summary>Whether a message belongs to the v3 browser workspace family.</summary>
    public static bool IsWorkspaceMessage(WireMessage message) => message is BrowserWorkspaceCommandMessage
        or BrowserWorkspaceStateMessage
        or BrowserWorkspaceInputMessage or BrowserWorkspaceResizeMessage or BrowserWorkspaceGeometryMessage;

    /// <summary>Whether a message-type number is one of the v2 browser types.</summary>
    public static bool IsBrowserType(int typeId) => typeId is >= (int)WireMessageType.BrowserCapability
        and <= (int)WireMessageType.BrowserWorkspaceGeometry;

    /// <summary>Whether a value is forbidden on the legacy CastSession/ReceiverServer route.</summary>
    public static bool IsForbiddenOnOrdinaryChannel(WireMessage message) => IsBrowserMessage(message)
        || message is SurfaceMessage { Mode: SurfaceMode.Browser };

    /// <summary>Whether a known message is legal at its declared payload version.</summary>
    public static bool IsAllowedAtVersion(int protocolVersion, WireMessage message)
    {
        if (message is BrowserWorkspaceResizeMessage or BrowserWorkspaceGeometryMessage && protocolVersion < 4) return false;
        if (IsWorkspaceMessage(message) && protocolVersion < 3)
        {
            return false;
        }

        return protocolVersion >= 2
            || (!IsBrowserMessage(message) && message is not SurfaceMessage { Mode: SurfaceMode.Browser });
    }
}
