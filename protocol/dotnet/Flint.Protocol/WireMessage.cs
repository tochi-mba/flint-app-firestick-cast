namespace Flint.Protocol;

// The whole message hierarchy lives in one file, against this repository's usual one-type-per-file
// habit. It mirrors WireMessages.kt in REX Cast, and these two files must be diffed against each
// other whenever the protocol changes; splitting them apart would make that review harder, not
// easier. The set is closed and small, and it changes as a unit or not at all.

/// <summary>Message type tags, matching the Kotlin and Rust implementations.</summary>
public enum WireMessageType
{
    /// <summary>Capability and version exchange.</summary>
    Hello = 1,

    /// <summary>Pairing code, session token, or public-key proof.</summary>
    Auth = 2,

    /// <summary>Codec, dimensions and codec-specific data.</summary>
    VideoConfig = 3,

    /// <summary>One video access unit.</summary>
    Video = 4,

    /// <summary>Audio codec, sample rate and channel count.</summary>
    AudioConfig = 5,

    /// <summary>One audio packet.</summary>
    Audio = 6,

    /// <summary>An input or transport event from the receiver.</summary>
    Control = 7,

    /// <summary>Receiver-side timing and loss counters.</summary>
    Stats = 8,

    /// <summary>Ordered shutdown.</summary>
    Bye = 9,

    /// <summary>Receiver-side local media playback command.</summary>
    MediaCommand = 10,

    /// <summary>Receiver render-surface selection.</summary>
    Surface = 11,

    /// <summary>Receiver media-player state report.</summary>
    PlaybackState = 12,

    /// <summary>One chunk of a media file pushed over the control connection.</summary>
    MediaData = 13,

    /// <summary>Secure receiver browser capability state.</summary>
    BrowserCapability = 14,

    /// <summary>Secure host-to-receiver browser command.</summary>
    BrowserCommand = 15,

    /// <summary>Secure host-to-receiver portable browser input.</summary>
    BrowserInput = 16,

    /// <summary>Secure receiver-to-host safe browser state.</summary>
    BrowserState = 17,

    /// <summary>Secure receiver-to-host latest-only JPEG browser preview.</summary>
    BrowserPreview = 18,

    /// <summary>Secure receiver-to-host native browser dialog observation.</summary>
    BrowserDialog = 19,

    /// <summary>Secure host-to-receiver native browser dialog reply.</summary>
    BrowserDialogReply = 20,

    /// <summary>Secure host-to-receiver browser tab operation.</summary>
    BrowserTabCommand = 21,

    /// <summary>Secure receiver-to-host bounded browser tab snapshot.</summary>
    BrowserTabState = 22,

    /// <summary>Secure host-to-receiver browser view-setting operation.</summary>
    BrowserViewCommand = 23,

    /// <summary>Secure receiver-to-host browser view-setting snapshot.</summary>
    BrowserViewState = 24,

    /// <summary>Secure receiver-to-host bounded PNG favicon.</summary>
    BrowserFavicon = 25,

    /// <summary>Secure host-to-receiver browser library operation.</summary>
    BrowserLibraryCommand = 26,

    /// <summary>Secure receiver-to-host bounded browser library snapshot.</summary>
    BrowserLibraryState = 27,

    /// <summary>Secure browser profile selection or TV profile-management command.</summary>
    BrowserProfileCommand = 28,

    /// <summary>Secure bounded snapshot of the active browser profile source and TV profiles.</summary>
    BrowserProfileState = 29,

    /// <summary>Secure host-to-receiver browser network/VPN settings command.</summary>
    BrowserNetworkCommand = 30,

    /// <summary>Secure receiver-to-host browser network/VPN settings snapshot.</summary>
    BrowserNetworkState = 31,

    /// <summary>Secure host-to-receiver browser workspace operation.</summary>
    BrowserWorkspaceCommand = 32,

    /// <summary>Secure receiver-to-host browser workspace snapshot.</summary>
    BrowserWorkspaceState = 33,

    /// <summary>Secure host-to-receiver browser workspace pane input.</summary>
    BrowserWorkspaceInput = 34,
    /// <summary>Version-four workspace divider command.</summary>
    BrowserWorkspaceResize = 35,
    /// <summary>Version-four authoritative workspace divider state.</summary>
    BrowserWorkspaceGeometry = 36,
}

/// <summary>How a client proves it may open a session.</summary>
public enum AuthMethod
{
    /// <summary>A six-digit code the user reads off the television.</summary>
    PairingCode = 1,

    /// <summary>A token issued by a previous successful pairing.</summary>
    SessionToken = 2,

    /// <summary>A signature over a challenge, proving possession of a known key.</summary>
    PublicKeyProof = 3,
}

/// <summary>Playback transport actions.</summary>
public enum TransportAction
{
    /// <summary>Resume.</summary>
    Play = 1,

    /// <summary>Pause.</summary>
    Pause = 2,

    /// <summary>Stop and release.</summary>
    Stop = 3,

    /// <summary>Seek to an absolute position.</summary>
    SeekTo = 4,

    /// <summary>Next item in the queue.</summary>
    Next = 5,

    /// <summary>Previous item in the queue.</summary>
    Previous = 6,
}

/// <summary>Pointer event kinds.</summary>
public enum PointerAction
{
    /// <summary>The pointer moved.</summary>
    Move = 1,

    /// <summary>A button went down.</summary>
    Down = 2,

    /// <summary>A button came up.</summary>
    Up = 3,

    /// <summary>A scroll wheel turned.</summary>
    Scroll = 4,
}

/// <summary>Key event kinds.</summary>
public enum KeyAction
{
    /// <summary>The key went down.</summary>
    Down = 1,

    /// <summary>The key came up.</summary>
    Up = 2,
}

/// <summary>Why a session is closing.</summary>
public enum ByeReason
{
    /// <summary>The user or the application ended it.</summary>
    Normal = 1,

    /// <summary>Credentials were rejected.</summary>
    AuthenticationFailed = 2,

    /// <summary>The version ranges did not overlap.</summary>
    UnsupportedVersion = 3,

    /// <summary>A peer sent something unreadable.</summary>
    ProtocolError = 4,

    /// <summary>The receiver shut down.</summary>
    ReceiverStopped = 5,
}

/// <summary>Receiver local-media actions.</summary>
public enum MediaAction
{
    /// <summary>Load and play the supplied source.</summary>
    Load = 1,

    /// <summary>Return the receiver to its idle surface.</summary>
    Clear = 2,
}

/// <summary>The receiver surface to show.</summary>
public enum SurfaceMode
{
    /// <summary>Receiver idle surface.</summary>
    Idle = 1,

    /// <summary>Receiver media-player surface.</summary>
    Player = 2,

    /// <summary>Receiver live mirror surface.</summary>
    Mirror = 3,

    /// <summary>Receiver presentation surface.</summary>
    Presentation = 4,

    /// <summary>Receiver-local browser surface. Invalid on v1 and every ordinary CastSession.</summary>
    Browser = 5,
}

/// <summary>Receiver playback state.</summary>
public enum PlaybackState
{
    /// <summary>No source is loaded.</summary>
    Idle = 1,

    /// <summary>The receiver is loading media.</summary>
    Buffering = 2,

    /// <summary>The receiver is presenting media.</summary>
    Playing = 3,

    /// <summary>Playback is paused.</summary>
    Paused = 4,

    /// <summary>The source reached its end.</summary>
    Ended = 5,

    /// <summary>The receiver could not load or play the source.</summary>
    Error = 6,
}

/// <summary>One control event.</summary>
public abstract record ControlEvent
{
    /// <summary>The wire tag for this event's variant.</summary>
    public abstract int EventId { get; }
}

/// <summary>A transport command.</summary>
/// <param name="Action">What to do.</param>
/// <param name="PositionMs">Absolute position for <see cref="TransportAction.SeekTo"/>; -1 otherwise.</param>
public sealed record TransportControl(TransportAction Action, long PositionMs = -1) : ControlEvent
{
    /// <inheritdoc />
    public override int EventId => 1;
}

/// <summary>A pointer event, in normalised surface coordinates.</summary>
/// <param name="Action">What happened.</param>
/// <param name="X">Horizontal position.</param>
/// <param name="Y">Vertical position.</param>
/// <param name="Buttons">Button bitmask.</param>
public sealed record PointerControl(PointerAction Action, float X, float Y, int Buttons = 0) : ControlEvent
{
    /// <inheritdoc />
    public override int EventId => 2;
}

/// <summary>A key event.</summary>
/// <param name="Action">Down or up.</param>
/// <param name="KeyCode">Platform key code.</param>
public sealed record KeyControl(KeyAction Action, int KeyCode) : ControlEvent
{
    /// <inheritdoc />
    public override int EventId => 3;
}

/// <summary>Text input, for on-screen fields.</summary>
/// <param name="Text">The text.</param>
public sealed record TextControl(string Text) : ControlEvent
{
    /// <inheritdoc />
    public override int EventId => 4;
}

/// <summary>Volume, 0.0 to 1.0.</summary>
/// <param name="Level">The level.</param>
public sealed record VolumeControl(float Level) : ControlEvent
{
    /// <inheritdoc />
    public override int EventId => 5;
}

/// <summary>One protocol message.</summary>
public abstract record WireMessage
{
    /// <summary>The wire tag for this message.</summary>
    public abstract int TypeId { get; }
}

/// <summary>Capability and version exchange.</summary>
/// <param name="MinimumVersion">Oldest payload version the sender accepts.</param>
/// <param name="MaximumVersion">Newest payload version the sender accepts.</param>
/// <param name="DeviceName">A human-readable device name.</param>
/// <param name="CodecCapabilities">Codecs the sender can handle. Encoded in ascending numeric order.</param>
/// <param name="ScreenWidth">Surface width in pixels.</param>
/// <param name="ScreenHeight">Surface height in pixels.</param>
/// <param name="DensityDpi">Surface density.</param>
public sealed record HelloMessage(
    int MinimumVersion,
    int MaximumVersion,
    string DeviceName,
    ValueList<CodecId> CodecCapabilities,
    int ScreenWidth,
    int ScreenHeight,
    int DensityDpi) : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.Hello;

    /// <summary>
    /// The codec list as it appears on the wire: ascending and deduplicated.
    /// </summary>
    /// <remarks>
    /// Normalising here is what makes the same capability set always produce the same bytes, which
    /// is what lets the golden vectors be stable across three languages.
    /// </remarks>
    public ValueList<CodecId> NormalisedCodecs =>
        ValueList<CodecId>.From(
            CodecCapabilities.Select(codec => codec.Value).Distinct().Order().Select(value => new CodecId(value)));
}

/// <summary>Authorization material.</summary>
/// <param name="Method">How the credential should be interpreted.</param>
/// <param name="Credential">The credential itself. Never empty.</param>
/// <param name="PublicKeyFingerprint">An optional public-key fingerprint.</param>
public sealed record AuthMessage(
    AuthMethod Method,
    BinaryData Credential,
    string? PublicKeyFingerprint = null) : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.Auth;
}

/// <summary>Video stream configuration.</summary>
/// <param name="Codec">The codec that will follow.</param>
/// <param name="Width">Frame width.</param>
/// <param name="Height">Frame height.</param>
/// <param name="CodecSpecificData">Codec-specific data, such as SPS and PPS.</param>
public sealed record VideoConfigMessage(
    CodecId Codec,
    int Width,
    int Height,
    ValueList<BinaryData> CodecSpecificData) : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.VideoConfig;
}

/// <summary>One video access unit.</summary>
/// <param name="PresentationTimeUs">Presentation timestamp, microseconds.</param>
/// <param name="KeyFrame">Whether this unit can be decoded without any earlier one.</param>
/// <param name="Data">The access unit.</param>
public sealed record VideoPacket(long PresentationTimeUs, bool KeyFrame, BinaryData Data) : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.Video;
}

/// <summary>Audio stream configuration.</summary>
/// <param name="Codec">The codec that will follow.</param>
/// <param name="SampleRateHz">Sample rate in hertz.</param>
/// <param name="ChannelCount">Channel count.</param>
/// <param name="CodecSpecificData">Codec-specific data.</param>
public sealed record AudioConfigMessage(
    CodecId Codec,
    int SampleRateHz,
    int ChannelCount,
    BinaryData CodecSpecificData) : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.AudioConfig;
}

/// <summary>One audio packet.</summary>
/// <param name="PresentationTimeUs">Presentation timestamp, microseconds.</param>
/// <param name="Data">The packet.</param>
public sealed record AudioPacket(long PresentationTimeUs, BinaryData Data) : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.Audio;
}

/// <summary>An input or transport event.</summary>
/// <param name="SequenceNumber">Monotonic sequence number, so a receiver can detect a gap.</param>
/// <param name="Event">What happened.</param>
public sealed record ControlMessage(long SequenceNumber, ControlEvent Event) : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.Control;
}

/// <summary>Receiver-side timing and loss counters.</summary>
/// <param name="ReceiverQueueDepth">How many decoded frames are waiting to be shown.</param>
/// <param name="DecodeLatencyUs">Measured decode latency, microseconds.</param>
/// <param name="RoundTripTimeUs">Measured round-trip time, microseconds.</param>
/// <param name="DroppedVideoFrames">Frames dropped since the session began.</param>
public sealed record StatsMessage(
    int ReceiverQueueDepth,
    long DecodeLatencyUs,
    long RoundTripTimeUs,
    long DroppedVideoFrames) : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.Stats;
}

/// <summary>Ordered shutdown.</summary>
/// <param name="Reason">Why.</param>
/// <param name="Detail">Optional detail for logs.</param>
public sealed record ByeMessage(ByeReason Reason, string Detail = "") : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.Bye;
}

/// <summary>
/// Instructs the receiver to play media: fetched from <see cref="Url"/> when it is set, or — when
/// it is empty — the file most recently pushed over this connection via <see cref="MediaDataMessage"/>.
/// </summary>
/// <remarks>
/// The push path exists because some receivers cannot reach the host at all: several Fire OS
/// builds silently drop outbound connections a third-party app initiates to a private LAN address,
/// while the same connection works perfectly in the other direction (host to receiver, which is how
/// pairing itself succeeds). A receiver on a network where outbound HTTP fetches work is free to use
/// <see cref="Url"/> instead and skip the push entirely.
/// </remarks>
public sealed record MediaCommandMessage(
    MediaAction Action,
    string Url = "",
    string Title = "",
    string MimeType = "",
    long DurationMs = -1,
    long StartPositionMs = 0,
    string? SubtitleUrl = null) : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.MediaCommand;
}

/// <summary>
/// One chunk of a media file pushed to the receiver over the control connection, in order.
/// </summary>
/// <param name="Data">Raw file bytes for this chunk.</param>
/// <param name="IsFinal">Whether this is the last chunk; the receiver may now play the file.</param>
public sealed record MediaDataMessage(BinaryData Data, bool IsFinal) : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.MediaData;
}

/// <summary>Selects the surface rendered by the receiver.</summary>
public sealed record SurfaceMessage(SurfaceMode Mode, string Caption = "") : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.Surface;
}

/// <summary>Playback progress and errors reported by the receiver.</summary>
public sealed record PlaybackStateMessage(
    PlaybackState State,
    long PositionMs = 0,
    long DurationMs = -1,
    string Detail = "") : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.PlaybackState;
}

/// <summary>
/// A message type this build does not understand.
/// </summary>
/// <remarks>
/// Preserved rather than rejected, so an older build stays usable against a newer peer for
/// everything it does understand. A protocol that fails closed on anything unfamiliar cannot be
/// extended without a flag day.
/// </remarks>
/// <param name="TypeId">The wire tag.</param>
/// <param name="Payload">The payload, untouched.</param>
public sealed record UnknownMessage(int TypeId, BinaryData Payload) : WireMessage
{
    /// <inheritdoc />
    public override int TypeId { get; } = TypeId;
}

/// <summary>A message inside its envelope.</summary>
/// <param name="ProtocolVersion">The payload version this frame is encoded at.</param>
/// <param name="Message">The message.</param>
/// <param name="Flags">Reserved bits. A peer must echo flags it does not understand.</param>
public sealed record WireFrame(int ProtocolVersion, WireMessage Message, int Flags = 0)
{
    /// <summary>Builds a frame at the current protocol version with no flags.</summary>
    public WireFrame(WireMessage message)
        : this(Flint.Protocol.ProtocolVersion.Current, message)
    {
    }
}
