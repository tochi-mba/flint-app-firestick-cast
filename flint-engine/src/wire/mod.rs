//! The REX wire protocol.
//!
//! The same framing REX Cast defines in its Kotlin `protocol` module. One protocol across the
//! family means the host, the phone app and the receiver can never disagree about what a byte
//! means, and a receiver written for one can be reused by the other.
//!
//! Byte compatibility with the Kotlin implementation is not aspirational: `testdata/golden/` holds
//! encoded frames committed as bytes, and Rust, C# and Kotlin each assert they produce and parse
//! them identically.
//!
//! Every integer on the wire is big-endian, because Java's `DataOutputStream` is and the Kotlin
//! implementation came first.

pub mod browser;
pub mod browser_phase2;
pub mod codec;
pub mod control;
pub mod media;

pub use browser::{
    BrowserCapabilityStatus, BrowserCommandAction, BrowserDialogType, BrowserInputEvent,
    BrowserLoadState, BrowserPointerAction, BrowserPreviewState, BrowserSemanticKey,
};
pub use browser_phase2::{
    BrowserDarkMode, BrowserInteractionMode, BrowserLibraryAction, BrowserLibraryEntry,
    BrowserLibraryEntryKind, BrowserNetworkAction, BrowserProfileAction, BrowserProfileEntry,
    BrowserProfileSource, BrowserSearchEngine, BrowserTabAction, BrowserTabStateEntry,
    BrowserUserAgentMode, BrowserViewAction, BrowserVpnProvider, BrowserVpnSessionState,
    BrowserWorkspaceCommandAction, BrowserWorkspaceInputKind, BrowserWorkspacePaneStateEntry,
    BrowserWorkspaceWireInteractionMode, BrowserWorkspaceWireLayout,
    BrowserWorkspaceWireMuteApplication, BrowserWorkspaceWireObservedPlayback,
    BrowserWorkspaceWirePaneResidency,
};
pub use control::{AuthMethod, ByeReason, ControlEvent, KeyAction, PointerAction, TransportAction};
pub use media::{MediaAction, PlaybackState, SurfaceMode};

/// Protocol version bounds this build understands.
pub mod version {
    /// The oldest payload version this build can decode.
    pub const MIN_SUPPORTED: u16 = 1;
    /// The version this build encodes.
    pub const CURRENT: u16 = 4;
}

/// A numeric media codec identifier.
///
/// A newtype over `u16` rather than an enum, deliberately: a future codec must stay representable
/// so an older build can relay a frame it does not itself understand.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub struct CodecId(pub u16);

impl CodecId {
    /// H.264 / AVC.
    pub const H264: Self = Self(1);
    /// H.265 / HEVC.
    pub const H265: Self = Self(2);
    /// AAC low complexity.
    pub const AAC_LC: Self = Self(3);
    /// Opus.
    pub const OPUS: Self = Self(4);
    /// AV1. A Flint extension; REX Cast does not name it yet.
    pub const AV1: Self = Self(5);

    /// Builds a codec id, rejecting zero.
    ///
    /// Zero is reserved so a zeroed buffer can never decode as a valid codec.
    #[must_use]
    pub fn new(value: u16) -> Option<Self> {
        if value == 0 {
            None
        } else {
            Some(Self(value))
        }
    }
}

/// Message type tags, matching `WireMessageType` in the Kotlin implementation.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u16)]
pub enum MessageType {
    /// Capability and version exchange.
    Hello = 1,
    /// Pairing code, session token, or public-key proof.
    Auth = 2,
    /// Codec, dimensions and codec-specific data.
    VideoConfig = 3,
    /// One video access unit.
    Video = 4,
    /// Audio codec, sample rate and channel count.
    AudioConfig = 5,
    /// One audio packet.
    Audio = 6,
    /// An input or transport event from the receiver.
    Control = 7,
    /// Receiver-side timing and loss counters.
    Stats = 8,
    /// Ordered shutdown.
    Bye = 9,
    /// Receiver-side local media playback command.
    MediaCommand = 10,
    /// Receiver render-surface selection.
    Surface = 11,
    /// Receiver media-player state report.
    PlaybackState = 12,
    /// One chunk of a media file pushed over the control connection.
    MediaData = 13,
    /// Secure receiver browser capability state.
    BrowserCapability = 14,
    /// Secure host-to-receiver browser command.
    BrowserCommand = 15,
    /// Secure host-to-receiver portable browser input.
    BrowserInput = 16,
    /// Secure receiver-to-host safe browser state.
    BrowserState = 17,
    /// Secure receiver-to-host latest-only JPEG browser preview.
    BrowserPreview = 18,
    /// Secure receiver-to-host native browser dialog observation.
    BrowserDialog = 19,
    /// Secure host-to-receiver native browser dialog reply.
    BrowserDialogReply = 20,
    /// Secure host-to-receiver tab collection command.
    BrowserTabCommand = 21,
    /// Secure receiver-to-host tab collection snapshot.
    BrowserTabState = 22,
    /// Secure host-to-receiver browser view command.
    BrowserViewCommand = 23,
    /// Secure receiver-to-host browser view snapshot.
    BrowserViewState = 24,
    /// Secure receiver-to-host bounded PNG favicon.
    BrowserFavicon = 25,
    /// Secure host-to-receiver browser-library command.
    BrowserLibraryCommand = 26,
    /// Secure receiver-to-host browser-library snapshot.
    BrowserLibraryState = 27,
    /// Secure browser profile selection or TV profile-management command.
    BrowserProfileCommand = 28,
    /// Secure active profile source and TV profile catalog.
    BrowserProfileState = 29,
    /// Secure host-to-receiver browser network/VPN settings command.
    BrowserNetworkCommand = 30,
    /// Secure receiver-to-host browser network/VPN snapshot.
    BrowserNetworkState = 31,
    /// Secure host-to-receiver browser workspace operation.
    BrowserWorkspaceCommand = 32,
    /// Secure receiver-to-host browser workspace snapshot.
    BrowserWorkspaceState = 33,
    /// Secure host-to-receiver browser workspace pane input.
    BrowserWorkspaceInput = 34,
    /// Sets workspace dividers.
    BrowserWorkspaceResize = 35,
    /// Reports workspace dividers.
    BrowserWorkspaceGeometry = 36,
}

impl MessageType {
    /// Maps a wire tag, or `None` when this build does not know the type.
    #[must_use]
    pub fn from_id(id: u16) -> Option<Self> {
        match id {
            1 => Some(Self::Hello),
            2 => Some(Self::Auth),
            3 => Some(Self::VideoConfig),
            4 => Some(Self::Video),
            5 => Some(Self::AudioConfig),
            6 => Some(Self::Audio),
            7 => Some(Self::Control),
            8 => Some(Self::Stats),
            9 => Some(Self::Bye),
            10 => Some(Self::MediaCommand),
            11 => Some(Self::Surface),
            12 => Some(Self::PlaybackState),
            13 => Some(Self::MediaData),
            14 => Some(Self::BrowserCapability),
            15 => Some(Self::BrowserCommand),
            16 => Some(Self::BrowserInput),
            17 => Some(Self::BrowserState),
            18 => Some(Self::BrowserPreview),
            19 => Some(Self::BrowserDialog),
            20 => Some(Self::BrowserDialogReply),
            21 => Some(Self::BrowserTabCommand),
            22 => Some(Self::BrowserTabState),
            23 => Some(Self::BrowserViewCommand),
            24 => Some(Self::BrowserViewState),
            25 => Some(Self::BrowserFavicon),
            26 => Some(Self::BrowserLibraryCommand),
            27 => Some(Self::BrowserLibraryState),
            28 => Some(Self::BrowserProfileCommand),
            29 => Some(Self::BrowserProfileState),
            30 => Some(Self::BrowserNetworkCommand),
            31 => Some(Self::BrowserNetworkState),
            32 => Some(Self::BrowserWorkspaceCommand),
            33 => Some(Self::BrowserWorkspaceState),
            34 => Some(Self::BrowserWorkspaceInput),
            35 => Some(Self::BrowserWorkspaceResize),
            36 => Some(Self::BrowserWorkspaceGeometry),
            _ => None,
        }
    }
}

/// One protocol message.
#[derive(Debug, Clone, PartialEq)]
pub enum Message {
    /// Capability and version exchange.
    Hello {
        /// Oldest payload version the sender accepts.
        minimum_version: u16,
        /// Newest payload version the sender accepts.
        maximum_version: u16,
        /// A human-readable device name.
        device_name: String,
        /// Codecs the sender can handle. Encoded in ascending numeric order.
        codec_capabilities: Vec<CodecId>,
        /// Surface width in pixels.
        screen_width: i32,
        /// Surface height in pixels.
        screen_height: i32,
        /// Surface density.
        density_dpi: i32,
    },
    /// Authorization material.
    Auth {
        /// How the credential should be interpreted.
        method: AuthMethod,
        /// The credential itself. Never empty.
        credential: Vec<u8>,
        /// An optional public-key fingerprint.
        public_key_fingerprint: Option<String>,
    },
    /// Video stream configuration.
    VideoConfig {
        /// The codec that will follow.
        codec: CodecId,
        /// Frame width.
        width: i32,
        /// Frame height.
        height: i32,
        /// Codec-specific data, such as SPS and PPS.
        codec_specific_data: Vec<Vec<u8>>,
    },
    /// One video access unit.
    Video {
        /// Presentation timestamp, microseconds.
        presentation_time_us: i64,
        /// Whether this unit can be decoded without any earlier one.
        key_frame: bool,
        /// The access unit.
        data: Vec<u8>,
    },
    /// Audio stream configuration.
    AudioConfig {
        /// The codec that will follow.
        codec: CodecId,
        /// Sample rate in hertz.
        sample_rate_hz: i32,
        /// Channel count.
        channel_count: u8,
        /// Codec-specific data.
        codec_specific_data: Vec<u8>,
    },
    /// One audio packet.
    Audio {
        /// Presentation timestamp, microseconds.
        presentation_time_us: i64,
        /// The packet.
        data: Vec<u8>,
    },
    /// An input or transport event.
    Control {
        /// Monotonic sequence number, so a receiver can detect a gap.
        sequence_number: i64,
        /// What happened.
        event: ControlEvent,
    },
    /// Receiver-side timing and loss counters.
    Stats {
        /// How many decoded frames are waiting to be shown.
        receiver_queue_depth: i32,
        /// Measured decode latency, microseconds.
        decode_latency_us: i64,
        /// Measured round-trip time, microseconds.
        round_trip_time_us: i64,
        /// Frames dropped since the session began.
        dropped_video_frames: i64,
    },
    /// Ordered shutdown.
    Bye {
        /// Why.
        reason: ByeReason,
        /// Optional detail for logs.
        detail: String,
    },
    /// Instructs the receiver to load local media, fetch a selected URL, or clear its player.
    MediaCommand {
        /// What the receiver should do.
        action: MediaAction,
        /// Selected source URL, or empty for the file pushed through `MediaData`.
        url: String,
        /// Safe, user-visible item title.
        title: String,
        /// MIME type, required for a `Load` action.
        mime_type: String,
        /// Duration in milliseconds, or -1 when unknown.
        duration_ms: i64,
        /// Non-negative start position in milliseconds.
        start_position_ms: i64,
        /// Optional selected subtitle URL.
        subtitle_url: Option<String>,
    },
    /// A bounded chunk of the explicitly selected media file being pushed to the receiver.
    MediaData {
        /// Raw file bytes for this chunk.
        data: Vec<u8>,
        /// Whether the receiver may now consume the complete pushed file.
        is_final: bool,
    },
    /// Selects the receiver render surface.
    Surface {
        /// Surface to show.
        mode: SurfaceMode,
        /// Safe caption for the selected surface.
        caption: String,
    },
    /// Reports receiver media playback progress and a safe diagnostic when appropriate.
    PlaybackState {
        /// Receiver playback state.
        state: PlaybackState,
        /// Non-negative current position in milliseconds.
        position_ms: i64,
        /// Duration in milliseconds, or -1 when unknown.
        duration_ms: i64,
        /// Safe, bounded diagnostic detail.
        detail: String,
    },
    /// Browser capability state emitted only after secure browser-session authentication.
    BrowserCapability {
        /// Receiver browser capability state.
        status: BrowserCapabilityStatus,
        /// Discovery-advertised secure endpoint port; zero when not available.
        secure_endpoint_port: u16,
        /// Android API level.
        api_level: u16,
        /// Safe WebView package/version diagnostic.
        web_view_version: String,
        /// Whether passive JPEG preview is supported.
        preview_supported: bool,
        /// Maximum supported preview width.
        preview_max_width: u16,
        /// Maximum supported preview height.
        preview_max_height: u16,
        /// Interactive preview-rate ceiling.
        interactive_preview_frames_per_second: u8,
        /// Idle preview-rate ceiling.
        idle_preview_frames_per_second: u8,
        /// Maximum encoded preview size in bytes.
        preview_max_bytes: i32,
        /// Safe, bounded diagnostic detail.
        detail: String,
    },
    /// Ordered, host-owned browser navigation or lifecycle command.
    BrowserCommand {
        /// Positive browser epoch.
        epoch: i64,
        /// Positive, monotonic host command id.
        command_id: i64,
        /// Requested operation.
        action: BrowserCommandAction,
        /// Present only for `Open` and `Navigate`.
        url: Option<String>,
        /// Present only for `SetPreviewEnabled`.
        preview_enabled: Option<bool>,
    },
    /// Ordered, host-owned portable browser input event.
    BrowserInput {
        /// Positive browser epoch.
        epoch: i64,
        /// Positive, monotonic host input sequence.
        sequence: i64,
        /// Explicit portable input event.
        event: BrowserInputEvent,
    },
    /// Bounded, safe browser state snapshot.
    BrowserState {
        /// Positive browser epoch.
        epoch: i64,
        /// Positive receiver-owned state revision.
        revision: i64,
        /// Navigation id, zero only while no navigation is active.
        navigation_id: i64,
        /// Last command accepted by the receiver, or zero.
        last_accepted_command_id: i64,
        /// Last input event accepted by the receiver, or zero.
        last_accepted_input_sequence: i64,
        /// Load lifecycle state.
        load_state: BrowserLoadState,
        /// Safe, bounded canonical page URL.
        url: String,
        /// Safe, bounded page title.
        title: String,
        /// Load progress from zero through one hundred.
        progress: u8,
        /// Whether browser history can move back.
        can_go_back: bool,
        /// Whether browser history can move forward.
        can_go_forward: bool,
        /// Viewport width, or zero while not known.
        viewport_width: i32,
        /// Viewport height, or zero while not known.
        viewport_height: i32,
        /// Passive preview state for this secure session.
        preview_state: BrowserPreviewState,
        /// Safe, bounded error detail, populated only for `Failed`.
        error_detail: String,
    },
    /// Latest-only JPEG preview tied to a navigation and frame reference.
    BrowserPreview {
        /// Positive browser epoch.
        epoch: i64,
        /// Positive navigation id.
        navigation_id: i64,
        /// Positive frame id.
        frame_id: i64,
        /// JPEG width.
        width: u16,
        /// JPEG height.
        height: u16,
        /// Encoded JPEG bytes.
        jpeg: Vec<u8>,
    },
    /// An expiring native browser-dialog observation.
    BrowserDialog {
        /// Positive browser epoch.
        epoch: i64,
        /// Positive receiver-owned dialog id.
        dialog_id: i64,
        /// Native dialog type.
        dialog_type: BrowserDialogType,
        /// Safe top-level origin.
        origin: String,
        /// Bounded native dialog message.
        message: String,
        /// Prompt default value, empty for non-prompt dialogs.
        default_value: String,
        /// Bounded dialog lifetime in milliseconds.
        timeout_milliseconds: i32,
    },
    /// Explicit reply to the currently active native browser dialog.
    BrowserDialogReply {
        /// Positive browser epoch.
        epoch: i64,
        /// Positive receiver-owned dialog id.
        dialog_id: i64,
        /// Whether the dialog was accepted.
        accepted: bool,
        /// Optional prompt reply; absent when cancelling.
        prompt_text: Option<String>,
    },
    /// Ordered host command over the receiver-owned tab collection.
    BrowserTabCommand {
        /// Positive browser epoch.
        epoch: i64,
        /// Positive, monotonic host command id.
        command_id: i64,
        /// Requested tab operation.
        action: BrowserTabAction,
        /// Target tab id, or zero for `New`.
        tab_id: i64,
        /// Optional initial URL, used only when creating a tab.
        url: Option<String>,
    },
    /// Bounded receiver-owned tab collection snapshot.
    BrowserTabState {
        /// Positive browser epoch.
        epoch: i64,
        /// Positive receiver-owned revision.
        revision: i64,
        /// Active tab id, or zero when the list is empty.
        active_tab_id: i64,
        /// At most eight bounded tab rows.
        tabs: Vec<BrowserTabStateEntry>,
    },
    /// Ordered host command for page-view settings and find-in-page.
    BrowserViewCommand {
        /// Positive browser epoch.
        epoch: i64,
        /// Positive, monotonic host command id.
        command_id: i64,
        /// Requested view operation.
        action: BrowserViewAction,
        /// Action-specific numeric value.
        value: i32,
        /// Action-specific bounded text, otherwise empty.
        text: String,
    },
    /// Bounded receiver browser-view snapshot.
    BrowserViewState {
        /// Positive browser epoch.
        epoch: i64,
        /// Positive receiver-owned revision.
        revision: i64,
        /// Current text zoom percentage.
        zoom_percent: i32,
        /// Current user-agent profile.
        ua_mode: BrowserUserAgentMode,
        /// Current page-colour policy.
        dark_mode: BrowserDarkMode,
        /// Current D-pad interaction mode.
        input_mode: BrowserInteractionMode,
        /// Whether a page custom view owns the full screen.
        fullscreen: bool,
        /// Whether page media is currently playing.
        media_playing: bool,
        /// Whether a page editing control is focused. No entered value is carried.
        editing_focused: bool,
        /// Whether find-in-page is active.
        find_active: bool,
        /// One-based current result, or zero when no result is selected.
        find_current: i32,
        /// Total find results.
        find_total: i32,
        /// Current search-engine preset/custom marker.
        search_engine: BrowserSearchEngine,
    },
    /// Bounded PNG favicon tied to a receiver-owned id.
    BrowserFavicon {
        /// Positive browser epoch.
        epoch: i64,
        /// Positive receiver-owned favicon id.
        favicon_id: i64,
        /// PNG width, at most 64 pixels.
        width: u16,
        /// PNG height, at most 64 pixels.
        height: u16,
        /// Encoded PNG bytes, capped at 16 KiB.
        png: Vec<u8>,
    },
    /// Ordered host command over the receiver-owned bookmark/history library.
    BrowserLibraryCommand {
        /// Positive browser epoch.
        epoch: i64,
        /// Positive, monotonic host command id.
        command_id: i64,
        /// Requested library operation.
        action: BrowserLibraryAction,
        /// Action-specific URL, otherwise empty.
        url: String,
        /// Action-specific title, otherwise empty.
        title: String,
    },
    /// Bounded receiver-owned bookmark and history snapshot.
    BrowserLibraryState {
        /// Positive browser epoch.
        epoch: i64,
        /// Positive receiver-owned revision.
        revision: i64,
        /// Bookmark rows, each tagged `Bookmark` on the wire.
        bookmarks: Vec<BrowserLibraryEntry>,
        /// History rows, each tagged `History` on the wire.
        history: Vec<BrowserLibraryEntry>,
    },
    /// Ordered profile selection or TV profile-management command.
    BrowserProfileCommand {
        /// Positive browser epoch.
        epoch: i64,
        /// Positive, monotonic command id.
        command_id: i64,
        /// Requested profile operation.
        action: BrowserProfileAction,
        /// Action-specific TV profile id, otherwise empty.
        profile_id: String,
        /// Action-specific TV profile name, otherwise empty.
        name: String,
    },
    /// Active profile source and bounded persistent TV-profile catalog.
    BrowserProfileState {
        /// Positive browser epoch.
        epoch: i64,
        /// Positive receiver-owned revision.
        revision: i64,
        /// Durable owner of the active profile.
        active_source: BrowserProfileSource,
        /// Active TV profile id, or empty for a device profile.
        active_profile_id: String,
        /// Bounded connected-device display name, which may be empty.
        device_name: String,
        /// At most eight persistent TV profiles.
        profiles: Vec<BrowserProfileEntry>,
    },
    /// Ordered browser network/VPN settings command.
    ///
    /// `config_text` is opaque WireGuard configuration and must never be logged.
    BrowserNetworkCommand {
        /// Positive browser epoch.
        epoch: i64,
        /// Positive, monotonic command id.
        command_id: i64,
        /// Requested network operation.
        action: BrowserNetworkAction,
        /// Target TV profile id; empty only for RequestSnapshot meaning the active TV profile.
        profile_id: String,
        /// Whether VPN auto-connect prefs are enabled for the profile.
        vpn_enabled: bool,
        /// Configured VPN provider.
        provider: BrowserVpnProvider,
        /// Whether to connect when a browser session starts.
        auto_connect_on_browser_start: bool,
        /// Whether pages must wait for VPN before loading.
        require_vpn_before_browse: bool,
        /// Opaque WireGuard config text; never log this.
        config_text: String,
    },
    /// Bounded network/VPN snapshot that never echoes full config text.
    BrowserNetworkState {
        /// Positive browser epoch.
        epoch: i64,
        /// Positive receiver-owned revision.
        revision: i64,
        /// Profile the snapshot describes; may be empty when none is active.
        profile_id: String,
        /// Whether VPN prefs are enabled for the profile.
        vpn_enabled: bool,
        /// Configured VPN provider.
        provider: BrowserVpnProvider,
        /// Whether to connect when a browser session starts.
        auto_connect_on_browser_start: bool,
        /// Whether pages must wait for VPN before loading.
        require_vpn_before_browse: bool,
        /// Whether non-blank config text is stored (full text is never echoed).
        config_present: bool,
        /// Whether the device can prepare a VPN session.
        capability_preparable: bool,
        /// Safe capability probe reason.
        capability_reason: String,
        /// Soft-fail session state.
        session_state: BrowserVpnSessionState,
        /// Safe session detail; never includes config text.
        session_detail: String,
    },
    /// Ordered host-to-receiver browser workspace command.
    BrowserWorkspaceCommand {
        /// Active browser session epoch.
        epoch: i64,
        /// Monotonic command identifier on the authenticated session.
        command_id: i64,
        /// Receiver revision the sender observed; stale actions are rejected.
        expected_revision: i64,
        /// Workspace operation to apply.
        action: browser::BrowserWorkspaceCommandAction,
        /// Target pane identifier.
        pane_id: i64,
        /// Action-specific bounded value.
        value: u8,
        /// Canonical HTTPS address, or empty when no navigation is requested.
        url: String,
    },
    /// Bounded receiver-owned browser workspace snapshot.
    BrowserWorkspaceState {
        /// Active browser session epoch.
        epoch: i64,
        /// Monotonic receiver workspace revision.
        revision: i64,
        /// Receiver-selected arrangement of panes.
        layout: browser::BrowserWorkspaceWireLayout,
        /// Pane receiving directed input; zero for an empty workspace.
        focused_pane_id: i64,
        /// Whether input belongs to workspace controls or the focused page.
        interaction_mode: browser::BrowserWorkspaceWireInteractionMode,
        /// Pane with a page-requested custom view; zero when absent.
        page_fullscreen_pane_id: i64,
        /// Explicitly enlarged pane; zero when theater is inactive.
        theater_pane_id: i64,
        /// Receiver-advertised concurrent renderer budget.
        max_live_renderers: u8,
        /// Receiver-advertised logical pane budget.
        max_open_panes: u8,
        /// Bounded pane states in slot order.
        panes: Vec<browser::BrowserWorkspacePaneStateEntry>,
    },
    /// Ordered host-to-receiver browser workspace input.
    BrowserWorkspaceInput {
        /// Active browser session epoch.
        epoch: i64,
        /// Monotonic command identifier on the authenticated session.
        command_id: i64,
        /// Receiver revision the sender observed; stale actions are rejected.
        expected_revision: i64,
        /// Target pane identifier.
        pane_id: i64,
        /// Discriminator for semantic-key or composed-text input.
        kind: browser::BrowserWorkspaceInputKind,
        /// Semantic key, present only for key input.
        key: Option<BrowserSemanticKey>,
        /// Composed text, empty for key input.
        text: String,
    },
    /// Sets workspace dividers in protocol four.
    BrowserWorkspaceResize {
        /// Session epoch.
        epoch: i64,
        /// Ordered command identifier.
        command_id: i64,
        /// Last observed workspace revision.
        expected_revision: i64,
        /// Column fraction in ten-thousandths.
        column: u16,
        /// Row fraction in ten-thousandths.
        row: u16,
        /// Zero unchanged, one tabs, two workspace.
        mode: u8,
    },
    /// Authoritative workspace dividers in protocol four.
    BrowserWorkspaceGeometry {
        /// Session epoch.
        epoch: i64,
        /// Workspace revision.
        revision: i64,
        /// Column fraction in ten-thousandths.
        column: u16,
        /// Row fraction in ten-thousandths.
        row: u16,
        /// Zero unchanged, one tabs, two workspace.
        mode: u8,
    },
    /// A message type this build does not understand.
    ///
    /// Preserved rather than rejected, so an older build stays usable against a newer peer for
    /// everything it does understand. A protocol that fails closed on anything unfamiliar cannot
    /// be extended without a flag day.
    Unknown {
        /// The wire tag.
        type_id: u16,
        /// The payload, untouched.
        payload: Vec<u8>,
    },
}

impl Message {
    /// The wire tag for this message.
    #[must_use]
    pub fn type_id(&self) -> u16 {
        match self {
            Self::Hello { .. } => MessageType::Hello as u16,
            Self::Auth { .. } => MessageType::Auth as u16,
            Self::VideoConfig { .. } => MessageType::VideoConfig as u16,
            Self::Video { .. } => MessageType::Video as u16,
            Self::AudioConfig { .. } => MessageType::AudioConfig as u16,
            Self::Audio { .. } => MessageType::Audio as u16,
            Self::Control { .. } => MessageType::Control as u16,
            Self::Stats { .. } => MessageType::Stats as u16,
            Self::Bye { .. } => MessageType::Bye as u16,
            Self::MediaCommand { .. } => MessageType::MediaCommand as u16,
            Self::MediaData { .. } => MessageType::MediaData as u16,
            Self::Surface { .. } => MessageType::Surface as u16,
            Self::PlaybackState { .. } => MessageType::PlaybackState as u16,
            Self::BrowserCapability { .. } => MessageType::BrowserCapability as u16,
            Self::BrowserCommand { .. } => MessageType::BrowserCommand as u16,
            Self::BrowserInput { .. } => MessageType::BrowserInput as u16,
            Self::BrowserState { .. } => MessageType::BrowserState as u16,
            Self::BrowserPreview { .. } => MessageType::BrowserPreview as u16,
            Self::BrowserDialog { .. } => MessageType::BrowserDialog as u16,
            Self::BrowserDialogReply { .. } => MessageType::BrowserDialogReply as u16,
            Self::BrowserTabCommand { .. } => MessageType::BrowserTabCommand as u16,
            Self::BrowserTabState { .. } => MessageType::BrowserTabState as u16,
            Self::BrowserViewCommand { .. } => MessageType::BrowserViewCommand as u16,
            Self::BrowserViewState { .. } => MessageType::BrowserViewState as u16,
            Self::BrowserFavicon { .. } => MessageType::BrowserFavicon as u16,
            Self::BrowserLibraryCommand { .. } => MessageType::BrowserLibraryCommand as u16,
            Self::BrowserLibraryState { .. } => MessageType::BrowserLibraryState as u16,
            Self::BrowserProfileCommand { .. } => MessageType::BrowserProfileCommand as u16,
            Self::BrowserProfileState { .. } => MessageType::BrowserProfileState as u16,
            Self::BrowserNetworkCommand { .. } => MessageType::BrowserNetworkCommand as u16,
            Self::BrowserNetworkState { .. } => MessageType::BrowserNetworkState as u16,
            Self::BrowserWorkspaceCommand { .. } => MessageType::BrowserWorkspaceCommand as u16,
            Self::BrowserWorkspaceState { .. } => MessageType::BrowserWorkspaceState as u16,
            Self::BrowserWorkspaceInput { .. } => MessageType::BrowserWorkspaceInput as u16,
            Self::BrowserWorkspaceResize { .. } => MessageType::BrowserWorkspaceResize as u16,
            Self::BrowserWorkspaceGeometry { .. } => MessageType::BrowserWorkspaceGeometry as u16,
            Self::Unknown { type_id, .. } => *type_id,
        }
    }
}

/// A message inside its envelope.
#[derive(Debug, Clone, PartialEq)]
pub struct Frame {
    /// The payload version this frame is encoded at.
    pub protocol_version: u16,
    /// The message.
    pub message: Message,
    /// Reserved bits. A peer must echo flags it does not understand.
    pub flags: u16,
}

impl Frame {
    /// Builds a frame at the current protocol version with no flags.
    #[must_use]
    pub fn new(message: Message) -> Self {
        Self {
            protocol_version: version::CURRENT,
            message,
            flags: 0,
        }
    }
}

/// Chooses the payload version two peers will speak.
///
/// Returns the highest version both ranges cover, or `None` when they do not overlap. A session
/// with no overlap closes rather than attempting a downgrade, because a downgrade to a version
/// neither side actually implements is worse than a clear refusal.
#[must_use]
pub fn negotiate_version(
    local_minimum: u16,
    local_maximum: u16,
    remote_minimum: u16,
    remote_maximum: u16,
) -> Option<u16> {
    if local_minimum > local_maximum || remote_minimum > remote_maximum {
        return None;
    }

    let lower = local_minimum.max(remote_minimum);
    let upper = local_maximum.min(remote_maximum);
    if lower <= upper {
        Some(upper)
    } else {
        None
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn message_tags_match_the_kotlin_implementation() {
        // These numbers are the contract. Changing one silently breaks the receiver.
        assert_eq!(MessageType::Hello as u16, 1);
        assert_eq!(MessageType::Auth as u16, 2);
        assert_eq!(MessageType::VideoConfig as u16, 3);
        assert_eq!(MessageType::Video as u16, 4);
        assert_eq!(MessageType::AudioConfig as u16, 5);
        assert_eq!(MessageType::Audio as u16, 6);
        assert_eq!(MessageType::Control as u16, 7);
        assert_eq!(MessageType::Stats as u16, 8);
        assert_eq!(MessageType::Bye as u16, 9);
        assert_eq!(MessageType::MediaCommand as u16, 10);
        assert_eq!(MessageType::Surface as u16, 11);
        assert_eq!(MessageType::PlaybackState as u16, 12);
        assert_eq!(MessageType::MediaData as u16, 13);
        assert_eq!(MessageType::BrowserCapability as u16, 14);
        assert_eq!(MessageType::BrowserCommand as u16, 15);
        assert_eq!(MessageType::BrowserInput as u16, 16);
        assert_eq!(MessageType::BrowserState as u16, 17);
        assert_eq!(MessageType::BrowserPreview as u16, 18);
        assert_eq!(MessageType::BrowserDialog as u16, 19);
        assert_eq!(MessageType::BrowserDialogReply as u16, 20);
        assert_eq!(MessageType::BrowserTabCommand as u16, 21);
        assert_eq!(MessageType::BrowserTabState as u16, 22);
        assert_eq!(MessageType::BrowserViewCommand as u16, 23);
        assert_eq!(MessageType::BrowserViewState as u16, 24);
        assert_eq!(MessageType::BrowserFavicon as u16, 25);
        assert_eq!(MessageType::BrowserLibraryCommand as u16, 26);
        assert_eq!(MessageType::BrowserLibraryState as u16, 27);
        assert_eq!(MessageType::BrowserProfileCommand as u16, 28);
        assert_eq!(MessageType::BrowserProfileState as u16, 29);
        assert_eq!(MessageType::BrowserNetworkCommand as u16, 30);
        assert_eq!(MessageType::BrowserNetworkState as u16, 31);
    }

    #[test]
    fn codec_ids_match_the_kotlin_implementation() {
        assert_eq!(CodecId::H264.0, 1);
        assert_eq!(CodecId::H265.0, 2);
        assert_eq!(CodecId::AAC_LC.0, 3);
        assert_eq!(CodecId::OPUS.0, 4);
    }

    #[test]
    fn codec_zero_is_rejected_so_a_zeroed_buffer_cannot_decode() {
        assert!(CodecId::new(0).is_none());
        assert_eq!(CodecId::new(1), Some(CodecId::H264));
    }

    #[test]
    fn unknown_tags_map_to_none_rather_than_a_default() {
        assert!(MessageType::from_id(0).is_none());
        assert_eq!(MessageType::from_id(10), Some(MessageType::MediaCommand));
        assert_eq!(
            MessageType::from_id(27),
            Some(MessageType::BrowserLibraryState)
        );
        assert_eq!(
            MessageType::from_id(28),
            Some(MessageType::BrowserProfileCommand)
        );
        assert_eq!(
            MessageType::from_id(29),
            Some(MessageType::BrowserProfileState)
        );
        assert_eq!(
            MessageType::from_id(30),
            Some(MessageType::BrowserNetworkCommand)
        );
        assert_eq!(
            MessageType::from_id(31),
            Some(MessageType::BrowserNetworkState)
        );
        assert_eq!(
            MessageType::from_id(32),
            Some(MessageType::BrowserWorkspaceCommand)
        );
        assert_eq!(
            MessageType::from_id(33),
            Some(MessageType::BrowserWorkspaceState)
        );
        assert_eq!(
            MessageType::from_id(34),
            Some(MessageType::BrowserWorkspaceInput)
        );
        assert!(MessageType::from_id(35).is_none());
        assert!(AuthMethod::from_id(0).is_none());
        assert!(TransportAction::from_id(7).is_none());
        assert!(PointerAction::from_id(5).is_none());
        assert!(KeyAction::from_id(3).is_none());
        assert!(ByeReason::from_id(6).is_none());
    }

    #[test]
    fn control_event_tags_are_stable() {
        assert_eq!(
            ControlEvent::Transport {
                action: TransportAction::Play,
                position_ms: -1
            }
            .event_id(),
            1
        );
        assert_eq!(
            ControlEvent::Pointer {
                action: PointerAction::Move,
                x: 0.0,
                y: 0.0,
                buttons: 0
            }
            .event_id(),
            2
        );
        assert_eq!(
            ControlEvent::Key {
                action: KeyAction::Down,
                key_code: 0
            }
            .event_id(),
            3
        );
        assert_eq!(ControlEvent::Text(String::new()).event_id(), 4);
        assert_eq!(ControlEvent::Volume(0.0).event_id(), 5);
    }

    #[test]
    fn an_unknown_message_reports_the_tag_it_arrived_with() {
        let message = Message::Unknown {
            type_id: 0xfefe,
            payload: vec![1, 2, 3],
        };
        assert_eq!(message.type_id(), 0xfefe);
    }

    #[test]
    fn version_negotiation_chooses_the_highest_overlap() {
        assert_eq!(negotiate_version(1, 3, 2, 4), Some(3));
        assert_eq!(negotiate_version(1, 1, 1, 1), Some(1));
    }

    #[test]
    fn version_negotiation_refuses_a_gap_rather_than_downgrading() {
        assert_eq!(negotiate_version(5, 6, 1, 4), None);
        assert_eq!(negotiate_version(1, 2, 3, 4), None);
    }

    #[test]
    fn version_negotiation_rejects_an_inverted_range() {
        assert_eq!(negotiate_version(3, 2, 1, 4), None);
        assert_eq!(negotiate_version(1, 4, 3, 2), None);
    }
}
