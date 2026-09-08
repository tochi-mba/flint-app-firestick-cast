//! Wire-level local-media values shared by the host and receiver.
//!
//! These types are intentionally separate from [`super::Message`]. Keeping the small closed
//! value sets here makes the browser-specific additions below stay reviewable without turning
//! `wire/mod.rs` into a thousand-line protocol dump.

/// Receiver local-media actions.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum MediaAction {
    /// Load and play the selected source.
    Load = 1,
    /// Return the receiver to its idle surface.
    Clear = 2,
}

impl MediaAction {
    /// Maps a wire tag.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::Load),
            2 => Some(Self::Clear),
            _ => None,
        }
    }
}

/// The receiver surface to show.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum SurfaceMode {
    /// Receiver idle surface.
    Idle = 1,
    /// Receiver media-player surface.
    Player = 2,
    /// Receiver live mirror surface.
    Mirror = 3,
    /// Receiver presentation surface.
    Presentation = 4,
    /// Receiver-local browser surface. It requires protocol version 2.
    Browser = 5,
}

impl SurfaceMode {
    /// Maps a wire tag.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::Idle),
            2 => Some(Self::Player),
            3 => Some(Self::Mirror),
            4 => Some(Self::Presentation),
            5 => Some(Self::Browser),
            _ => None,
        }
    }
}

/// Receiver playback state.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum PlaybackState {
    /// No source is loaded.
    Idle = 1,
    /// The receiver is loading media.
    Buffering = 2,
    /// The receiver is presenting media.
    Playing = 3,
    /// Playback is paused.
    Paused = 4,
    /// The source reached its end.
    Ended = 5,
    /// The receiver could not load or play the source.
    Error = 6,
}

impl PlaybackState {
    /// Maps a wire tag.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::Idle),
            2 => Some(Self::Buffering),
            3 => Some(Self::Playing),
            4 => Some(Self::Paused),
            5 => Some(Self::Ended),
            6 => Some(Self::Error),
            _ => None,
        }
    }
}
