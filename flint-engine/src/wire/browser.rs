//! The constrained v2 browser wire family.
//!
//! Browser values are valid only inside the authenticated TLS browser session. They are modelled
//! in the shared wire crate so Rust, C#, and Kotlin can prove byte-for-byte agreement without
//! letting a legacy cast socket accidentally carry browser traffic.

pub use super::browser_phase2::*;
use super::Message;

/// Browser payload limits that are part of the byte-level protocol contract.
pub mod limits {
    /// Maximum UTF-8 bytes in a navigable URL.
    pub const MAX_URL_BYTES: usize = 4 * 1024;
    /// Maximum UTF-8 bytes in explicit text input or a prompt reply.
    pub const MAX_TEXT_BYTES: usize = 4 * 1024;
    /// Maximum UTF-8 bytes in a safe page title or WebView version string.
    pub const MAX_TITLE_BYTES: usize = 512;
    /// Maximum UTF-8 bytes in a safe diagnostic detail string.
    pub const MAX_DETAIL_BYTES: usize = 1024;
    /// Maximum UTF-8 bytes in a top-level dialog origin.
    pub const MAX_ORIGIN_BYTES: usize = 2 * 1024;
    /// Maximum UTF-8 bytes in a browser dialog message or default value.
    pub const MAX_DIALOG_BYTES: usize = 4 * 1024;
    /// Largest permitted encoded JPEG preview payload.
    pub const MAX_PREVIEW_BYTES: usize = 768 * 1024;
    /// Maximum preview width advertised or accepted by the protocol.
    pub const MAX_PREVIEW_WIDTH: u16 = 960;
    /// Maximum preview height advertised or accepted by the protocol.
    pub const MAX_PREVIEW_HEIGHT: u16 = 540;
    /// Maximum requested interactive preview rate.
    pub const MAX_INTERACTIVE_PREVIEW_FPS: u8 = 5;
    /// Maximum requested idle preview rate.
    pub const MAX_IDLE_PREVIEW_FPS: u8 = 1;
    /// Maximum native-dialog lifetime.
    pub const MAX_DIALOG_TIMEOUT_MILLISECONDS: i32 = 60_000;
    /// Maximum browser viewport dimension.
    pub const MAX_VIEWPORT_DIMENSION: i32 = 16_384;
    /// Maximum number of tabs carried by one tab-state snapshot.
    pub const MAX_TABS: usize = 8;
    /// Largest permitted encoded PNG favicon payload.
    pub const MAX_FAVICON_BYTES: usize = 16 * 1024;
    /// Maximum favicon width or height.
    pub const MAX_FAVICON_DIMENSION: u16 = 64;
    /// Maximum UTF-8 bytes in a find-in-page query.
    pub const MAX_FIND_TEXT_BYTES: usize = 512;
    /// Smallest browser text zoom accepted by the protocol.
    pub const MIN_ZOOM_PERCENT: i32 = 50;
    /// Largest browser text zoom accepted by the protocol.
    pub const MAX_ZOOM_PERCENT: i32 = 200;
    /// Maximum number of bookmarks in one library snapshot. The count is a wire `u8`.
    pub const MAX_BOOKMARKS: usize = u8::MAX as usize;
    /// Maximum number of history rows in one library snapshot. The count is a wire `u8`.
    pub const MAX_HISTORY_ENTRIES: usize = u8::MAX as usize;
    /// Maximum UTF-8 bytes in a stable TV browser-profile identifier.
    pub const MAX_PROFILE_ID_BYTES: usize = 64;
    /// Maximum UTF-8 bytes in a user-visible TV browser-profile name.
    pub const MAX_PROFILE_NAME_BYTES: usize = 64;
    /// Maximum UTF-8 bytes in the connected device display name.
    pub const MAX_DEVICE_PROFILE_NAME_BYTES: usize = 64;
    /// Maximum TV-resident profiles in one bounded catalog.
    pub const MAX_TV_PROFILES: usize = 8;
    /// Maximum UTF-8 bytes in an opaque WireGuard configuration text blob.
    pub const MAX_CONFIG_UTF8_BYTES: usize = 64 * 1024;
    /// Maximum independently browsing panes in one workspace snapshot.
    pub const MAX_WORKSPACE_PANES: usize = 8;
}

/// Receiver browser capability state sent after secure-session authentication.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum BrowserCapabilityStatus {
    /// The receiver can host the constrained browser feature.
    Available = 1,
    /// The secure endpoint is absent or has not been paired.
    SecureEndpointUnavailable = 2,
    /// The receiver cannot create a compatible WebView.
    WebViewUnavailable = 3,
    /// The device/API baseline cannot satisfy the browser security profile.
    UnsupportedPlatform = 4,
    /// The build/channel does not permit browser use.
    DistributionRestricted = 5,
}

impl BrowserCapabilityStatus {
    /// Maps a wire tag.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::Available),
            2 => Some(Self::SecureEndpointUnavailable),
            3 => Some(Self::WebViewUnavailable),
            4 => Some(Self::UnsupportedPlatform),
            5 => Some(Self::DistributionRestricted),
            _ => None,
        }
    }
}

/// Browser navigation and lifecycle commands.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum BrowserCommandAction {
    /// Begin a new browser epoch at an HTTPS URL.
    Open = 1,
    /// Navigate inside the current browser epoch.
    Navigate = 2,
    /// Move to the previous history entry.
    Back = 3,
    /// Move to the next history entry.
    Forward = 4,
    /// Reload the current page.
    Reload = 5,
    /// Stop the current load.
    Stop = 6,
    /// Close the browser surface and release browser state.
    Close = 7,
    /// Enable or disable passive preview for this secure session.
    SetPreviewEnabled = 8,
    /// Clear bounded browser data after separate native confirmation.
    ClearData = 9,
}

impl BrowserCommandAction {
    /// Maps a wire tag.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::Open),
            2 => Some(Self::Navigate),
            3 => Some(Self::Back),
            4 => Some(Self::Forward),
            5 => Some(Self::Reload),
            6 => Some(Self::Stop),
            7 => Some(Self::Close),
            8 => Some(Self::SetPreviewEnabled),
            9 => Some(Self::ClearData),
            _ => None,
        }
    }
}

/// Pointer actions deliberately independent of platform input constants.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum BrowserPointerAction {
    /// The pointer moved without a button transition.
    Move = 1,
    /// The primary button went down.
    Down = 2,
    /// The primary button went up.
    Up = 3,
    /// Cancel an active primary-pointer gesture without clicking.
    Cancel = 4,
}

impl BrowserPointerAction {
    /// Maps a wire tag.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::Move),
            2 => Some(Self::Down),
            3 => Some(Self::Up),
            4 => Some(Self::Cancel),
            _ => None,
        }
    }
}

/// Portable semantic keys accepted by the browser contract.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum BrowserSemanticKey {
    /// Directional up.
    Up = 1,
    /// Directional down.
    Down = 2,
    /// Directional left.
    Left = 3,
    /// Directional right.
    Right = 4,
    /// Primary/select action.
    Select = 5,
    /// Navigate back.
    Back = 6,
    /// Move focus forward.
    Tab = 7,
    /// Move focus backward.
    ShiftTab = 8,
    /// Dismiss/cancel.
    Escape = 9,
    /// Page upward.
    PageUp = 10,
    /// Page downward.
    PageDown = 11,
    /// Move to start.
    Home = 12,
    /// Move to end.
    End = 13,
    /// Refresh the current page.
    Refresh = 14,
}

impl BrowserSemanticKey {
    /// Maps a wire tag.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::Up),
            2 => Some(Self::Down),
            3 => Some(Self::Left),
            4 => Some(Self::Right),
            5 => Some(Self::Select),
            6 => Some(Self::Back),
            7 => Some(Self::Tab),
            8 => Some(Self::ShiftTab),
            9 => Some(Self::Escape),
            10 => Some(Self::PageUp),
            11 => Some(Self::PageDown),
            12 => Some(Self::Home),
            13 => Some(Self::End),
            14 => Some(Self::Refresh),
            _ => None,
        }
    }
}

/// One portable browser input event.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum BrowserInputEvent {
    /// Pointer input tied to the preview/navigation from which the user acted.
    Pointer {
        /// Pointer transition.
        action: BrowserPointerAction,
        /// Positive navigation reference.
        navigation_id: i64,
        /// Positive preview frame reference.
        frame_id: i64,
        /// Normalised horizontal coordinate, 0 through 65,535.
        x: u16,
        /// Normalised vertical coordinate, 0 through 65,535.
        y: u16,
        /// Primary pointer bit; only zero or one is valid.
        buttons: i32,
    },
    /// Scroll input tied to the preview/navigation from which the user acted.
    Scroll {
        /// Positive navigation reference.
        navigation_id: i64,
        /// Positive preview frame reference.
        frame_id: i64,
        /// Normalised horizontal coordinate.
        x: u16,
        /// Normalised vertical coordinate.
        y: u16,
        /// Horizontal scroll delta.
        delta_x: i32,
        /// Vertical scroll delta.
        delta_y: i32,
    },
    /// A portable semantic key.
    SemanticKey(BrowserSemanticKey),
    /// Explicit text input. It must never be echoed into browser state or diagnostics.
    Text(String),
}

impl BrowserInputEvent {
    /// Stable union tag on the wire.
    #[must_use]
    pub fn event_id(&self) -> u8 {
        match self {
            Self::Pointer { .. } => 1,
            Self::Scroll { .. } => 2,
            Self::SemanticKey(_) => 3,
            Self::Text(_) => 4,
        }
    }
}

/// Browser page lifecycle state.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum BrowserLoadState {
    /// No browser page is active.
    Idle = 1,
    /// The receiver is loading a page.
    Loading = 2,
    /// A page completed successfully.
    Loaded = 3,
    /// The receiver reported a safe, bounded loading failure.
    Failed = 4,
    /// The browser surface is closing or has been closed.
    Closed = 5,
}

impl BrowserLoadState {
    /// Maps a wire tag.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::Idle),
            2 => Some(Self::Loading),
            3 => Some(Self::Loaded),
            4 => Some(Self::Failed),
            5 => Some(Self::Closed),
            _ => None,
        }
    }
}

/// Passive preview availability for the current secure browser session.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum BrowserPreviewState {
    /// The user has not opted in or disabled preview.
    Disabled = 1,
    /// Preview is enabled under its bounded policy.
    Enabled = 2,
    /// The receiver cannot capture the WebView safely.
    Unavailable = 3,
}

impl BrowserPreviewState {
    /// Maps a wire tag.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::Disabled),
            2 => Some(Self::Enabled),
            3 => Some(Self::Unavailable),
            _ => None,
        }
    }
}

/// Native browser-dialog types observed remotely and explicitly answered.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum BrowserDialogType {
    /// An informational JavaScript alert.
    Alert = 1,
    /// A JavaScript confirm request.
    Confirm = 2,
    /// A JavaScript prompt request.
    Prompt = 3,
    /// A before-unload confirmation.
    BeforeUnload = 4,
}

impl BrowserDialogType {
    /// Maps a wire tag.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::Alert),
            2 => Some(Self::Confirm),
            3 => Some(Self::Prompt),
            4 => Some(Self::BeforeUnload),
            _ => None,
        }
    }
}

/// Returns whether a message belongs to the v3 browser workspace family.
#[must_use]
pub fn is_workspace_message(message: &Message) -> bool {
    matches!(
        message,
        Message::BrowserWorkspaceCommand { .. }
            | Message::BrowserWorkspaceState { .. }
            | Message::BrowserWorkspaceInput { .. }
            | Message::BrowserWorkspaceResize { .. }
            | Message::BrowserWorkspaceGeometry { .. }
    )
}

/// Structural WireGuard config check used by network-command validation.
///
/// Config text must never be logged. This only looks for `[Interface]` and `[Peer]` section
/// headers so incomplete Set commands fail closed at the wire layer.
#[must_use]
pub fn is_valid_wireguard_config(config_text: &str) -> bool {
    if config_text.trim().is_empty() {
        return false;
    }
    let mut has_interface = false;
    let mut has_peer = false;
    for line in config_text.lines() {
        let trimmed = line.trim();
        if trimmed == "[Interface]" {
            has_interface = true;
        } else if trimmed == "[Peer]" {
            has_peer = true;
        }
    }
    has_interface && has_peer
}

/// Returns whether a message is reserved for the authenticated TLS browser session.
#[must_use]
pub fn is_browser_message(message: &Message) -> bool {
    matches!(
        message,
        Message::BrowserCapability { .. }
            | Message::BrowserCommand { .. }
            | Message::BrowserInput { .. }
            | Message::BrowserState { .. }
            | Message::BrowserPreview { .. }
            | Message::BrowserDialog { .. }
            | Message::BrowserDialogReply { .. }
            | Message::BrowserTabCommand { .. }
            | Message::BrowserTabState { .. }
            | Message::BrowserViewCommand { .. }
            | Message::BrowserViewState { .. }
            | Message::BrowserFavicon { .. }
            | Message::BrowserLibraryCommand { .. }
            | Message::BrowserLibraryState { .. }
            | Message::BrowserProfileCommand { .. }
            | Message::BrowserProfileState { .. }
            | Message::BrowserNetworkCommand { .. }
            | Message::BrowserNetworkState { .. }
            | Message::BrowserWorkspaceCommand { .. }
            | Message::BrowserWorkspaceState { .. }
            | Message::BrowserWorkspaceInput { .. }
            | Message::BrowserWorkspaceResize { .. }
            | Message::BrowserWorkspaceGeometry { .. }
    )
}

/// Returns whether a message-type number belongs to the browser family.
#[must_use]
pub const fn is_browser_type(type_id: u16) -> bool {
    type_id >= 14 && type_id <= 36
}

/// Returns whether a message is forbidden on the ordinary cast socket.
#[must_use]
pub fn is_forbidden_on_ordinary_channel(message: &Message) -> bool {
    is_browser_message(message)
        || matches!(
            message,
            Message::Surface {
                mode: super::SurfaceMode::Browser,
                ..
            }
        )
}

/// Returns whether a value is legal at a declared payload version.
#[must_use]
pub fn is_allowed_at_version(protocol_version: u16, message: &Message) -> bool {
    if matches!(message, Message::BrowserWorkspaceResize { .. } | Message::BrowserWorkspaceGeometry { .. }) && protocol_version < 4 { return false; }
    if is_workspace_message(message) && protocol_version < 3 {
        return false;
    }
    protocol_version >= 2 || !is_forbidden_on_ordinary_channel(message)
}
