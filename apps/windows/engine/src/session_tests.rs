use super::*;
use crate::encode::video::{EncoderConfig, FrameData, NullEncoder};
use crate::encode::VideoCodec;

fn config() -> EncoderConfig {
    EncoderConfig {
        width: 4,
        height: 4,
        frame_rate: 30,
        bitrate_bits_per_second: 1_000_000,
        codec: VideoCodec::H264,
    }
}

fn frame(presentation_time_us: i64) -> SourceFrame {
    SourceFrame {
        width: 4,
        height: 4,
        presentation_time_us,
        data: FrameData::Bgra {
            pixels: vec![0u8; 4 * 4 * 4],
            stride: 16,
        },
    }
}

/// A capture source that replays a fixed script, so every branch is reachable without a GPU.
struct ScriptedSource {
    steps: Vec<Result<(FrameOutcome, Option<SourceFrame>), CaptureError>>,
    at: usize,
    recycled_frames: usize,
}

impl ScriptedSource {
    fn new(steps: Vec<Result<(FrameOutcome, Option<SourceFrame>), CaptureError>>) -> Self {
        Self {
            steps,
            at: 0,
            recycled_frames: 0,
        }
    }
}

impl FrameSource for ScriptedSource {
    fn next_frame(
        &mut self,
        _timeout_ms: u32,
    ) -> Result<(FrameOutcome, Option<SourceFrame>), CaptureError> {
        let step = self.steps.get(self.at).cloned();
        self.at += 1;
        step.unwrap_or(Ok((FrameOutcome::Unchanged, None)))
    }

    fn recycle_frame(&mut self, _frame: SourceFrame) {
        self.recycled_frames += 1;
    }
}

fn session(
    steps: Vec<Result<(FrameOutcome, Option<SourceFrame>), CaptureError>>,
) -> MirrorSession<ScriptedSource, NullEncoder> {
    MirrorSession::new(
        ScriptedSource::new(steps),
        NullEncoder::new(config()).unwrap(),
    )
}

#[test]
fn a_captured_frame_is_encoded_and_counted() {
    let mut session = session(vec![Ok((FrameOutcome::Captured, Some(frame(0))))]);

    let tick = session.tick().unwrap();

    assert!(matches!(tick, Tick::Encoded(_)));
    assert_eq!(session.stats().frames_encoded, 1);
    assert!(session.stats().bytes_encoded > 0);
    assert_eq!(session.source.recycled_frames, 1);
}

#[test]
fn a_still_desktop_reports_unchanged_rather_than_re_encoding() {
    // Re-encoding a picture the receiver is already showing spends bandwidth for nothing.
    let mut session = session(vec![Ok((FrameOutcome::Unchanged, None))]);

    assert_eq!(session.tick().unwrap(), Tick::Unchanged);
    assert_eq!(session.stats().frames_unchanged, 1);
    assert_eq!(session.stats().frames_encoded, 0);
}

#[test]
fn a_captured_outcome_with_no_frame_is_treated_as_unchanged() {
    // Duplication reports this for a pointer-only move: a frame arrived, but no new pixels.
    let mut session = session(vec![Ok((FrameOutcome::Captured, None))]);

    assert_eq!(session.tick().unwrap(), Tick::Unchanged);
    assert_eq!(session.stats().frames_unchanged, 1);
}

#[test]
fn the_very_first_encoded_frame_is_forced_to_a_key_frame() {
    // Without this a receiver joining the stream has nothing to decode against and shows
    // nothing at all, which looks exactly like a dead pipeline.
    let mut session = session(vec![Ok((FrameOutcome::Captured, Some(frame(0))))]);

    assert!(session.key_frame_pending());
    let Tick::Encoded(encoded) = session.tick().unwrap() else {
        panic!("expected an encoded frame");
    };
    assert!(encoded.key_frame);
    assert!(!session.key_frame_pending());
}

#[test]
fn an_interruption_is_reported_as_recovered_rather_than_ending_the_session() {
    // A lock screen or UAC prompt arrives this way and must not read as a crash.
    let mut session = session(vec![Err(CaptureError::Interrupted)]);

    assert_eq!(session.tick().unwrap(), Tick::Recovered);
    assert_eq!(session.stats().recoveries, 1);
}

#[test]
fn recovery_forces_the_next_frame_to_be_a_key_frame() {
    // Everything the receiver holds as a reference is stale after capture was lost, so a
    // delta frame against it would decode to garbage.
    let mut session = session(vec![
        Ok((FrameOutcome::Captured, Some(frame(0)))),
        Err(CaptureError::Interrupted),
        Ok((FrameOutcome::Captured, Some(frame(66_666)))),
    ]);

    session.tick().unwrap();
    assert!(!session.key_frame_pending());

    assert_eq!(session.tick().unwrap(), Tick::Recovered);
    assert!(session.key_frame_pending());

    let Tick::Encoded(encoded) = session.tick().unwrap() else {
        panic!("expected an encoded frame after recovery");
    };
    assert!(encoded.key_frame);
}

#[test]
fn a_requested_key_frame_is_honoured_on_the_next_encoded_frame() {
    let mut session = session(vec![
        Ok((FrameOutcome::Captured, Some(frame(0)))),
        Ok((FrameOutcome::Captured, Some(frame(33_333)))),
    ]);

    session.tick().unwrap();
    session.request_key_frame();

    let Tick::Encoded(encoded) = session.tick().unwrap() else {
        panic!("expected an encoded frame");
    };
    assert!(encoded.key_frame);
}

#[test]
fn a_key_frame_request_survives_ticks_that_produce_nothing() {
    // The request must not be cleared by an unchanged tick, or a receiver that asked after
    // packet loss would wait for a key frame that was quietly discarded.
    let mut session = session(vec![
        Ok((FrameOutcome::Captured, Some(frame(0)))),
        Ok((FrameOutcome::Unchanged, None)),
        Ok((FrameOutcome::Captured, Some(frame(66_666)))),
    ]);

    session.tick().unwrap();
    session.request_key_frame();
    assert_eq!(session.tick().unwrap(), Tick::Unchanged);
    assert!(
        session.key_frame_pending(),
        "the request was lost on an idle tick"
    );

    let Tick::Encoded(encoded) = session.tick().unwrap() else {
        panic!("expected an encoded frame");
    };
    assert!(encoded.key_frame);
}

#[test]
fn an_unrecoverable_capture_failure_ends_the_session() {
    let mut session = session(vec![Err(CaptureError::NoDisplayAdapter)]);

    let error = session.tick().unwrap_err();

    assert_eq!(error, SessionError::Capture(CaptureError::NoDisplayAdapter));
    assert!(error.to_string().contains("capture failed"));
}

#[test]
fn a_larger_capture_is_scaled_to_the_encoder_rather_than_rejected() {
    // A 4K desktop and a 1080p encoder must still produce a stream. Refusing here would make
    // mirroring appear broken on the common case of a desktop bigger than the television.
    let mut session = session(vec![Ok((
        FrameOutcome::Captured,
        Some(SourceFrame {
            width: 8,
            height: 8,
            presentation_time_us: 0,
            data: FrameData::Bgra {
                pixels: vec![0u8; 8 * 8 * 4],
                stride: 32,
            },
        }),
    ))]);

    let tick = session.tick().unwrap();

    assert!(matches!(tick, Tick::Encoded(_)));
    assert_eq!(session.output_size(), (4, 4));
}

#[test]
fn capture_and_scaling_buffers_are_preallocated_and_recycled_every_tick() {
    // The frame path must not allocate. Capture is the biggest buffer in the loop and the
    // scaled encoder input is the next biggest, so both allocations are made before ticking.
    let source = ReusingSource::new(8, 8, 3);
    let capture_capacity = source.pixels.capacity();
    let mut session = MirrorSession::new(source, NullEncoder::new(config()).unwrap());
    let scaling_capacity = session.scaled.capacity();
    assert!(scaling_capacity >= 4 * 4 * 4);

    for _ in 0..3 {
        assert!(matches!(session.tick().unwrap(), Tick::Encoded(_)));
    }

    assert_eq!(session.source.frames_recycled, 3);
    assert_eq!(session.source.pixels.capacity(), capture_capacity);
    assert_eq!(
        session.source.allocation_addresses[0],
        session.source.allocation_addresses[1]
    );
    assert_eq!(
        session.source.allocation_addresses[1],
        session.source.allocation_addresses[2]
    );
    assert_eq!(session.scaled.capacity(), scaling_capacity);
}

/// A source that can only produce its next frame if the session returned the previous buffer.
struct ReusingSource {
    width: u32,
    height: u32,
    pixels: Vec<u8>,
    frames_left: usize,
    frames_captured: usize,
    frames_recycled: usize,
    allocation_addresses: [usize; 3],
}

impl ReusingSource {
    fn new(width: u32, height: u32, frames: usize) -> Self {
        Self {
            width,
            height,
            pixels: vec![0; (width * height * 4) as usize],
            frames_left: frames,
            frames_captured: 0,
            frames_recycled: 0,
            allocation_addresses: [0; 3],
        }
    }
}

impl FrameSource for ReusingSource {
    fn next_frame(
        &mut self,
        _timeout_ms: u32,
    ) -> Result<(FrameOutcome, Option<SourceFrame>), CaptureError> {
        if self.frames_left == 0 {
            return Ok((FrameOutcome::Unchanged, None));
        }

        let len = (self.width * self.height * 4) as usize;
        self.pixels.resize(len, 0);
        self.allocation_addresses[self.frames_captured] = self.pixels.as_ptr() as usize;
        let presentation_time_us = self.frames_captured as i64 * 33_333;
        self.frames_captured += 1;
        self.frames_left -= 1;

        Ok((
            FrameOutcome::Captured,
            Some(SourceFrame {
                width: self.width,
                height: self.height,
                presentation_time_us,
                data: FrameData::Bgra {
                    pixels: std::mem::take(&mut self.pixels),
                    stride: self.width * 4,
                },
            }),
        ))
    }

    fn recycle_frame(&mut self, frame: SourceFrame) {
        // The fake source in these tests only ever produces system memory.
        let FrameData::Bgra { mut pixels, .. } = frame.data else {
            return;
        };
        pixels.clear();
        self.pixels = pixels;
        self.frames_recycled += 1;
    }
}

struct DelayedKeyFrameEncoder {
    submissions: usize,
    forced: [bool; 2],
}

impl VideoEncoder for DelayedKeyFrameEncoder {
    fn codec_specific_data(&self) -> &[Vec<u8>] {
        const BLOCKS: &[Vec<u8>] = &[];
        BLOCKS
    }

    fn output_size(&self) -> (u32, u32) {
        (4, 4)
    }

    fn codec(&self) -> crate::encode::VideoCodec {
        crate::encode::VideoCodec::H264
    }

    fn submit(
        &mut self,
        frame: &SourceFrame,
        force_key_frame: bool,
    ) -> Result<Option<EncodedFrame>, EncodeError> {
        self.forced[self.submissions] = force_key_frame;
        let key_frame = self.submissions == 1;
        self.submissions += 1;
        Ok(Some(EncodedFrame {
            data: vec![if key_frame { 0x65 } else { 0x41 }],
            presentation_time_us: frame.presentation_time_us,
            key_frame,
        }))
    }
}

#[test]
fn a_key_frame_request_remains_pending_until_an_idr_actually_arrives() {
    let mut session = MirrorSession::new(
        ScriptedSource::new(vec![
            Ok((FrameOutcome::Captured, Some(frame(0)))),
            Ok((FrameOutcome::Captured, Some(frame(33_333)))),
        ]),
        DelayedKeyFrameEncoder {
            submissions: 0,
            forced: [false; 2],
        },
    );

    let Tick::Encoded(first) = session.tick().unwrap() else {
        panic!("expected an encoded frame");
    };
    assert!(!first.key_frame);
    assert!(session.key_frame_pending());

    let Tick::Encoded(second) = session.tick().unwrap() else {
        panic!("expected an encoded frame");
    };
    assert!(second.key_frame);
    assert!(!session.key_frame_pending());
    assert_eq!(session.encoder.forced, [true, true]);
}

struct FailingEncoder;

impl VideoEncoder for FailingEncoder {
    fn codec_specific_data(&self) -> &[Vec<u8>] {
        const BLOCKS: &[Vec<u8>] = &[];
        BLOCKS
    }

    fn output_size(&self) -> (u32, u32) {
        (4, 4)
    }

    fn codec(&self) -> crate::encode::VideoCodec {
        crate::encode::VideoCodec::H264
    }

    fn submit(
        &mut self,
        _frame: &SourceFrame,
        _force_key_frame: bool,
    ) -> Result<Option<EncodedFrame>, EncodeError> {
        Err(EncodeError::Platform(
            "the encoder refused the frame".into(),
        ))
    }
}

#[test]
fn an_encoder_failure_ends_the_session_and_names_the_encoder() {
    let mut session = MirrorSession::new(
        ScriptedSource::new(vec![Ok((FrameOutcome::Captured, Some(frame(0))))]),
        FailingEncoder,
    );

    let error = session.tick().unwrap_err();

    assert!(matches!(error, SessionError::Encode(_)));
    assert!(error.to_string().contains("encode failed"));
    assert_eq!(session.source.recycled_frames, 1);
}

#[test]
fn a_scale_failure_still_returns_the_capture_buffer() {
    let malformed = SourceFrame {
        width: 8,
        height: 8,
        presentation_time_us: 0,
        data: FrameData::Bgra {
            pixels: vec![0; 4],
            stride: 32,
        },
    };
    let mut session = session(vec![Ok((FrameOutcome::Captured, Some(malformed)))]);

    let error = session.tick().unwrap_err();

    assert!(matches!(error, SessionError::Encode(_)));
    assert_eq!(session.source.recycled_frames, 1);
}

#[test]
fn stats_accumulate_across_a_mixed_run() {
    let mut session = session(vec![
        Ok((FrameOutcome::Captured, Some(frame(0)))),
        Ok((FrameOutcome::Unchanged, None)),
        Err(CaptureError::Interrupted),
        Ok((FrameOutcome::Captured, Some(frame(100_000)))),
        Ok((FrameOutcome::Unchanged, None)),
    ]);

    for _ in 0..5 {
        session.tick().unwrap();
    }

    let stats = session.stats();
    assert_eq!(stats.frames_encoded, 2);
    assert_eq!(stats.frames_unchanged, 2);
    assert_eq!(stats.recoveries, 1);
    assert!(stats.bytes_encoded > 0);
}

#[test]
fn codec_specific_data_is_readable_before_any_frame_is_captured() {
    // The transport sends a VideoConfig message before the first access unit, so it has to be
    // able to ask for this without waiting for capture to produce anything.
    let session = session(vec![]);
    assert_eq!(session.codec_specific_data().len(), 2);
}

#[test]
fn a_source_that_runs_dry_keeps_reporting_unchanged_rather_than_failing() {
    // The scripted source returns Unchanged past the end of its script, which is what a real
    // still desktop does indefinitely.
    let mut session = session(vec![]);
    for _ in 0..3 {
        assert_eq!(session.tick().unwrap(), Tick::Unchanged);
    }
}
