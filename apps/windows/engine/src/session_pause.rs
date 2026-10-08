//! Pausing a share for privacy.
//!
//! A pause is a decision about what happens between two frames, so it lives here rather than in
//! the shell. While paused, capture keeps running - frames are taken and given straight back - so
//! the desktop duplication stays healthy and a lock screen still recovers, but nothing captured
//! after the pause is ever handed to the encoder.

use super::{packed_bgra_len, MirrorSession, SessionError, Tick};
use crate::encode::video::{EncodedFrame, FrameData, SourceFrame, VideoEncoder};
use crate::pacing::PacingClock;

/// What a share does.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub enum Pause {
    /// Sending the screen.
    #[default]
    Running,
    /// Paused, and the TV keeps showing the last picture it was sent.
    Holding,
    /// Paused, and the TV shows a black screen: one black frame is sent, then nothing.
    Blanking,
}

/// What pausing needs: the black frame, kept from the session's start so pausing never allocates.
#[derive(Debug, Default)]
pub(super) struct PauseState {
    pub(super) pause: Pause,
    pub(super) black: Vec<u8>,
    pub(super) black_pending: bool,
    /// Whether an access unit was produced while paused and not sent, which leaves the TV's
    /// decoder without a frame later ones may refer to.
    pub(super) dropped: bool,
    pub(super) last_presentation_time_us: i64,
}

impl PauseState {
    /// A running share whose black frame is `width` by `height`.
    pub(super) fn new(width: u32, height: u32) -> Self {
        Self {
            black: packed_bgra_len(width, height)
                .map(|len| vec![0; len])
                .unwrap_or_default(),
            ..Self::default()
        }
    }
}

impl<S: super::FrameSource, E: VideoEncoder, C: PacingClock> MirrorSession<S, E, C> {
    /// Pauses or resumes the share. Takes effect on the next tick.
    ///
    /// A frame the frame-rate cap was holding is from before the pause and is given back unsent.
    pub fn set_pause(&mut self, pause: Pause) {
        let state = &mut self.paused;
        if pause == state.pause {
            return;
        }

        if pause == Pause::Running {
            // Anything not sent while paused leaves the TV's decoder short of a frame the next
            // ones may refer to; a key frame starts it afresh. Otherwise nothing diverged.
            if state.dropped {
                self.force_key_frame = true;
            }
            state.dropped = false;
        } else {
            self.return_held();
        }

        let state = &mut self.paused;
        state.black_pending = pause == Pause::Blanking;
        state.pause = pause;
    }

    /// Whether the share is paused, and how.
    #[must_use]
    pub fn pause(&self) -> Pause {
        self.paused.pause
    }

    /// One tick while paused: give back whatever was captured, and send only the black frame and
    /// what the encoder still had from before the pause.
    pub(super) fn paused_tick(
        &mut self,
        arrived: Option<SourceFrame>,
    ) -> Result<Tick, SessionError> {
        if let Some(frame) = arrived {
            self.source.recycle_frame(frame);
        }

        let output = if self.paused.black_pending {
            self.paused.black_pending = false;
            self.submit_black()?
        } else {
            self.encoder.poll().map_err(SessionError::Encode)?
        };

        let Some(encoded) = output else {
            return Ok(Tick::Paused);
        };

        if self.paused.pause == Pause::Holding {
            self.paused.dropped = true;
            return Ok(Tick::Paused);
        }

        Ok(self.count(encoded))
    }

    /// Hands the encoder one black frame at its own size.
    fn submit_black(&mut self) -> Result<Option<EncodedFrame>, SessionError> {
        let (width, height) = self.encoder.output_size();
        let frame = SourceFrame {
            width,
            height,
            presentation_time_us: self.paused.last_presentation_time_us + 1,
            data: FrameData::Bgra {
                pixels: std::mem::take(&mut self.paused.black),
                stride: width * 4,
            },
        };
        // Paused before anything was ever sent, the black frame is the TV's first, so it must be
        // one the TV can decode on its own.
        let force_key_frame = self.force_key_frame || !self.started;
        let encoded = self.encoder.submit(&frame, force_key_frame);
        if let FrameData::Bgra { pixels, .. } = frame.data {
            self.paused.black = pixels;
        }

        encoded.map_err(SessionError::Encode)
    }
}
