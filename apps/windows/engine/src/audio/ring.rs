//! The queue of encoded sound between the capture thread and whoever sends it to the TV.
//!
//! Fixed size and allocated once: pushing and popping copy into slots that already exist, so the
//! capture thread never touches the allocator. When the network falls behind and the queue fills,
//! the oldest packet is dropped and counted, because sound that is late is worth less than sound
//! that is current. A delay can hold every packet back by the same amount, which is how picture and
//! sound are lined up when the TV shows the picture later than it plays the sound.

use std::collections::VecDeque;
use std::sync::{Condvar, Mutex};
use std::time::{Duration, Instant};

/// The largest packet a slot holds. A stereo AAC frame is at most 1536 bytes.
pub const SLOT_BYTES: usize = 2048;

/// What became of a push.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Pushed {
    /// The packet is queued.
    Queued,
    /// The packet is queued, and the oldest one was dropped to make room.
    DroppedOldest,
    /// The packet was larger than a slot and was refused rather than cut short.
    TooLarge,
}

/// Counts a queue keeps.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct RingStats {
    /// Packets dropped because the queue was full.
    pub dropped: u64,
    /// Packets refused because they were larger than a slot.
    pub refused: u64,
}

struct Slot {
    data: Vec<u8>,
    presentation_time_us: i64,
    release_at: Instant,
}

struct State {
    queued: VecDeque<Slot>,
    free: Vec<Slot>,
    stats: RingStats,
    delay: Duration,
}

/// A fixed-size queue of encoded packets, safe to share between two threads.
pub struct PacketRing {
    state: Mutex<State>,
    ready: Condvar,
}

impl PacketRing {
    /// A queue of `slots` packets, every slot allocated now.
    #[must_use]
    pub fn new(slots: usize) -> Self {
        let now = Instant::now();
        let free = (0..slots.max(1))
            .map(|_| Slot {
                data: Vec::with_capacity(SLOT_BYTES),
                presentation_time_us: 0,
                release_at: now,
            })
            .collect();
        Self {
            state: Mutex::new(State {
                queued: VecDeque::with_capacity(slots.max(1)),
                free,
                stats: RingStats::default(),
                delay: Duration::ZERO,
            }),
            ready: Condvar::new(),
        }
    }

    /// Holds every packet pushed from now on back by `delay` before it can be popped.
    pub fn set_delay(&self, delay: Duration) {
        self.lock().delay = delay;
    }

    /// The counts so far.
    #[must_use]
    pub fn stats(&self) -> RingStats {
        self.lock().stats
    }

    /// Queues a copy of `data`, timed at `presentation_time_us`.
    pub fn push(&self, data: &[u8], presentation_time_us: i64) -> Pushed {
        let mut state = self.lock();
        if data.len() > SLOT_BYTES {
            state.stats.refused += 1;
            return Pushed::TooLarge;
        }

        let mut outcome = Pushed::Queued;
        let mut slot = if let Some(slot) = state.free.pop() {
            slot
        } else {
            state.stats.dropped += 1;
            outcome = Pushed::DroppedOldest;
            // Never empty here: every slot is either free or queued.
            state
                .queued
                .pop_front()
                .expect("a full queue has a packet to drop")
        };

        slot.data.clear();
        slot.data.extend_from_slice(data);
        slot.presentation_time_us = presentation_time_us;
        // Never earlier than the packet before it, so a shorter delay set mid-stream cannot
        // reorder what is already queued.
        let after_previous = state.queued.back().map(|previous| previous.release_at);
        slot.release_at =
            (Instant::now() + state.delay).max(after_previous.unwrap_or_else(Instant::now));
        state.queued.push_back(slot);
        drop(state);
        self.ready.notify_one();
        outcome
    }

    /// Waits up to `timeout` for a packet whose time has come, copying it into `out`.
    ///
    /// Returns its presentation time, or `None` when none was ready in time.
    pub fn pop(&self, out: &mut Vec<u8>, timeout: Duration) -> Option<i64> {
        let deadline = Instant::now() + timeout;
        let mut state = self.lock();
        loop {
            let now = Instant::now();
            if let Some(release_at) = state.queued.front().map(|slot| slot.release_at) {
                if release_at <= now {
                    let slot = state.queued.pop_front().expect("checked above");
                    out.clear();
                    out.extend_from_slice(&slot.data);
                    let time = slot.presentation_time_us;
                    state.free.push(slot);
                    return Some(time);
                }
            }

            if now >= deadline {
                return None;
            }

            let wake = state
                .queued
                .front()
                .map_or(deadline, |slot| slot.release_at.min(deadline));
            state = self
                .ready
                .wait_timeout(state, wake.saturating_duration_since(now))
                .unwrap_or_else(std::sync::PoisonError::into_inner)
                .0;
        }
    }

    /// Empties the queue, as a pause does: what was waiting would play late.
    pub fn clear(&self) {
        let mut state = self.lock();
        while let Some(slot) = state.queued.pop_front() {
            state.free.push(slot);
        }
    }

    fn lock(&self) -> std::sync::MutexGuard<'_, State> {
        self.state
            .lock()
            .unwrap_or_else(std::sync::PoisonError::into_inner)
    }
}

#[cfg(test)]
#[path = "ring_tests.rs"]
mod tests;
