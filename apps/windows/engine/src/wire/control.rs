//! Session-control vocabularies shared across transport messages.
//!
//! Separated from the message catalogue because they are a different kind of thing: small closed
//! sets, each with a `from_wire` that rejects anything it does not recognise. They are read when
//! checking one field's legality, not when following how a message is assembled.

/// How a client proves it may open a session.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum AuthMethod {
    /// A six-digit code the user reads off the television.
    PairingCode = 1,
    /// A token issued by a previous successful pairing.
    SessionToken = 2,
    /// A signature over a challenge, proving possession of a known key.
    PublicKeyProof = 3,
}

impl AuthMethod {
    /// Maps a wire tag.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::PairingCode),
            2 => Some(Self::SessionToken),
            3 => Some(Self::PublicKeyProof),
            _ => None,
        }
    }
}

/// Playback transport actions.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum TransportAction {
    /// Resume.
    Play = 1,
    /// Pause.
    Pause = 2,
    /// Stop and release.
    Stop = 3,
    /// Seek to an absolute position.
    SeekTo = 4,
    /// Next item in the queue.
    Next = 5,
    /// Previous item in the queue.
    Previous = 6,
}

impl TransportAction {
    /// Maps a wire tag.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::Play),
            2 => Some(Self::Pause),
            3 => Some(Self::Stop),
            4 => Some(Self::SeekTo),
            5 => Some(Self::Next),
            6 => Some(Self::Previous),
            _ => None,
        }
    }
}

/// Pointer event kinds.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum PointerAction {
    /// The pointer moved.
    Move = 1,
    /// A button went down.
    Down = 2,
    /// A button came up.
    Up = 3,
    /// A scroll wheel turned.
    Scroll = 4,
}

impl PointerAction {
    /// Maps a wire tag.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::Move),
            2 => Some(Self::Down),
            3 => Some(Self::Up),
            4 => Some(Self::Scroll),
            _ => None,
        }
    }
}

/// Key event kinds.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum KeyAction {
    /// The key went down.
    Down = 1,
    /// The key came up.
    Up = 2,
}

impl KeyAction {
    /// Maps a wire tag.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::Down),
            2 => Some(Self::Up),
            _ => None,
        }
    }
}

/// Why a session is closing.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum ByeReason {
    /// The user or the application ended it.
    Normal = 1,
    /// Credentials were rejected.
    AuthenticationFailed = 2,
    /// The version ranges did not overlap.
    UnsupportedVersion = 3,
    /// A peer sent something unreadable.
    ProtocolError = 4,
    /// The receiver shut down.
    ReceiverStopped = 5,
}

impl ByeReason {
    /// Maps a wire tag.
    #[must_use]
    pub fn from_id(id: u8) -> Option<Self> {
        match id {
            1 => Some(Self::Normal),
            2 => Some(Self::AuthenticationFailed),
            3 => Some(Self::UnsupportedVersion),
            4 => Some(Self::ProtocolError),
            5 => Some(Self::ReceiverStopped),
            _ => None,
        }
    }
}

/// One control event.
#[derive(Debug, Clone, PartialEq)]
pub enum ControlEvent {
    /// A transport command. `position_ms` is -1 for every action but `SeekTo`.
    Transport {
        /// What to do.
        action: TransportAction,
        /// Absolute position for `SeekTo`; -1 otherwise.
        position_ms: i64,
    },
    /// A pointer event, in normalised 0..1 surface coordinates.
    Pointer {
        /// What happened.
        action: PointerAction,
        /// Horizontal position.
        x: f32,
        /// Vertical position.
        y: f32,
        /// Button bitmask.
        buttons: i32,
    },
    /// A key event.
    Key {
        /// Down or up.
        action: KeyAction,
        /// Platform key code.
        key_code: i32,
    },
    /// Text input, for on-screen fields.
    Text(String),
    /// Volume, 0.0 to 1.0.
    Volume(f32),
}

impl ControlEvent {
    /// The wire tag for this event's variant.
    #[must_use]
    pub fn event_id(&self) -> u8 {
        match self {
            Self::Transport { .. } => 1,
            Self::Pointer { .. } => 2,
            Self::Key { .. } => 3,
            Self::Text(_) => 4,
            Self::Volume(_) => 5,
        }
    }
}
