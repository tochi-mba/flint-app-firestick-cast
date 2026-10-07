//! The frame-rate cap, driven by a fake clock that moves only when the fake desktop waits.

use super::*;
use crate::encode::video::{EncoderConfig, FrameData, NullEncoder};
use crate::encode::VideoCodec;
use std::cell::Cell;
use std::rc::Rc;
use std::time::Duration;

/// Time that passes only when the source waits for the desktop.
#[derive(Clone, Default)]
struct FakeClock(Rc<Cell<Duration>>);

impl PacingClock for FakeClock {
    fn now(&self) -> Duration {
        self.0.get()
    }
}

#[derive(Clone, Copy)]
enum Event {
    Frame(Duration),
    Interrupt(Duration),
    Fail(Duration),
}

impl Event {
    fn at(self) -> Duration {
        match self {
            Self::Frame(at) | Self::Interrupt(at) | Self::Fail(at) => at,
        }
    }
}

/// A desktop that changes at scripted moments, waiting on the fake clock as duplication would.
struct TimedSource {
    clock: FakeClock,
    events: Vec<Event>,
    next: usize,
    taken: usize,
    recycled: Vec<i64>,
    waits: Vec<u32>,
}

impl TimedSource {
    fn new(clock: &FakeClock, events: Vec<Event>) -> Self {
        Self {
            clock: clock.clone(),
            events,
            next: 0,
            taken: 0,
            recycled: Vec::new(),
            waits: Vec::new(),
        }
    }
}

impl FrameSource for TimedSource {
    fn next_frame(
        &mut self,
        timeout_ms: u32,
    ) -> Result<(FrameOutcome, Option<SourceFrame>), CaptureError> {
        self.waits.push(timeout_ms);
        let now = self.clock.now();
        let deadline = now + Duration::from_millis(u64::from(timeout_ms));

        // Changes that piled up while nobody was looking arrive as one frame, the newest, which
        // is what duplication does with accumulated frames.
        let mut latest = None;
        while let Some(&event) = self.events.get(self.next) {
            if latest.is_none() {
                if event.at() > deadline {
                    break;
                }
                // Waited for it.
                self.clock.0.set(event.at().max(now));
            } else if event.at() > self.clock.now() {
                break;
            }
            self.next += 1;
            latest = Some(event);
            if !matches!(event, Event::Frame(_)) {
                break;
            }
        }

        match latest {
            None => {
                self.clock.0.set(deadline);
                Ok((FrameOutcome::Unchanged, None))
            }
            Some(Event::Frame(at)) => {
                self.taken += 1;
                Ok((FrameOutcome::Captured, Some(frame(at))))
            }
            Some(Event::Interrupt(_)) => Err(CaptureError::Interrupted),
            Some(Event::Fail(_)) => Err(CaptureError::Platform("gone".into())),
        }
    }

    fn recycle_frame(&mut self, frame: SourceFrame) {
        self.recycled.push(frame.presentation_time_us);
    }
}

fn frame(at: Duration) -> SourceFrame {
    SourceFrame {
        width: 4,
        height: 4,
        presentation_time_us: i64::try_from(at.as_micros()).unwrap(),
        data: FrameData::Bgra {
            pixels: vec![0u8; 4 * 4 * 4],
            stride: 16,
        },
    }
}

const fn ms(value: u64) -> Duration {
    Duration::from_millis(value)
}

fn us(at: Duration) -> i64 {
    i64::try_from(at.as_micros()).unwrap()
}

fn capped(
    frames_per_second: u32,
    events: Vec<Event>,
) -> MirrorSession<TimedSource, NullEncoder, FakeClock> {
    let clock = FakeClock::default();
    let encoder = NullEncoder::new(EncoderConfig {
        width: 4,
        height: 4,
        frame_rate: 30,
        bitrate_bits_per_second: 1_000_000,
        codec: VideoCodec::H264,
    })
    .unwrap();
    MirrorSession::with_clock(TimedSource::new(&clock, events), encoder, clock)
        .with_frame_rate_cap(frames_per_second)
}

/// Frames arriving every `period` from zero until `until`.
fn steady(period: Duration, until: Duration) -> Vec<Event> {
    std::iter::successors(Some(Duration::ZERO), |at| Some(*at + period))
        .take_while(|at| *at < until)
        .map(Event::Frame)
        .collect()
}

/// Ticks until the fake clock passes `until`, returning the times of the frames that were sent.
fn run(
    session: &mut MirrorSession<TimedSource, NullEncoder, FakeClock>,
    until: Duration,
) -> Vec<i64> {
    let mut sent = Vec::new();
    let mut ticks = 0;
    while session.clock.now() < until {
        // A tick that waits for nothing never lets time pass, which on a real desktop is a core
        // spinning; here it would be a test that never ends.
        ticks += 1;
        assert!(ticks < 10_000, "the session spun without waiting");
        if let Tick::Encoded(encoded) = session.tick().unwrap() {
            sent.push(encoded.presentation_time_us);
        }
    }
    sent
}

fn encoded(tick: Tick) -> EncodedFrame {
    match tick {
        Tick::Encoded(encoded) => encoded,
        other => panic!("expected an encoded frame, got {other:?}"),
    }
}

#[test]
fn a_fast_display_is_held_to_the_cap() {
    // A 144 Hz display redrawing continuously, capped at 30 a second, for two seconds.
    let mut session = capped(30, steady(Duration::from_micros(6_944), ms(2_000)));

    let sent = run(&mut session, ms(2_000));

    assert!(
        (59..=61).contains(&sent.len()),
        "about 60 frames in two seconds, sent {}",
        sent.len()
    );
    assert_eq!(session.stats().frames_encoded, sent.len() as u64);
    assert_eq!(session.frame_rate_cap(), 30);
}

#[test]
fn every_frame_taken_is_given_back_exactly_once() {
    let mut session = capped(30, steady(Duration::from_micros(6_944), ms(1_000)));

    run(&mut session, ms(1_000));

    let held = usize::from(session.held.is_some());
    let mut recycled = session.source.recycled.clone();
    recycled.sort_unstable();
    recycled.dedup();
    assert_eq!(
        recycled.len(),
        session.source.recycled.len(),
        "no frame twice"
    );
    assert_eq!(session.source.taken, session.source.recycled.len() + held);
    assert_eq!(
        session.stats().frames_held_back,
        (session.source.taken - held) as u64 - session.stats().frames_encoded,
        "the held-back counter is exactly the frames replaced before they could be sent"
    );
}

#[test]
fn a_frame_that_arrives_too_soon_is_sent_once_the_interval_has_passed() {
    let mut session = capped(30, vec![Event::Frame(ms(0)), Event::Frame(ms(10))]);

    assert_eq!(encoded(session.tick().unwrap()).presentation_time_us, 0);
    assert_eq!(session.tick().unwrap(), Tick::Held);
    // Then the desktop goes still: the held frame must still reach the TV.
    let late = encoded(session.tick().unwrap());

    assert_eq!(late.presentation_time_us, us(ms(10)));
    assert_eq!(
        session.source.waits,
        [100, 100, 24],
        "the wait is cut to the 23.3 ms left, rounded up"
    );
    assert_eq!(session.clock.now(), ms(34));
    assert_eq!(session.stats().frames_unchanged, 0);
}

#[test]
fn a_newer_frame_replaces_a_held_one_which_is_never_sent() {
    let mut session = capped(
        30,
        vec![
            Event::Frame(ms(0)),
            Event::Frame(ms(10)),
            Event::Frame(ms(20)),
        ],
    );

    session.tick().unwrap();
    assert_eq!(session.tick().unwrap(), Tick::Held);
    assert_eq!(session.tick().unwrap(), Tick::Held);
    let sent = encoded(session.tick().unwrap());

    assert_eq!(sent.presentation_time_us, us(ms(20)));
    assert_eq!(session.source.recycled, [0, us(ms(10)), us(ms(20))]);
    assert_eq!(session.stats().frames_held_back, 1);
    assert_eq!(session.stats().frames_encoded, 2);
}

#[test]
fn a_key_frame_asked_for_while_a_frame_is_held_applies_to_that_frame() {
    let mut session = capped(30, vec![Event::Frame(ms(0)), Event::Frame(ms(10))]);
    session.tick().unwrap();
    assert_eq!(session.tick().unwrap(), Tick::Held);

    session.request_key_frame();

    let sent = encoded(session.tick().unwrap());
    assert_eq!(sent.presentation_time_us, us(ms(10)));
    assert!(sent.key_frame);
}

#[test]
fn losing_capture_while_a_frame_is_held_gives_it_back_and_reports_recovery() {
    let mut session = capped(
        30,
        vec![
            Event::Frame(ms(0)),
            Event::Frame(ms(10)),
            Event::Interrupt(ms(15)),
            Event::Frame(ms(50)),
        ],
    );
    session.tick().unwrap();
    assert_eq!(session.tick().unwrap(), Tick::Held);

    assert_eq!(session.tick().unwrap(), Tick::Recovered);

    assert!(session.held.is_none());
    assert_eq!(session.source.recycled, [0, us(ms(10))]);
    assert_eq!(session.stats().recoveries, 1);
    let next = encoded(session.tick().unwrap());
    assert_eq!(next.presentation_time_us, us(ms(50)));
    assert!(
        next.key_frame,
        "the receiver's references are stale after a recovery"
    );
}

#[test]
fn a_capture_failure_while_a_frame_is_held_gives_it_back_and_ends_the_session() {
    let mut session = capped(
        30,
        vec![
            Event::Frame(ms(0)),
            Event::Frame(ms(10)),
            Event::Fail(ms(15)),
        ],
    );
    session.tick().unwrap();
    assert_eq!(session.tick().unwrap(), Tick::Held);

    assert!(matches!(
        session.tick(),
        Err(SessionError::Capture(CaptureError::Platform(_)))
    ));

    assert!(session.held.is_none());
    assert_eq!(session.source.recycled, [0, us(ms(10))]);
}

#[test]
fn a_cap_at_or_above_the_arrival_rate_changes_nothing() {
    // The regression guard for today's behaviour: a 30 a second desktop under a 60 cap, and the
    // same desktop with no cap at all, send exactly the same frames.
    let events = steady(Duration::from_micros(33_333), ms(1_000));
    let arrived = events.len();
    let mut under_cap = capped(60, events.clone());
    let mut uncapped = capped(0, events);

    let sent = run(&mut under_cap, ms(1_000));

    assert_eq!(sent, run(&mut uncapped, ms(1_000)));
    assert_eq!(sent.len(), arrived, "every frame that arrived was sent");
    assert_eq!(under_cap.stats().frames_held_back, 0);
    assert!(under_cap.held.is_none());
    assert_eq!(uncapped.frame_rate_cap(), 0);
}

#[test]
fn a_still_desktop_with_nothing_held_still_counts_as_unchanged() {
    let mut session = capped(30, vec![Event::Frame(ms(0))]);
    session.tick().unwrap();

    assert_eq!(session.tick().unwrap(), Tick::Unchanged);

    assert_eq!(session.stats().frames_unchanged, 1);
    assert_eq!(session.source.waits, [100, 100]);
}
