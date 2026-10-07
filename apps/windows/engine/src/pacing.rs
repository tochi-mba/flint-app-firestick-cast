//! Holding a mirror to a frame-rate cap.
//!
//! Desktop duplication hands over a frame every time the screen changes, which on a 144 Hz display
//! is up to 144 times a second whatever frame rate the encoder was configured for. The pacer
//! decides when the next frame may go; the session keeps the newest frame that arrived too early
//! and sends it the moment the interval has passed, so a cap never leaves the TV showing a picture
//! older than the desktop.
//!
//! Time comes from a [`PacingClock`] so the rules can be tested without sleeping.

use std::time::{Duration, Instant};

/// A monotonic time source for pacing.
pub trait PacingClock {
    /// Time elapsed since some fixed point. Only differences between readings matter.
    fn now(&self) -> Duration;
}

/// The real clock: time since the session was built.
#[derive(Debug, Clone, Copy)]
pub struct MonotonicClock {
    origin: Instant,
}

impl MonotonicClock {
    /// A clock that starts counting now.
    #[must_use]
    pub fn new() -> Self {
        Self {
            origin: Instant::now(),
        }
    }
}

impl Default for MonotonicClock {
    fn default() -> Self {
        Self::new()
    }
}

impl PacingClock for MonotonicClock {
    fn now(&self) -> Duration {
        self.origin.elapsed()
    }
}

/// When the next frame may be sent.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct FramePacer {
    frames_per_second: u32,
    interval: Option<Duration>,
    next_due: Option<Duration>,
}

impl FramePacer {
    /// No cap: every frame is due as soon as it arrives.
    pub const UNCAPPED: Self = Self {
        frames_per_second: 0,
        interval: None,
        next_due: None,
    };

    /// A cap of `frames_per_second`. Zero means no cap.
    #[must_use]
    pub fn capped_at(frames_per_second: u32) -> Self {
        if frames_per_second == 0 {
            return Self::UNCAPPED;
        }

        Self {
            frames_per_second,
            interval: Some(Duration::from_secs(1) / frames_per_second),
            next_due: None,
        }
    }

    /// The cap in frames a second, or zero when there is none.
    #[must_use]
    pub fn frames_per_second(&self) -> u32 {
        self.frames_per_second
    }

    /// Whether a frame may be sent at `now`.
    #[must_use]
    pub fn is_due(&self, now: Duration) -> bool {
        self.next_due.is_none_or(|due| now >= due)
    }

    /// How long until a frame may be sent, which is zero when one may be sent now.
    #[must_use]
    pub fn wait_before_due(&self, now: Duration) -> Duration {
        self.next_due
            .map_or(Duration::ZERO, |due| due.saturating_sub(now))
    }

    /// Records that a frame was sent at `now`.
    pub fn sent(&mut self, now: Duration) {
        let Some(interval) = self.interval else {
            return;
        };

        self.next_due = Some(match self.next_due {
            // On time, or late by less than half a frame: keep the cadence. Measuring from the
            // send instead would let every millisecond the wait is rounded up to accumulate, and a
            // 30 a second cap would settle near 29.
            Some(due) if now < due + interval / 2 => due + interval,
            // Long after the last frame - the desktop was still - so the cadence starts again here
            // rather than allowing a burst to catch up on frames that never existed.
            _ => now + interval,
        });
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const fn ms(value: u64) -> Duration {
        Duration::from_millis(value)
    }

    #[test]
    fn without_a_cap_every_moment_is_due() {
        let mut pacer = FramePacer::capped_at(0);
        assert_eq!(pacer, FramePacer::UNCAPPED);

        pacer.sent(ms(5));

        assert!(pacer.is_due(ms(5)));
        assert_eq!(pacer.wait_before_due(ms(5)), Duration::ZERO);
        assert_eq!(pacer.frames_per_second(), 0);
    }

    #[test]
    fn the_first_frame_is_due_at_once_and_the_next_one_interval_later() {
        let mut pacer = FramePacer::capped_at(20);
        assert!(pacer.is_due(Duration::ZERO));

        pacer.sent(ms(10));

        assert!(!pacer.is_due(ms(59)));
        assert_eq!(pacer.wait_before_due(ms(40)), ms(20));
        assert!(pacer.is_due(ms(60)));
        assert_eq!(pacer.wait_before_due(ms(70)), Duration::ZERO);
        assert_eq!(pacer.frames_per_second(), 20);
    }

    #[test]
    fn a_slightly_late_send_keeps_the_cadence() {
        let mut pacer = FramePacer::capped_at(20);
        pacer.sent(ms(0));

        pacer.sent(ms(74));

        assert!(
            pacer.is_due(ms(100)),
            "due on the 50 ms grid, not 50 ms after 74"
        );
        assert!(!pacer.is_due(ms(99)));
    }

    #[test]
    fn a_send_long_after_the_last_restarts_the_cadence() {
        let mut pacer = FramePacer::capped_at(20);
        pacer.sent(ms(0));

        pacer.sent(ms(75));

        assert!(
            !pacer.is_due(ms(124)),
            "half a frame late or more starts again from the send"
        );
        assert!(pacer.is_due(ms(125)));
    }

    #[test]
    fn the_real_clock_moves_forward() {
        let clock = MonotonicClock::default();
        let first = clock.now();
        assert!(clock.now() >= first);
    }
}
