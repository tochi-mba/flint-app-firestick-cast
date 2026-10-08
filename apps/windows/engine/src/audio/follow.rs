//! Which output sound is captured from, and getting it back when it goes away.
//!
//! "The default output" means whichever it is now: plugging in headphones moves capture to them
//! within a second. A named output that is unplugged leaves the share unavailable until it comes
//! back, and capture picks it up again on its own when it does.

use std::time::Duration;

use super::AudioError;

/// How often a lost output is looked for again, and the default output checked for a change.
pub(crate) const RECHECK: Duration = Duration::from_secs(1);

/// A running capture of one output.
pub(crate) trait Capture {
    /// Appends everything captured since the last read to `out`, as interleaved samples.
    ///
    /// # Errors
    /// [`AudioError::DeviceLost`] when the output went away; anything else ends the share.
    fn read(&mut self, out: &mut Vec<i16>) -> Result<(), AudioError>;

    /// The level Windows' own meter shows for the output, from 0 to 1.
    ///
    /// Measured before the output's mute, so it shows sound while capture may not hear it.
    fn meter(&self) -> f32;
}

/// This PC's outputs, or stand-ins for them in tests.
pub(crate) trait Outputs {
    /// The capture these outputs open.
    type Capture: Capture;

    /// Starts capturing `id`, or the default output when `None`; returns what it opened.
    ///
    /// # Errors
    /// [`AudioError::DeviceMissing`] or [`AudioError::NoOutput`] when there is nothing to open,
    /// and whatever else stopped capture from starting.
    fn open(&self, id: Option<&str>) -> Result<(Self::Capture, String), AudioError>;

    /// The default output's identity now, or `None` when there is no output.
    fn default_id(&self) -> Option<String>;
}

/// What happened to the output during a read.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum Change {
    /// Nothing worth saying: still capturing, or moved to a new default without a gap.
    None,
    /// The output went away and there is nothing to capture until it comes back.
    Lost,
    /// Capturing again after a loss.
    Back,
}

/// Captures from the chosen output, following it as outputs come and go.
pub(crate) struct Follower<O: Outputs> {
    outputs: O,
    named: Option<String>,
    capture: Option<O::Capture>,
    id: String,
    checked_at: Duration,
}

impl<O: Outputs> Follower<O> {
    /// Starts capturing `named`, or the default output when `None`.
    ///
    /// # Errors
    /// Whatever stopped the first capture from starting: a share never starts without sound to
    /// capture.
    pub(crate) fn open(outputs: O, named: Option<String>) -> Result<Self, AudioError> {
        let (capture, id) = outputs.open(named.as_deref())?;
        Ok(Self {
            outputs,
            named,
            capture: Some(capture),
            id,
            checked_at: Duration::ZERO,
        })
    }

    /// Appends what was captured to `out`; `now` is the time since the share started.
    ///
    /// # Errors
    /// Anything the capture failed with other than its output going away.
    pub(crate) fn read(&mut self, now: Duration, out: &mut Vec<i16>) -> Result<Change, AudioError> {
        if let Some(capture) = self.capture.as_mut() {
            match capture.read(out) {
                Ok(()) => {}
                Err(AudioError::DeviceLost) => {
                    out.clear();
                    self.capture = None;
                    self.checked_at = now;
                    // The default output may already have moved, as when headphones are unplugged.
                    return Ok(if self.reopen() {
                        Change::None
                    } else {
                        Change::Lost
                    });
                }
                Err(error) => return Err(error),
            }
        }

        if now.saturating_sub(self.checked_at) < RECHECK {
            return Ok(Change::None);
        }
        self.checked_at = now;

        if self.capture.is_none() {
            return Ok(if self.reopen() {
                Change::Back
            } else {
                Change::None
            });
        }
        if self.named.is_none() && self.outputs.default_id().is_some_and(|id| id != self.id) {
            // A new default that will not open leaves capture where it was, and is tried again.
            let _ = self.reopen();
        }
        Ok(Change::None)
    }

    /// What Windows' meter shows for the output being captured; nothing while it is lost.
    pub(crate) fn meter(&self) -> f32 {
        self.capture.as_ref().map_or(0.0, Capture::meter)
    }

    /// Opens the chosen output again; whether it opened.
    fn reopen(&mut self) -> bool {
        match self.outputs.open(self.named.as_deref()) {
            Ok((capture, id)) => {
                self.capture = Some(capture);
                self.id = id;
                true
            }
            Err(_) => false,
        }
    }
}

#[cfg(test)]
#[path = "follow_tests.rs"]
mod tests;
