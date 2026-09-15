use super::*;
use crate::capture::duplication::DesktopDuplication;
use crate::encode::h264::H264Encoder;
use std::time::Instant;

fn config(width: u32, height: u32) -> EncoderConfig {
    EncoderConfig {
        width,
        height,
        frame_rate: 60,
        bitrate_bits_per_second: 12_000_000,
        codec: VideoCodec::H264,
    }
}

/// A frame with real motion, so the encoder has something to compress.
fn moving_frame(index: u32, width: u32, height: u32) -> SourceFrame {
    let mut pixels = vec![0u8; (width * height * 4) as usize];
    let bar = (index * 23) % width;
    for y in 0..height {
        for x in 0..width {
            let at = ((y * width + x) * 4) as usize;
            let lit = x.abs_diff(bar) < 40;
            pixels[at] = if lit { 240 } else { 20 };
            pixels[at + 1] = if lit { 180 } else { 20 };
            pixels[at + 2] = if lit { 60 } else { 20 };
            pixels[at + 3] = 255;
        }
    }
    SourceFrame {
        width,
        height,
        presentation_time_us: i64::from(index) * 16_666,
        data: FrameData::Bgra {
            pixels,
            stride: width * 4,
        },
    }
}

/// The adapter desktop duplication actually runs on, which is the one the encoder must match.
fn capture_adapter() -> Option<i64> {
    DesktopDuplication::open(0)
        .ok()
        .map(|d| d.format().adapter_luid)
}

#[test]
fn the_hardware_encoder_produces_real_h264_on_the_capture_adapter() {
    let Some(luid) = capture_adapter() else {
        return;
    };
    let Ok(mut encoder) = HardwareH264Encoder::new(config(1280, 720), luid) else {
        // A machine whose display adapter has no hardware encoder is a legitimate answer; the
        // caller falls back to software and the capability report says so honestly.
        return;
    };

    let mut units = 0;
    let mut key_frames = 0;
    for index in 0..60 {
        match encoder.submit(&moving_frame(index, 1280, 720), false) {
            Ok(Some(frame)) => {
                assert!(
                    !frame.data.is_empty(),
                    "an empty access unit reached the wire"
                );
                assert!(
                    frame.data.starts_with(&[0, 0, 0, 1]) || frame.data.starts_with(&[0, 0, 1]),
                    "expected Annex B framing, got {:02x?}",
                    &frame.data[..frame.data.len().min(8)]
                );
                units += 1;
                if frame.key_frame {
                    key_frames += 1;
                }
            }
            Ok(None) => {}
            Err(error) => panic!("hardware encode failed: {error}"),
        }
    }

    assert!(
        units > 0,
        "the hardware encoder accepted 60 frames and produced nothing"
    );
    assert!(
        key_frames > 0,
        "no key frame, so no receiver could start decoding"
    );
}

#[test]
fn the_hardware_encoder_refuses_a_frame_of_the_wrong_size() {
    let Some(luid) = capture_adapter() else {
        return;
    };
    let Ok(mut encoder) = HardwareH264Encoder::new(config(1280, 720), luid) else {
        return;
    };

    let error = encoder
        .submit(&moving_frame(0, 640, 360), false)
        .unwrap_err();

    assert_eq!(
        error,
        EncodeError::FrameSizeChanged {
            expected: (1280, 720),
            actual: (640, 360),
        }
    );
}

#[test]
fn a_hardware_encoder_reports_the_size_and_codec_it_was_built_for() {
    let Some(luid) = capture_adapter() else {
        return;
    };
    let Ok(encoder) = HardwareH264Encoder::new(config(1280, 720), luid) else {
        return;
    };

    assert_eq!(encoder.output_size(), (1280, 720));
    assert_eq!(encoder.codec(), VideoCodec::H264);
}

#[test]
fn hardware_encoders_can_be_created_and_dropped_repeatedly() {
    // Each one starts and shuts down Media Foundation and holds a driver resource. An
    // unbalanced teardown shows up on a later iteration, not the first.
    let Some(luid) = capture_adapter() else {
        return;
    };
    for _ in 0..3 {
        if let Ok(mut encoder) = HardwareH264Encoder::new(config(640, 360), luid) {
            let _ = encoder.submit(&moving_frame(0, 640, 360), false);
        }
    }
}

/// Compares hardware and software encode cost on identical frames.
#[test]
#[ignore = "measures this machine; run deliberately"]
fn report_hardware_versus_software() {
    let Some(luid) = capture_adapter() else {
        println!("NO CAPTURE");
        return;
    };
    println!("capture adapter luid: {luid}");

    let frames: Vec<SourceFrame> = (0..80).map(|i| moving_frame(i, 1280, 720)).collect();

    let mut hardware_us = Vec::new();
    match HardwareH264Encoder::new(config(1280, 720), luid) {
        Ok(mut encoder) => {
            for frame in &frames {
                let started = Instant::now();
                let _ = encoder.submit(frame, false);
                hardware_us.push(started.elapsed().as_micros() as u64);
            }
        }
        Err(error) => println!("no hardware encoder: {error}"),
    }

    let mut software_us = Vec::new();
    match H264Encoder::new(config(1280, 720)) {
        Ok(mut encoder) => {
            for frame in &frames {
                let started = Instant::now();
                let _ = encoder.submit(frame, false);
                software_us.push(started.elapsed().as_micros() as u64);
            }
        }
        Err(error) => println!("no software encoder: {error}"),
    }

    for (name, mut samples) in [("hardware", hardware_us), ("software", software_us)] {
        if samples.is_empty() {
            println!("{name:>10}: no samples");
            continue;
        }
        samples.sort_unstable();
        println!(
            "{name:>10}: median {:>6.2}ms  worst {:>7.2}ms",
            samples[samples.len() / 2] as f64 / 1000.0,
            *samples.last().unwrap() as f64 / 1000.0
        );
    }
}

#[test]
fn the_first_access_unit_of_a_session_is_always_a_key_frame() {
    // The receiver has nothing to decode from until one arrives. Priming leaves black frames in the
    // encoder that are flushed rather than sent, so without an explicit request the first real
    // frame would predict from pictures the receiver never saw.
    let Some(luid) = capture_adapter() else {
        return;
    };
    let Ok(mut encoder) = HardwareH264Encoder::new(config(1280, 720), luid) else {
        return;
    };

    let mut first = None;
    for index in 0..90 {
        if let Some(unit) = encoder
            .submit(&moving_frame(index, 1280, 720), false)
            .unwrap()
        {
            first = Some(unit);
            break;
        }
    }

    let first = first.expect("no access unit within 90 frames");
    assert!(
        first.key_frame,
        "the first access unit must be decodable on its own"
    );
}

#[test]
fn parameter_sets_are_available_before_any_frame_is_submitted() {
    // Flint sends VIDEO_CONFIG before the first access unit, so an encoder that publishes SPS and
    // PPS only mid-stream leaves the receiver unable to construct a decoder at all — a black screen
    // rather than an error. Priming exists entirely to make this true.
    let Some(luid) = capture_adapter() else {
        return;
    };
    let Ok(encoder) = HardwareH264Encoder::new(config(1280, 720), luid) else {
        return;
    };

    let sets = encoder.codec_specific_data();
    assert_eq!(sets.len(), 2, "MediaCodec needs csd-0 and csd-1 separately");
    for set in sets {
        assert!(
            set.starts_with(&[0, 0, 0, 1]),
            "a parameter set must be an Annex B unit, got {:02x?}",
            &set[..set.len().min(8)]
        );
    }
}

#[test]
fn a_receiver_that_loses_the_stream_gets_a_key_frame_within_the_configured_interval() {
    // The recovery guarantee. Without a bounded group of pictures a receiver that joins late or
    // drops a packet waits forever on a driver that ignores on-demand key-frame requests.
    let Some(luid) = capture_adapter() else {
        return;
    };
    let Ok(mut encoder) = HardwareH264Encoder::new(config(1280, 720), luid) else {
        return;
    };

    // Past the first key frame, so what follows measures the steady-state interval.
    let mut seen_first = false;
    let mut since_key = 0usize;
    let mut longest_gap = 0usize;
    for index in 0..300 {
        let Some(unit) = encoder
            .submit(&moving_frame(index, 1280, 720), false)
            .unwrap()
        else {
            continue;
        };
        if unit.key_frame {
            if seen_first {
                longest_gap = longest_gap.max(since_key);
            }
            seen_first = true;
            since_key = 0;
        } else if seen_first {
            since_key += 1;
        }
    }

    assert!(seen_first, "no key frame at all in 300 frames");
    // Compared against the configured interval with headroom, not against an exact count: the
    // encoder is free to place an IDR early, and some do when the scene changes enough.
    let allowed = (gop_size(60) as usize) + 2;
    assert!(
        longest_gap <= allowed,
        "went {longest_gap} frames without a key frame, which is longer than the {allowed} the          configured interval allows; a receiver recovering from loss would be frozen for that long"
    );
}

#[test]
fn asking_for_a_key_frame_is_not_an_error_even_when_the_driver_ignores_it() {
    // force_key_frame is best-effort by contract: this project has a driver that ignores it, which
    // is exactly why the bounded interval above exists. What must never happen is a failure.
    let Some(luid) = capture_adapter() else {
        return;
    };
    let Ok(mut encoder) = HardwareH264Encoder::new(config(1280, 720), luid) else {
        return;
    };

    for index in 0..20 {
        encoder
            .submit(&moving_frame(index, 1280, 720), true)
            .expect("requesting a key frame must never fail the session");
    }
}
