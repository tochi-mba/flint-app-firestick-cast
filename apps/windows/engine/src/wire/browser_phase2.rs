//! Cockpit message types for browser ids 21 and above.
//!
//! Separated from the v1 browser types because they carry a different compatibility promise: ids
//! 14 through 20 are what every receiver understands, while everything here arrives as a new type
//! id and a host enables it only after a receiver has demonstrably sent one.
//!
//! The channel predicates stay in `browser.rs` with the v1 types. They enumerate every family at
//! once, and a missing arm there is a security regression rather than a missing feature — the
//! message stops being forbidden on the plaintext channel.

use super::browser::BrowserLoadState;

/// Operations over the receiver-owned tab collection.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum BrowserTabAction {
    /// Create a new tab. A zero tab id asks the receiver to allocate the next id.
    New = 1,
    /// Close the named tab.
    Close = 2,
    /// Make the named tab active.
    Select = 3,
    /// Move the named tab one position in the direction selected by the receiver UI.
    Move = 4,
    /// Duplicate the named tab.
    Duplicate = 5,
}

impl BrowserTabAction {
    /// Maps a wire tag.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::New),
            2 => Some(Self::Close),
            3 => Some(Self::Select),
            4 => Some(Self::Move),
            5 => Some(Self::Duplicate),
            _ => None,
        }
    }
}

/// One bounded tab row in a browser tab-state snapshot.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BrowserTabStateEntry {
    /// Positive receiver-owned tab identifier.
    pub tab_id: i64,
    /// Current page load state.
    pub load_state: BrowserLoadState,
    /// Load progress from zero through one hundred.
    pub progress: u8,
    /// Whether this tab can move back in page history.
    pub can_go_back: bool,
    /// Whether this tab can move forward in page history.
    pub can_go_forward: bool,
    /// Whether the renderer was released under the tab memory budget.
    pub frozen: bool,
    /// Non-negative favicon id, or zero when no icon is available.
    pub favicon_id: i64,
    /// Safe, bounded canonical URL; empty only for an idle new tab.
    pub url: String,
    /// Safe, bounded page title.
    pub title: String,
}

/// Browser view-setting and find-in-page operations.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum BrowserViewAction {
    /// Set the text zoom percentage from the command value.
    SetZoom = 1,
    /// Select a user-agent mode from the command value.
    SetUa = 2,
    /// Select a dark-mode policy from the command value.
    SetDark = 3,
    /// Select cursor or focus input from the command value.
    SetInputMode = 4,
    /// Enter or leave fullscreen from a zero/one command value.
    SetFullscreen = 5,
    /// Begin a find-in-page operation using the command text.
    FindStart = 6,
    /// Move to the next find result.
    FindNext = 7,
    /// Move to the previous find result.
    FindPrev = 8,
    /// Clear the current find operation.
    FindClear = 9,
    /// Select a search engine from the value and optional custom template text.
    SetSearchEngine = 10,
}

impl BrowserViewAction {
    /// Maps a wire tag.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::SetZoom),
            2 => Some(Self::SetUa),
            3 => Some(Self::SetDark),
            4 => Some(Self::SetInputMode),
            5 => Some(Self::SetFullscreen),
            6 => Some(Self::FindStart),
            7 => Some(Self::FindNext),
            8 => Some(Self::FindPrev),
            9 => Some(Self::FindClear),
            10 => Some(Self::SetSearchEngine),
            _ => None,
        }
    }
}

/// User-agent profile applied by the receiver WebView.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum BrowserUserAgentMode {
    /// Television-oriented user agent.
    Tv = 1,
    /// Desktop-class user agent.
    Desktop = 2,
    /// Mobile user agent.
    Mobile = 3,
}

impl BrowserUserAgentMode {
    /// Maps a wire tag.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::Tv),
            2 => Some(Self::Desktop),
            3 => Some(Self::Mobile),
            _ => None,
        }
    }
}

/// Receiver page-colour policy.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum BrowserDarkMode {
    /// Follow the receiver system theme.
    FollowSystem = 1,
    /// Prefer the page's light presentation.
    Light = 2,
    /// Request supported algorithmic/page dark presentation.
    Dark = 3,
}

impl BrowserDarkMode {
    /// Maps a wire tag.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::FollowSystem),
            2 => Some(Self::Light),
            3 => Some(Self::Dark),
            _ => None,
        }
    }
}

/// How the TV D-pad interacts with the current page.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum BrowserInteractionMode {
    /// Move an on-screen pointer.
    Cursor = 1,
    /// Traverse the page's focus order.
    Focus = 2,
}

impl BrowserInteractionMode {
    /// Maps a wire tag.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::Cursor),
            2 => Some(Self::Focus),
            _ => None,
        }
    }
}

/// Search engines understood by the shared browser contract.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum BrowserSearchEngine {
    /// DuckDuckGo preset.
    DuckDuckGo = 1,
    /// Google preset.
    Google = 2,
    /// Bing preset.
    Bing = 3,
    /// A custom HTTPS template carried only by `SetSearchEngine`.
    Custom = 4,
}

impl BrowserSearchEngine {
    /// Maps a wire tag.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::DuckDuckGo),
            2 => Some(Self::Google),
            3 => Some(Self::Bing),
            4 => Some(Self::Custom),
            _ => None,
        }
    }
}

/// Receiver-owned browser-library operations.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum BrowserLibraryAction {
    /// Add or update a bookmark for the supplied URL and title.
    AddBookmark = 1,
    /// Remove the bookmark identified by URL.
    RemoveBookmark = 2,
    /// Clear only browsing history.
    ClearHistory = 3,
    /// Clear only bookmarks.
    ClearBookmarks = 4,
    /// Ask the receiver to publish its current bounded library snapshot.
    RequestSnapshot = 5,
}

impl BrowserLibraryAction {
    /// Maps a wire tag.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::AddBookmark),
            2 => Some(Self::RemoveBookmark),
            3 => Some(Self::ClearHistory),
            4 => Some(Self::ClearBookmarks),
            5 => Some(Self::RequestSnapshot),
            _ => None,
        }
    }
}

/// Kind tag carried by every browser-library row.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum BrowserLibraryEntryKind {
    /// User-saved bookmark.
    Bookmark = 1,
    /// Recently visited page.
    History = 2,
}

impl BrowserLibraryEntryKind {
    /// Maps a wire tag.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::Bookmark),
            2 => Some(Self::History),
            _ => None,
        }
    }
}

/// One bounded row in a receiver browser-library snapshot.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BrowserLibraryEntry {
    /// Bookmark or history discriminator.
    pub kind: BrowserLibraryEntryKind,
    /// Non-negative favicon id, or zero when no icon is available.
    pub favicon_id: i64,
    /// Non-negative Unix epoch timestamp in milliseconds.
    pub last_visited_ms: i64,
    /// Safe, bounded canonical URL.
    pub url: String,
    /// Safe, bounded page title.
    pub title: String,
}

/// Browser profile selection and TV profile-management operations.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum BrowserProfileAction {
    /// Select one persistent profile stored on the television.
    SelectTvProfile = 1,
    /// Create a persistent TV profile; the receiver allocates its identifier.
    CreateTvProfile = 2,
    /// Rename one persistent TV profile.
    RenameTvProfile = 3,
    /// Delete one persistent TV profile.
    DeleteTvProfile = 4,
    /// Use the connected device profile without persisting it on the television.
    SelectDevice = 5,
    /// Request the current profile-source and TV-profile catalog snapshot.
    RequestSnapshot = 6,
}

impl BrowserProfileAction {
    /// Maps a wire tag.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::SelectTvProfile),
            2 => Some(Self::CreateTvProfile),
            3 => Some(Self::RenameTvProfile),
            4 => Some(Self::DeleteTvProfile),
            5 => Some(Self::SelectDevice),
            6 => Some(Self::RequestSnapshot),
            _ => None,
        }
    }
}

/// Where the active browser profile is durably stored.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum BrowserProfileSource {
    /// The active profile is persistent on the television.
    Tv = 1,
    /// The active profile belongs to the connected device and is session-only on the TV.
    Device = 2,
}

impl BrowserProfileSource {
    /// Maps a wire tag.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::Tv),
            2 => Some(Self::Device),
            _ => None,
        }
    }
}

/// One bounded persistent browser profile stored on the television.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BrowserProfileEntry {
    /// Stable safe-ASCII identifier.
    pub profile_id: String,
    /// Trimmed user-visible name.
    pub name: String,
}

/// Host-to-receiver browser network/VPN operations.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum BrowserNetworkAction {
    /// Store or replace network settings for one TV profile.
    Set = 1,
    /// Clear stored network settings for one TV profile.
    Clear = 2,
    /// Request the current network/VPN snapshot for a profile or the active TV profile.
    RequestSnapshot = 3,
}

impl BrowserNetworkAction {
    /// Maps a wire tag.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::Set),
            2 => Some(Self::Clear),
            3 => Some(Self::RequestSnapshot),
            _ => None,
        }
    }
}

/// VPN provider carried by browser network settings.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum BrowserVpnProvider {
    /// No VPN provider configured.
    None = 0,
    /// WireGuard configuration text.
    WireGuard = 1,
}

impl BrowserVpnProvider {
    /// Maps a wire tag.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            0 => Some(Self::None),
            1 => Some(Self::WireGuard),
            _ => None,
        }
    }
}

/// Soft-fail VPN session state published to the host.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum BrowserVpnSessionState {
    /// No VPN session is active.
    Idle = 0,
    /// The system VPN consent prompt is required.
    NeedsConsent = 1,
    /// A tunnel connect is in progress.
    Connecting = 2,
    /// A tunnel is up.
    Connected = 3,
    /// The last connect attempt failed with a safe detail.
    Failed = 4,
    /// The device cannot prepare a VPN session.
    Unavailable = 5,
}

impl BrowserVpnSessionState {
    /// Maps a wire tag.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            0 => Some(Self::Idle),
            1 => Some(Self::NeedsConsent),
            2 => Some(Self::Connecting),
            3 => Some(Self::Connected),
            4 => Some(Self::Failed),
            5 => Some(Self::Unavailable),
            _ => None,
        }
    }
}

/// Browser workspace command actions.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
/// Wire representation of command action.
pub enum BrowserWorkspaceCommandAction {
    /// Focus an existing pane.
    Focus = 1,
    /// Create an independently browsing pane.
    OpenPane = 2,
    /// Close a pane and release its renderer.
    ClosePane = 3,
    /// Change the workspace layout.
    SetLayout = 4,
    /// Navigate a pane to a secure address.
    Navigate = 5,
    /// Reload the current page.
    Reload = 6,
    /// Navigate backward in page history.
    Back = 7,
    /// Navigate forward in page history.
    Forward = 8,
    /// Request per-pane audio mute.
    SetMute = 9,
    /// Dispatch a media play/pause request.
    PlayPause = 10,
    /// Choose page input or workspace controls.
    SetInteraction = 11,
    /// Enlarge the selected pane explicitly.
    EnterTheater = 12,
    /// Return from theater presentation.
    ExitTheater = 13,
    /// Request the authoritative workspace state.
    RequestSnapshot = 14,
    /// Move one pane to another slot without recreating its renderer.
    MovePane = 15,
}

impl BrowserWorkspaceCommandAction {
    /// Decodes a recognized wire tag; unknown values are rejected.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::Focus),
            2 => Some(Self::OpenPane),
            3 => Some(Self::ClosePane),
            4 => Some(Self::SetLayout),
            5 => Some(Self::Navigate),
            6 => Some(Self::Reload),
            7 => Some(Self::Back),
            8 => Some(Self::Forward),
            9 => Some(Self::SetMute),
            10 => Some(Self::PlayPause),
            11 => Some(Self::SetInteraction),
            12 => Some(Self::EnterTheater),
            13 => Some(Self::ExitTheater),
            14 => Some(Self::RequestSnapshot),
            15 => Some(Self::MovePane),
            _ => None,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
/// Wire representation of layout.
pub enum BrowserWorkspaceWireLayout {
    /// One page rectangle.
    Single = 1,
    /// Two side-by-side page rectangles.
    TwoColumns = 2,
    /// Two vertically stacked page rectangles.
    TwoRows = 3,
    /// Four page rectangles in a two-by-two grid.
    FourGrid = 4,
}

impl BrowserWorkspaceWireLayout {
    /// Decodes a recognized wire tag; unknown values are rejected.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::Single),
            2 => Some(Self::TwoColumns),
            3 => Some(Self::TwoRows),
            4 => Some(Self::FourGrid),
            _ => None,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
/// Wire representation of interaction mode.
pub enum BrowserWorkspaceWireInteractionMode {
    /// Input operates workspace controls.
    WorkspaceChrome = 1,
    /// Input operates the focused page.
    Page = 2,
}

impl BrowserWorkspaceWireInteractionMode {
    /// Decodes a recognized wire tag; unknown values are rejected.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::WorkspaceChrome),
            2 => Some(Self::Page),
            _ => None,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
/// Wire representation of pane residency.
pub enum BrowserWorkspaceWirePaneResidency {
    /// A native renderer currently exists.
    Live = 1,
    /// Page metadata exists without a live renderer.
    Suspended = 2,
    /// The renderer or operation failed.
    Failed = 3,
}

impl BrowserWorkspaceWirePaneResidency {
    /// Decodes a recognized wire tag; unknown values are rejected.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::Live),
            2 => Some(Self::Suspended),
            3 => Some(Self::Failed),
            _ => None,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
/// Wire representation of input kind.
pub enum BrowserWorkspaceInputKind {
    /// Semantic keyboard input.
    Key = 1,
    /// Composed text input.
    Text = 2,
}

impl BrowserWorkspaceInputKind {
    /// Decodes a recognized wire tag; unknown values are rejected.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::Key),
            2 => Some(Self::Text),
            _ => None,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
/// Wire representation of observed playback.
pub enum BrowserWorkspaceWireObservedPlayback {
    /// No reliable playback observation exists.
    Unknown = 1,
    /// Playback was observed as playing.
    Playing = 2,
    /// Playback was observed as paused.
    Paused = 3,
    /// Playback was observed as ended.
    Ended = 4,
    /// The receiver cannot provide playback observations.
    Unavailable = 5,
}

impl BrowserWorkspaceWireObservedPlayback {
    /// Decodes a recognized wire tag; unknown values are rejected.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::Unknown),
            2 => Some(Self::Playing),
            3 => Some(Self::Paused),
            4 => Some(Self::Ended),
            5 => Some(Self::Unavailable),
            _ => None,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
/// Wire representation of mute application.
pub enum BrowserWorkspaceWireMuteApplication {
    /// No mute request has been issued.
    NotRequested = 0,
    /// Mute must wait until a renderer exists.
    PendingRenderer = 1,
    /// Mute was requested but is not yet confirmed.
    Requested = 2,
    /// The renderer confirmed the requested mute state.
    AppliedToRenderer = 3,
    /// The renderer does not support per-page mute.
    Unsupported = 4,
    /// The renderer or operation failed.
    Failed = 5,
}

impl BrowserWorkspaceWireMuteApplication {
    /// Decodes a recognized wire tag; unknown values are rejected.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            0 => Some(Self::NotRequested),
            1 => Some(Self::PendingRenderer),
            2 => Some(Self::Requested),
            3 => Some(Self::AppliedToRenderer),
            4 => Some(Self::Unsupported),
            5 => Some(Self::Failed),
            _ => None,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
/// Receiver-owned metadata for one independently browsing pane.
pub struct BrowserWorkspacePaneStateEntry {
    /// Target pane identifier.
    pub pane_id: i64,
    /// Zero-based position in the selected layout.
    pub slot: u8,
    /// Whether the pane has a live renderer, is suspended, or failed.
    pub residency: BrowserWorkspaceWirePaneResidency,
    /// Canonical HTTPS address, or empty when no navigation is requested.
    pub url: String,
    /// Receiver-reported page title.
    pub title: String,
    /// Whether navigation is still loading.
    pub loading: bool,
    /// Page load percentage from zero through one hundred.
    pub progress: u8,
    /// Whether native page history permits backward navigation.
    pub can_go_back: bool,
    /// Whether native page history permits forward navigation.
    pub can_go_forward: bool,
    /// Requested audio-mute state, not proof of application.
    pub desired_muted: bool,
    /// Receiver-confirmed disposition of the mute request.
    pub mute_application: BrowserWorkspaceWireMuteApplication,
    /// Playback observation; unknown is never inferred as playing.
    pub observed_playback: BrowserWorkspaceWireObservedPlayback,
}
