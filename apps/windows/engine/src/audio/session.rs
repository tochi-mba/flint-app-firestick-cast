//! A running sound share: one thread captures, encodes and queues; the caller takes packets.
//!
//! Sound never holds the picture up. Starting it, failing or being unavailable is reported as a
//! state the shell shows; nothing here touches the mirror session.

use std::sync::atomic::{AtomicBool, AtomicU32, AtomicU64, AtomicU8, Ordering};
use std::sync::{mpsc, Arc, Mutex};
use std::thread::JoinHandle;
use std::time::{Duration, Instant};

use super::aac::{AacEncoder, AudioPacket, CHANNELS, SAMPLE_RATE};
use super::capture::WindowsOutputs;
use super::follow::{Change, Follower, Outputs};
use super::ring::PacketRing;
use super::AudioError;

/// Why a share failed to start when its thread ended before saying how starting went.
const ENDED_EARLY: &str = "the sound thread ended before it started";

/// How often the capture thread looks for new sound.
const POLL: Duration = Duration::from_millis(10);

/// How far behind the wall clock the sound may fall before silence is added: 20 ms.
const SLACK_FRAMES: u64 = SAMPLE_RATE as u64 / 50;

/// Packets the queue holds: about two seconds of sound.
const QUEUE_PACKETS: usize = 96;

/// A level below which sound counts as silence: about -60 dBFS.
const SILENCE: f32 = 0.001;

/// How long after the last sound it still counts as sounding.
const SOUNDING_HOLD: Duration = Duration::from_secs(1);

/// What a sound share is doing.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum SoundState {
    /// Running, and nothing is playing on this PC yet.
    Ready = 1,
    /// Running, and sound is reaching the TV.
    Sounding = 2,
    /// Stopped by a problem, which [`AudioSession::problem`] describes.
    Unavailable = 3,
}

/// What a sound share is asked to do.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AudioOptions {
    /// The output to capture, or `None` for whichever is the default at the time.
    pub device_id: Option<String>,
    /// The data rate, one of [`super::aac::BITRATES_KBPS`].
    pub bitrate_kbps: u32,
    /// Added to every packet's time, so sound shares the picture's clock.
    pub start_offset_us: i64,
    /// How long every packet is held back, to line sound up with a picture the TV shows late.
    pub delay_ms: u32,
}

/// Counters for the Screen page.
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct AudioStats {
    /// Packets encoded.
    pub packets: u64,
    /// Packets dropped because the queue was full.
    pub dropped: u64,
    /// The captured level, from 0 to 1.
    pub level: f32,
    /// The level Windows' meter shows for the output, from 0 to 1, measured before its mute.
    ///
    /// Sound on the meter while the captured level is silent means capture cannot hear the
    /// output, as happens on some outputs once they are muted.
    pub meter: f32,
}

/// Keeps sound on the wall clock.
///
/// Windows delivers nothing while nothing plays, so without this a pause in the sound would shift
/// everything after it earlier on the TV than the picture it belongs to.
#[derive(Debug, Default)]
pub(crate) struct SampleClock {
    fed: u64,
}

impl SampleClock {
    /// The frames of silence to add before `captured` new frames, `elapsed` after the start.
    pub(crate) fn missing(&self, elapsed: Duration, captured: u64) -> u64 {
        let expected = u64::try_from(elapsed.as_micros()).unwrap_or(u64::MAX)
            * u64::from(SAMPLE_RATE)
            / 1_000_000;
        let have = self.fed + captured;
        if expected > have + SLACK_FRAMES {
            expected - have
        } else {
            0
        }
    }

    /// Records that `frames` more went to the encoder.
    pub(crate) fn fed(&mut self, frames: u64) {
        self.fed += frames;
    }
}

/// Tells "nothing playing yet" from "sending sound", without flickering between them.
#[derive(Debug, Default)]
pub(crate) struct Liveness {
    last_sound: Option<Duration>,
}

impl Liveness {
    /// The state after hearing `level` at `now`.
    pub(crate) fn observe(&mut self, now: Duration, level: f32) -> SoundState {
        if level > SILENCE {
            self.last_sound = Some(now);
        }
        match self.last_sound {
            Some(at) if now.saturating_sub(at) < SOUNDING_HOLD => SoundState::Sounding,
            _ => SoundState::Ready,
        }
    }
}

/// What the encoder is given: `missing` frames of silence, then what was captured, or silence in
/// its place while paused, so nothing captured during a pause is ever encoded while the encoder's
/// clock keeps running.
pub(crate) fn fill_feed(feed: &mut Vec<i16>, missing: u64, captured: &[i16], paused: bool) {
    feed.clear();
    feed.resize(usize::try_from(missing).unwrap_or(0) * CHANNELS as usize, 0);
    if paused {
        feed.resize(feed.len() + captured.len(), 0);
    } else {
        feed.extend_from_slice(captured);
    }
}

/// The level of interleaved 16-bit samples, from 0 to 1.
pub(crate) fn level(samples: &[i16]) -> f32 {
    if samples.is_empty() {
        return 0.0;
    }
    let squares: f64 = samples
        .iter()
        .map(|&sample| f64::from(sample).powi(2))
        .sum();
    ((squares / samples.len() as f64).sqrt() / 32_768.0) as f32
}

struct Shared {
    stop: AtomicBool,
    paused: AtomicBool,
    state: AtomicU8,
    level: AtomicU32,
    meter: AtomicU32,
    packets: AtomicU64,
    problem: Mutex<Option<String>>,
}

/// A running sound share.
pub struct AudioSession {
    ring: Arc<PacketRing>,
    shared: Arc<Shared>,
    worker: Option<JoinHandle<()>>,
    config: Vec<u8>,
}

impl AudioSession {
    /// Starts capturing and encoding, returning once the first packet's setup is known.
    ///
    /// # Errors
    /// Whatever stopped the encoder or the capture from starting.
    pub fn start(options: AudioOptions) -> Result<Self, AudioError> {
        Self::start_with(WindowsOutputs, options)
    }

    /// Starts capturing from `outputs`: this PC's own, or stand-ins in tests.
    pub(crate) fn start_with<O: Outputs + Send + 'static>(
        outputs: O,
        options: AudioOptions,
    ) -> Result<Self, AudioError> {
        let ring = Arc::new(PacketRing::new(QUEUE_PACKETS));
        ring.set_delay(Duration::from_millis(u64::from(options.delay_ms)));
        let shared = Arc::new(Shared {
            stop: AtomicBool::new(false),
            paused: AtomicBool::new(false),
            state: AtomicU8::new(SoundState::Ready as u8),
            level: AtomicU32::new(0),
            meter: AtomicU32::new(0),
            packets: AtomicU64::new(0),
            problem: Mutex::new(None),
        });

        let (ready, started) = mpsc::channel();
        let worker = {
            let ring = Arc::clone(&ring);
            let shared = Arc::clone(&shared);
            std::thread::Builder::new()
                .name("flint-sound".into())
                .spawn(move || run(outputs, &options, &ring, &shared, &ready))
                .map_err(|error| AudioError::Platform(error.to_string()))?
        };

        // A thread that ends without a word has panicked; that is a failure to start, not a hang.
        match started
            .recv()
            .unwrap_or(Err(AudioError::Platform(ENDED_EARLY.into())))
        {
            Ok(config) => Ok(Self {
                ring,
                shared,
                worker: Some(worker),
                config,
            }),
            Err(error) => {
                let _ = worker.join();
                Err(error)
            }
        }
    }

    /// The two-byte `AudioSpecificConfig` for the TV's decoder.
    #[must_use]
    pub fn config(&self) -> &[u8] {
        &self.config
    }

    /// Waits up to `timeout` for the next packet, copying it into `out`; returns its time.
    pub fn next(&self, out: &mut Vec<u8>, timeout: Duration) -> Option<i64> {
        self.ring.pop(out, timeout)
    }

    /// What the share is doing.
    #[must_use]
    pub fn state(&self) -> SoundState {
        match self.shared.state.load(Ordering::Acquire) {
            2 => SoundState::Sounding,
            3 => SoundState::Unavailable,
            _ => SoundState::Ready,
        }
    }

    /// Why the share is unavailable, when it is.
    #[must_use]
    pub fn problem(&self) -> Option<String> {
        self.shared
            .problem
            .lock()
            .unwrap_or_else(std::sync::PoisonError::into_inner)
            .clone()
    }

    /// The counters so far.
    #[must_use]
    pub fn stats(&self) -> AudioStats {
        AudioStats {
            packets: self.shared.packets.load(Ordering::Relaxed),
            dropped: self.ring.stats().dropped,
            level: f32::from_bits(self.shared.level.load(Ordering::Relaxed)),
            meter: f32::from_bits(self.shared.meter.load(Ordering::Relaxed)),
        }
    }

    /// Pauses or resumes. While paused nothing captured is encoded, and what was queued is dropped.
    pub fn set_paused(&self, paused: bool) {
        self.shared.paused.store(paused, Ordering::Release);
        if paused {
            self.ring.clear();
        }
    }

    /// Holds packets queued from now on back by `delay_ms`.
    pub fn set_delay(&self, delay_ms: u32) {
        self.ring
            .set_delay(Duration::from_millis(u64::from(delay_ms)));
    }
}

impl Drop for AudioSession {
    fn drop(&mut self) {
        self.shared.stop.store(true, Ordering::Release);
        if let Some(worker) = self.worker.take() {
            let _ = worker.join();
        }
    }
}

/// The capture thread: open everything, then capture, encode and queue until stopped.
fn run<O: Outputs>(
    outputs: O,
    options: &AudioOptions,
    ring: &PacketRing,
    shared: &Shared,
    ready: &mpsc::Sender<Result<Vec<u8>, AudioError>>,
) {
    let opened = AacEncoder::new(options.bitrate_kbps).and_then(|encoder| {
        let follower = Follower::open(outputs, options.device_id.clone())?;
        Ok(Capturing::new(encoder, follower))
    });
    let mut capturing = match opened {
        Ok(capturing) => capturing,
        Err(error) => {
            let _ = ready.send(Err(error));
            return;
        }
    };
    let _ = ready.send(Ok(capturing.encoder.audio_specific_config().to_vec()));
    raise_priority();

    while !shared.stop.load(Ordering::Acquire) {
        std::thread::sleep(POLL);
        if let Err(error) = capturing.turn(options, ring, shared) {
            unavailable(shared, &error.to_string());
            return;
        }
    }
}

/// What the capture thread keeps from one turn to the next. Every buffer is allocated once.
struct Capturing<O: Outputs> {
    encoder: AacEncoder,
    follower: Follower<O>,
    started: Instant,
    clock: SampleClock,
    liveness: Liveness,
    captured: Vec<i16>,
    feed: Vec<i16>,
    packets: Vec<AudioPacket>,
}

impl<O: Outputs> Capturing<O> {
    fn new(encoder: AacEncoder, follower: Follower<O>) -> Self {
        Self {
            encoder,
            follower,
            started: Instant::now(),
            clock: SampleClock::default(),
            liveness: Liveness::default(),
            captured: Vec::with_capacity(SAMPLE_RATE as usize / 5 * CHANNELS as usize),
            feed: Vec::with_capacity(SAMPLE_RATE as usize / 5 * CHANNELS as usize),
            packets: Vec::with_capacity(16),
        }
    }

    /// Captures what arrived since the last turn, encodes it and queues the packets.
    ///
    /// # Errors
    /// A capture or encoder failure, which ends the share.
    fn turn(
        &mut self,
        options: &AudioOptions,
        ring: &PacketRing,
        shared: &Shared,
    ) -> Result<(), AudioError> {
        self.captured.clear();
        match self
            .follower
            .read(self.started.elapsed(), &mut self.captured)?
        {
            Change::None => {}
            Change::Lost => unavailable(shared, lost(options)),
            Change::Back => {
                available(shared);
                self.liveness = Liveness::default();
            }
        }
        shared
            .meter
            .store(self.follower.meter().to_bits(), Ordering::Relaxed);

        let frames = (self.captured.len() / CHANNELS as usize) as u64;
        let missing = self.clock.missing(self.started.elapsed(), frames);
        let paused = shared.paused.load(Ordering::Acquire);
        fill_feed(&mut self.feed, missing, &self.captured, paused);

        let heard = level(&self.captured);
        shared.level.store(heard.to_bits(), Ordering::Relaxed);
        if shared.state.load(Ordering::Acquire) != SoundState::Unavailable as u8 {
            let state = self
                .liveness
                .observe(self.started.elapsed(), if paused { 0.0 } else { heard });
            shared.state.store(state as u8, Ordering::Release);
        }

        self.encoder.encode(&self.feed, &mut self.packets)?;
        self.clock.fed((self.feed.len() / CHANNELS as usize) as u64);
        for packet in self.packets.drain(..) {
            shared.packets.fetch_add(1, Ordering::Relaxed);
            if !paused {
                ring.push(
                    &packet.data,
                    options.start_offset_us + packet.presentation_time_us,
                );
            }
        }
        Ok(())
    }
}

/// Why the share has nothing to capture, by whether an output was chosen.
pub(crate) fn lost(options: &AudioOptions) -> &'static str {
    if options.device_id.is_some() {
        "The chosen sound output was disconnected."
    } else {
        "This PC has no sound output connected."
    }
}

/// Capturing again: the problem is cleared, and the state is worked out afresh.
fn available(shared: &Shared) {
    *shared
        .problem
        .lock()
        .unwrap_or_else(std::sync::PoisonError::into_inner) = None;
    shared
        .state
        .store(SoundState::Ready as u8, Ordering::Release);
}

fn unavailable(shared: &Shared, problem: &str) {
    *shared
        .problem
        .lock()
        .unwrap_or_else(std::sync::PoisonError::into_inner) = Some(problem.to_owned());
    shared
        .state
        .store(SoundState::Unavailable as u8, Ordering::Release);
}

/// Asks Windows to schedule this thread as pro audio, so capture keeps up under load.
fn raise_priority() {
    let mut task = 0u32;
    // SAFETY: a constant task name and a valid out-parameter; failing leaves normal priority.
    let _ = unsafe {
        windows::Win32::System::Threading::AvSetMmThreadCharacteristicsW(
            windows::core::w!("Pro Audio"),
            &raw mut task,
        )
    };
}

#[cfg(test)]
#[path = "session_tests.rs"]
mod tests;
