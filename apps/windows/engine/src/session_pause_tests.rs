//! Pausing a share: nothing captured after the pause reaches the encoder, and the TV is left
//! holding its picture or showing black.

use super::*;
use crate::encode::video::{EncodedFrame, EncoderConfig, FrameData};
use crate::encode::VideoCodec;
use std::collections::VecDeque;

const WIDTH: u32 = 4;
const HEIGHT: u32 = 4;

/// A desktop that changes on every look, unless told to fail once.
#[derive(Default)]
struct BusySource {
    taken: usize,
    recycled: usize,
    interrupt_next: bool,
}

impl FrameSource for BusySource {
    fn next_frame(
        &mut self,
        _timeout_ms: u32,
    ) -> Result<(FrameOutcome, Option<SourceFrame>), CaptureError> {
        if std::mem::take(&mut self.interrupt_next) {
            return Err(CaptureError::Interrupted);
        }

        self.taken += 1;
        Ok((
            FrameOutcome::Captured,
            Some(SourceFrame {
                width: WIDTH,
                height: HEIGHT,
                presentation_time_us: i64::try_from(self.taken).unwrap() * 1_000,
                data: FrameData::Bgra {
                    // Never black, so a black frame can only be the pause's own.
                    pixels: vec![0x7f; (WIDTH * HEIGHT * 4) as usize],
                    stride: WIDTH * 4,
                },
            }),
        ))
    }

    fn recycle_frame(&mut self, _frame: SourceFrame) {
        self.recycled += 1;
    }
}

/// What the encoder was given, and an output delay like a hardware encoder's.
struct RecordingEncoder {
    delay: usize,
    submitted: Vec<(bool, bool)>,
    waiting: VecDeque<EncodedFrame>,
}

impl RecordingEncoder {
    fn new(delay: usize) -> Self {
        Self {
            delay,
            submitted: Vec::new(),
            waiting: VecDeque::new(),
        }
    }

    /// Frames submitted that were entirely black.
    fn black(&self) -> usize {
        self.submitted.iter().filter(|(black, _)| *black).count()
    }
}

impl VideoEncoder for RecordingEncoder {
    fn codec_specific_data(&self) -> &[Vec<u8>] {
        &[]
    }

    fn output_size(&self) -> (u32, u32) {
        (WIDTH, HEIGHT)
    }

    fn codec(&self) -> VideoCodec {
        VideoCodec::H264
    }

    fn submit(
        &mut self,
        frame: &SourceFrame,
        force_key_frame: bool,
    ) -> Result<Option<EncodedFrame>, crate::encode::video::EncodeError> {
        let FrameData::Bgra { pixels, .. } = &frame.data else {
            panic!("only system-memory frames here");
        };
        assert_eq!((frame.width, frame.height), (WIDTH, HEIGHT));
        let black = pixels.iter().all(|&byte| byte == 0);
        self.submitted.push((black, force_key_frame));
        self.waiting.push_back(EncodedFrame {
            data: vec![u8::from(black); 8],
            key_frame: force_key_frame,
            presentation_time_us: frame.presentation_time_us,
        });

        Ok(if self.waiting.len() > self.delay {
            self.waiting.pop_front()
        } else {
            None
        })
    }

    fn poll(&mut self) -> Result<Option<EncodedFrame>, crate::encode::video::EncodeError> {
        Ok(self.waiting.pop_front())
    }
}

fn session(delay: usize) -> MirrorSession<BusySource, RecordingEncoder> {
    MirrorSession::new(BusySource::default(), RecordingEncoder::new(delay))
}

fn encoded(tick: Tick) -> EncodedFrame {
    match tick {
        Tick::Encoded(encoded) => encoded,
        other => panic!("expected an encoded frame, got {other:?}"),
    }
}

#[test]
fn holding_keeps_capturing_but_gives_the_encoder_nothing() {
    let mut session = session(0);
    encoded(session.tick().unwrap());

    session.set_pause(Pause::Holding);
    for _ in 0..5 {
        assert_eq!(session.tick().unwrap(), Tick::Paused);
    }

    assert_eq!(session.pause(), Pause::Holding);
    assert_eq!(
        session.encoder.submitted.len(),
        1,
        "only the frame from before the pause"
    );
    assert_eq!(session.source.taken, 6, "capture kept running");
    assert_eq!(session.source.recycled, 6, "and every frame went back");
}

#[test]
fn blanking_sends_exactly_one_black_frame_then_nothing() {
    let mut session = session(0);
    encoded(session.tick().unwrap());

    session.set_pause(Pause::Blanking);
    let black = encoded(session.tick().unwrap());
    for _ in 0..4 {
        assert_eq!(session.tick().unwrap(), Tick::Paused);
    }

    assert_eq!(
        black.data,
        vec![1; 8],
        "the frame the TV got was the black one"
    );
    assert!(
        black.presentation_time_us > 1_000,
        "after the last frame sent"
    );
    assert_eq!(session.encoder.black(), 1);
    assert_eq!(session.encoder.submitted.len(), 2);
    assert_eq!(session.stats().frames_encoded, 2);
}

#[test]
fn resuming_sends_the_screen_at_once_without_a_key_frame() {
    let mut session = session(0);
    encoded(session.tick().unwrap());
    session.set_pause(Pause::Blanking);
    session.tick().unwrap();

    session.set_pause(Pause::Running);
    let next = encoded(session.tick().unwrap());

    assert_eq!(next.data, vec![0; 8], "the current screen, not black");
    assert!(!next.key_frame, "the TV's decoder never lost its place");
}

#[test]
fn pausing_before_anything_was_sent_still_starts_the_tv_on_a_key_frame() {
    let mut session = session(0);
    session.set_pause(Pause::Holding);
    session.tick().unwrap();

    session.set_pause(Pause::Running);

    assert!(encoded(session.tick().unwrap()).key_frame);

    let mut blank_first = self::session(0);
    blank_first.set_pause(Pause::Blanking);
    assert!(
        encoded(blank_first.tick().unwrap()).key_frame,
        "a black frame that is the TV's first must decode on its own"
    );
}

#[test]
fn losing_capture_while_paused_is_still_reported_and_recovered() {
    let mut session = session(0);
    encoded(session.tick().unwrap());
    session.set_pause(Pause::Holding);
    session.source.interrupt_next = true;

    assert_eq!(session.tick().unwrap(), Tick::Recovered);
    assert_eq!(session.stats().recoveries, 1);
    assert_eq!(session.tick().unwrap(), Tick::Paused);
}

#[test]
fn turning_a_held_picture_black_while_paused_sends_the_black_frame() {
    let mut session = session(0);
    encoded(session.tick().unwrap());
    session.set_pause(Pause::Holding);
    session.tick().unwrap();

    session.set_pause(Pause::Blanking);

    assert_eq!(encoded(session.tick().unwrap()).data, vec![1; 8]);
    session.set_pause(Pause::Blanking);
    assert_eq!(
        session.tick().unwrap(),
        Tick::Paused,
        "asking again sends nothing more"
    );
}

#[test]
fn a_frame_the_cap_was_holding_is_given_back_on_pause_and_never_sent() {
    let mut session =
        MirrorSession::new(BusySource::default(), RecordingEncoder::new(0)).with_frame_rate_cap(1);
    encoded(session.tick().unwrap());
    assert_eq!(session.tick().unwrap(), Tick::Held);
    let recycled_before = session.source.recycled;

    session.set_pause(Pause::Holding);
    assert_eq!(session.source.recycled, recycled_before + 1);
    session.set_pause(Pause::Running);

    assert_eq!(
        session.encoder.submitted.len(),
        1,
        "the held frame was not submitted"
    );
}

#[test]
fn black_frames_still_reach_the_tv_from_an_encoder_that_hands_output_back_late() {
    // A hardware encoder may finish a frame only after it is given the next. A paused share gives
    // it nothing, so what it still holds is drained instead: the last frame from before the pause,
    // then the black one.
    let mut session = session(1);
    session.tick().unwrap();
    encoded(session.tick().unwrap());

    session.set_pause(Pause::Blanking);
    let first = session.tick().unwrap();
    let last = encoded(session.tick().unwrap());

    assert_eq!(
        encoded(first).data,
        vec![0; 8],
        "the frame from before the pause"
    );
    assert_eq!(last.data, vec![1; 8], "then black");
    assert_eq!(session.tick().unwrap(), Tick::Paused);
}

#[test]
fn output_held_back_while_holding_is_dropped_so_resuming_starts_afresh() {
    let mut session = session(1);
    session.tick().unwrap();
    encoded(session.tick().unwrap());

    session.set_pause(Pause::Holding);
    assert_eq!(session.tick().unwrap(), Tick::Paused);
    session.set_pause(Pause::Running);

    // The TV never got the last frame, so what comes next must not depend on it.
    session.tick().unwrap();
    let (_, forced) = *session.encoder.submitted.last().unwrap();
    assert!(forced, "a key frame after output was dropped");
}

#[test]
fn a_running_session_ignores_being_told_to_run() {
    let mut session = session(0);
    session.set_pause(Pause::Running);

    assert_eq!(session.pause(), Pause::Running);
    assert!(matches!(session.tick().unwrap(), Tick::Encoded(_)));
}

#[test]
fn an_encoder_that_fails_while_draining_ends_the_session() {
    struct Failing;

    impl VideoEncoder for Failing {
        fn codec_specific_data(&self) -> &[Vec<u8>] {
            &[]
        }

        fn output_size(&self) -> (u32, u32) {
            (WIDTH, HEIGHT)
        }

        fn codec(&self) -> VideoCodec {
            VideoCodec::H264
        }

        fn submit(
            &mut self,
            _frame: &SourceFrame,
            _force_key_frame: bool,
        ) -> Result<Option<EncodedFrame>, crate::encode::video::EncodeError> {
            Err(crate::encode::video::EncodeError::InvalidConfig)
        }

        fn poll(&mut self) -> Result<Option<EncodedFrame>, crate::encode::video::EncodeError> {
            Err(crate::encode::video::EncodeError::InvalidConfig)
        }
    }

    let mut session = MirrorSession::new(BusySource::default(), Failing);
    session.set_pause(Pause::Holding);
    assert!(matches!(session.tick(), Err(SessionError::Encode(_))));

    session.set_pause(Pause::Blanking);
    assert!(matches!(session.tick(), Err(SessionError::Encode(_))));
}

#[test]
fn the_null_encoder_has_nothing_to_drain() {
    let mut encoder = crate::encode::video::NullEncoder::new(EncoderConfig {
        width: WIDTH,
        height: HEIGHT,
        frame_rate: 30,
        bitrate_bits_per_second: 1_000_000,
        codec: VideoCodec::H264,
    })
    .unwrap();

    assert_eq!(encoder.poll().unwrap(), None);
}
