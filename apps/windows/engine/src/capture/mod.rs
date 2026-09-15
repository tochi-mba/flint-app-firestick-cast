//! Screen capture.
//!
//! The source end of the mirror path. A captured frame is a GPU texture, and it stays on the GPU
//! all the way into the encoder: reading it back to system memory would cost more than the entire
//! rest of the host-side budget.

#[cfg(windows)]
pub mod duplication;
#[cfg(windows)]
pub mod mipscale;
#[cfg(windows)]
pub mod readback;

/// Why capture could not start or continue.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum CaptureError {
    /// No adapter on this host drives a display, so there is nothing to duplicate.
    NoDisplayAdapter,
    /// The requested display index does not exist.
    NoSuchOutput(u32),
    /// The platform refused, with its own message.
    Platform(String),
    /// Another process holds the duplication, or the desktop switched under us.
    ///
    /// Recoverable: the caller re-creates the duplication and continues. This happens routinely on
    /// a lock screen, a UAC prompt, or a resolution change, and is a normal state transition
    /// rather than a failure.
    Interrupted,
}

impl std::fmt::Display for CaptureError {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::NoDisplayAdapter => write!(formatter, "no graphics adapter drives a display"),
            Self::NoSuchOutput(index) => write!(formatter, "display {index} does not exist"),
            Self::Platform(message) => write!(formatter, "{message}"),
            Self::Interrupted => write!(formatter, "the desktop duplication was interrupted"),
        }
    }
}

impl std::error::Error for CaptureError {}

/// What a capture source reports about itself once it is open.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CaptureFormat {
    /// Frame width in pixels.
    pub width: u32,
    /// Frame height in pixels.
    pub height: u32,
    /// The adapter the frames live on, matching the DXGI adapter LUID.
    pub adapter_luid: i64,
}

/// The outcome of asking for the next frame.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum FrameOutcome {
    /// A new frame arrived.
    Captured,
    /// The timeout elapsed with no screen change.
    ///
    /// Not an error, and common: the desktop only produces a frame when something moves. The
    /// caller repeats its previous frame or simply waits.
    Unchanged,
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn errors_describe_themselves_for_the_diagnostics_page() {
        assert_eq!(
            CaptureError::NoSuchOutput(3).to_string(),
            "display 3 does not exist"
        );
        assert_eq!(
            CaptureError::Platform("device removed".into()).to_string(),
            "device removed"
        );
    }

    #[test]
    fn an_interruption_reads_as_recoverable_rather_than_fatal() {
        // A lock screen or a UAC prompt lands here, and the wording must not imply a crash.
        assert!(CaptureError::Interrupted
            .to_string()
            .contains("interrupted"));
    }
}
