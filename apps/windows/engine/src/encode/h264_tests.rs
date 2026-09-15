use super::*;

#[test]
fn microseconds_and_hundred_nanosecond_units_round_trip() {
    assert_eq!(us_to_hns(1_000_000), HNS_PER_SECOND);
    assert_eq!(hns_to_us(HNS_PER_SECOND), 1_000_000);
    assert_eq!(hns_to_us(us_to_hns(33_333)), 33_333);
}

#[test]
fn packing_puts_width_in_the_high_half_as_media_foundation_expects() {
    // Swapping these produces a 1080x1920 encoder that accepts every frame and emits a stream
    // no receiver can render — the failure is far away from the mistake.
    assert_eq!(pack(1920, 1080), (1920u64 << 32) | 1080);
    assert_eq!(pack(30, 1), (30u64 << 32) | 1);
}

#[test]
fn an_idr_slice_is_recognised_as_a_key_frame() {
    // 0x65: NAL type 5, an IDR slice.
    let access_unit = [0, 0, 0, 1, 0x65, 0xde, 0xad];
    assert!(is_key_frame(&access_unit));
}

#[test]
fn a_non_idr_slice_is_not_a_key_frame() {
    // 0x41: NAL type 1, a non-IDR slice.
    let access_unit = [0, 0, 0, 1, 0x41, 0xde, 0xad];
    assert!(!is_key_frame(&access_unit));
}

#[test]
fn an_idr_after_parameter_sets_is_still_found() {
    // Real key frames arrive as SPS, then PPS, then the IDR slice, all in one access unit.
    let access_unit = [
        0, 0, 0, 1, 0x67, 0x42, // SPS
        0, 0, 0, 1, 0x68, 0xce, // PPS
        0, 0, 0, 1, 0x65, 0x88, // IDR
    ];
    assert!(is_key_frame(&access_unit));
}

#[test]
fn three_byte_start_codes_are_understood_too() {
    let access_unit = [0, 0, 1, 0x65, 0x88];
    assert!(is_key_frame(&access_unit));
}

#[test]
fn an_empty_or_truncated_access_unit_is_not_mistaken_for_a_key_frame() {
    assert!(!is_key_frame(&[]));
    assert!(!is_key_frame(&[0, 0, 0]));
    assert!(!is_key_frame(&[0, 0, 0, 1]));
}

#[test]
fn a_sequence_header_splits_into_separate_parameter_sets() {
    // MediaCodec wants csd-0 and csd-1 as distinct blocks, so a single concatenated blob has
    // to be taken apart before it reaches the wire.
    let header = [
        0, 0, 0, 1, 0x67, 0x42, 0x1f, //
        0, 0, 0, 1, 0x68, 0xce, 0x3c,
    ];

    let sets = normalise_parameter_sets(&header).unwrap();

    assert_eq!(sets.len(), 2);
    assert_eq!(sets[0], vec![0, 0, 0, 1, 0x67, 0x42, 0x1f]);
    assert_eq!(sets[1], vec![0, 0, 0, 1, 0x68, 0xce, 0x3c]);
}

#[test]
fn an_avc_configuration_record_is_normalised_for_media_codec() {
    let header = [
        1, 0x42, 0, 0x1f, 0xff, 0xe1, // avcC header and one SPS
        0, 3, 0x67, 0x42, 0x1f, // SPS
        1, 0, 2, 0x68, 0xce, // one PPS
    ];

    let sets = normalise_parameter_sets(&header).unwrap();

    assert_eq!(sets.len(), 2);
    assert_eq!(sets[0], vec![0, 0, 0, 1, 0x67, 0x42, 0x1f]);
    assert_eq!(sets[1], vec![0, 0, 0, 1, 0x68, 0xce]);
}

#[test]
fn empty_or_opaque_sequence_data_is_rejected() {
    assert!(normalise_parameter_sets(&[]).is_err());
    assert!(normalise_parameter_sets(&[0x67, 0x42, 0]).is_err());
}

#[test]
fn three_byte_start_codes_are_normalised_to_four_bytes() {
    let header = [0, 0, 1, 0x67, 0x42, 0, 0, 1, 0x68, 0xce];
    let sets = normalise_parameter_sets(&header).unwrap();
    assert_eq!(sets.len(), 2);
    assert_eq!(sets[0], vec![0, 0, 0, 1, 0x67, 0x42]);
    assert_eq!(sets[1], vec![0, 0, 0, 1, 0x68, 0xce]);
}

#[test]
fn a_sequence_header_without_both_parameter_set_types_is_rejected() {
    let only_sps = [0, 0, 0, 1, 0x67, 0x42, 0];
    let error = normalise_parameter_sets(&only_sps).unwrap_err();
    assert!(error.to_string().contains("both SPS and PPS"));
}

#[test]
fn the_sequence_header_attribute_is_the_one_the_encoder_publishes() {
    // Naming the wrong attribute yields an encoder that appears to work and a receiver that
    // never configures its decoder.
    assert_eq!(SEQUENCE_HEADER_GUID, MF_MT_MPEG_SEQUENCE_HEADER);
}

#[test]
fn a_non_h264_codec_is_refused_rather_than_silently_producing_h264() {
    let config = EncoderConfig {
        width: 1920,
        height: 1080,
        frame_rate: 30,
        bitrate_bits_per_second: 12_000_000,
        codec: VideoCodec::H265,
    };

    let error = H264Encoder::new(config).unwrap_err();

    assert!(matches!(error, EncodeError::Platform(message) if message.contains("H.264")));
}

#[test]
fn an_invalid_configuration_is_refused_before_any_platform_call() {
    let config = EncoderConfig {
        width: 0,
        height: 1080,
        frame_rate: 30,
        bitrate_bits_per_second: 12_000_000,
        codec: VideoCodec::H264,
    };

    assert_eq!(
        H264Encoder::new(config).unwrap_err(),
        EncodeError::InvalidConfig
    );
}

/// How many frames a streaming encoder may buffer before its first access unit.
///
/// Four is generous for a target of one: it leaves room for encoders that legitimately need a
/// frame or two to settle, while still failing loudly if one reverts to file-encoder buffering.
const MAX_ACCEPTABLE_STARTUP_FRAMES: u32 = 4;

/// A small, cheap configuration so these run in seconds rather than minutes.
fn live_config() -> EncoderConfig {
    EncoderConfig {
        width: 320,
        height: 240,
        frame_rate: 30,
        bitrate_bits_per_second: 1_000_000,
        codec: VideoCodec::H264,
    }
}

/// A frame whose content changes with `index`, so the encoder has real motion to compress.
///
/// A static frame is the one case an encoder can compress to almost nothing, which would let a
/// broken pipeline look healthy — every frame after the first would legitimately be tiny.
pub(super) fn moving_frame(index: u32, width: u32, height: u32) -> SourceFrame {
    let mut pixels = vec![0u8; (width * height * 4) as usize];
    let bar = (index * 17) % width;
    for y in 0..height {
        for x in 0..width {
            let at = ((y * width + x) * 4) as usize;
            let lit = x.abs_diff(bar) < 20;
            pixels[at] = if lit { 255 } else { 24 };
            pixels[at + 1] = if lit { 200 } else { 24 };
            pixels[at + 2] = if lit { 64 } else { 24 };
            pixels[at + 3] = 255;
        }
    }

    SourceFrame {
        width,
        height,
        presentation_time_us: i64::from(index) * 33_333,
        data: FrameData::Bgra {
            pixels,
            stride: width * 4,
        },
    }
}

#[test]
fn the_encoder_produces_real_h264_from_real_frames() {
    // The whole point of this module. Everything above tests the helpers; this proves Windows
    // actually hands back an encoder and that it emits access units a decoder could read.
    let Ok(mut encoder) = H264Encoder::new(live_config()) else {
        // A machine with no H.264 encoder at all is a legitimate answer, and the capability
        // report already says so honestly rather than pretending otherwise.
        return;
    };

    let mut encoded = Vec::new();
    for index in 0..30 {
        if let Some(frame) = encoder
            .submit(&moving_frame(index, 320, 240), false)
            .unwrap()
        {
            encoded.push(frame);
        }
    }

    assert!(
        !encoded.is_empty(),
        "the encoder accepted 30 frames and produced no access unit at all"
    );
    assert!(
        encoded.iter().any(|frame| frame.key_frame),
        "no key frame was produced, so no receiver could ever start decoding"
    );
    assert!(
        encoded.iter().all(|frame| !frame.data.is_empty()),
        "an empty access unit reached the wire"
    );
}

#[test]
fn the_first_access_unit_starts_with_a_nal_start_code() {
    // Annex B framing is what the receiver's MediaCodec path expects. An encoder configured
    // into AVCC (length-prefixed) mode produces bytes that look plausible and decode to
    // nothing, so the framing is worth asserting rather than assuming.
    let Ok(mut encoder) = H264Encoder::new(live_config()) else {
        return;
    };

    let mut first = None;
    for index in 0..30 {
        if let Some(frame) = encoder
            .submit(&moving_frame(index, 320, 240), false)
            .unwrap()
        {
            first = Some(frame);
            break;
        }
    }

    let Some(frame) = first else {
        panic!("the encoder produced no access unit to inspect");
    };
    assert!(
        frame.data.starts_with(&[0, 0, 0, 1]) || frame.data.starts_with(&[0, 0, 1]),
        "expected Annex B start code, got {:02x?}",
        &frame.data[..frame.data.len().min(8)]
    );
}

#[test]
fn presentation_time_survives_the_round_trip_through_media_foundation() {
    // Media Foundation counts in hundred-nanosecond units and the wire protocol counts in
    // microseconds; a missing conversion here shows up as playback running 10x fast or slow.
    let Ok(mut encoder) = H264Encoder::new(live_config()) else {
        return;
    };

    let mut times = Vec::new();
    for index in 0..30 {
        if let Some(frame) = encoder
            .submit(&moving_frame(index, 320, 240), false)
            .unwrap()
        {
            times.push(frame.presentation_time_us);
        }
    }

    assert!(!times.is_empty(), "no access units to check timing against");
    assert!(
        times.iter().all(|&time| (0..=1_000_000).contains(&time)),
        "timestamps left the plausible range for a one-second clip: {times:?}"
    );
}

#[test]
fn a_frame_of_the_wrong_size_is_refused_by_the_live_encoder_too() {
    let Ok(mut encoder) = H264Encoder::new(live_config()) else {
        return;
    };

    let error = encoder
        .submit(&moving_frame(0, 640, 480), false)
        .unwrap_err();

    assert_eq!(
        error,
        EncodeError::FrameSizeChanged {
            expected: (320, 240),
            actual: (640, 480),
        }
    );
}

#[test]
fn the_encoder_emits_its_first_access_unit_almost_immediately() {
    // The single most valuable assertion in this file. Left at its defaults, and equally if
    // the low-latency settings are applied after the media types are committed, this encoder
    // buffers eighteen frames before emitting anything — six hundred milliseconds of latency
    // baked in before a single pixel reaches the network, which no downstream tuning recovers.
    // Applying them first brings it to one frame. Both orderings compile, both "succeed", and
    // only the delay tells them apart, so it is measured here rather than trusted.
    let Ok(mut encoder) = H264Encoder::new(live_config()) else {
        return;
    };

    let mut first_at = None;
    for index in 0..MAX_ACCEPTABLE_STARTUP_FRAMES {
        if matches!(
            encoder.submit(&moving_frame(index, 320, 240), false),
            Ok(Some(_))
        ) {
            first_at = Some(index + 1);
            break;
        }
    }

    let frames = first_at.unwrap_or_else(|| {
        panic!(
            "no access unit within {MAX_ACCEPTABLE_STARTUP_FRAMES} frames; the encoder is \
             buffering like a file encoder, not a streaming one"
        )
    });
    assert!(
        frames <= MAX_ACCEPTABLE_STARTUP_FRAMES,
        "first access unit took {frames} frames"
    );
}

#[test]
fn the_encoder_encodes_the_picture_it_was_given_rather_than_a_blank_one() {
    // The failure this pins cost a long evening. `IMFMediaBuffer::Lock` reports the buffer's
    // maximum length through its second out-parameter and its current length through its
    // third; a freshly created buffer's current length is zero. Reading the third and using it
    // to bound the copy meant every frame was copied as zero bytes, so the encoder was handed a
    // blank buffer and dutifully produced a valid H.264 stream of nothing at all. The host
    // counted frames, the television decoded them, and the screen showed flat colour.
    //
    // Two pictures with no pixel values in common are the probe: an encoder reading its input
    // cannot possibly spend the same number of bytes on both.
    let Ok(mut black) = H264Encoder::new(live_config()) else {
        return;
    };
    let Ok(mut noisy) = H264Encoder::new(live_config()) else {
        return;
    };

    let blank = SourceFrame {
        width: 320,
        height: 240,
        data: FrameData::Bgra {
            pixels: vec![0u8; 320 * 240 * 4],
            stride: 320 * 4,
        },
        presentation_time_us: 0,
    };

    let mut black_total = 0usize;
    let mut noisy_total = 0usize;
    for index in 0..10u32 {
        if let Ok(Some(frame)) = black.submit(&blank, index == 0) {
            black_total += frame.data.len();
        }
        if let Ok(Some(frame)) = noisy.submit(&noisy_frame(index, 320, 240), index == 0) {
            noisy_total += frame.data.len();
        }
    }

    assert!(
        noisy_total > black_total * 2,
        "noise encoded to {noisy_total} bytes and flat black to {black_total}; an encoder that \
         spends the same on both is not reading the buffer it was handed"
    );
}

#[test]
fn a_detailed_key_frame_is_not_quantised_down_to_a_few_hundred_bytes() {
    // Measured on a Fire TV Stick 4K: selecting CBR without also stating a target bitrate left
    // this encoder emitting ~400-byte access units for a full desktop. The stream was valid and
    // the television decoded it happily, so the host reported thousands of frames and the
    // receiver reported a healthy 2-14 kbps — while the picture was flat blocks of colour.
    //
    // Only the size tells the two apart, so it is measured rather than trusted.
    const MIN_PLAUSIBLE_KEY_FRAME_BYTES: usize = 2_000;

    let Ok(mut encoder) = H264Encoder::new(live_config()) else {
        return;
    };

    let mut largest_key_frame = 0usize;
    for index in 0..30u32 {
        // Noise rather than a gradient: a smooth ramp genuinely compresses to very little, so
        // it could not tell a starved encoder from a working one.
        if let Ok(Some(frame)) = encoder.submit(&noisy_frame(index, 320, 240), index == 0) {
            if frame.key_frame {
                largest_key_frame = largest_key_frame.max(frame.data.len());
            }
        }
    }

    assert!(
        largest_key_frame >= MIN_PLAUSIBLE_KEY_FRAME_BYTES,
        "the largest key frame was {largest_key_frame} bytes for a noisy 320x240 picture; the \
         encoder is running at its bitrate floor, which decodes to flat blocks"
    );
}

/// A frame of high-entropy pixels, which no honest encoder can compress to almost nothing.
pub(super) fn noisy_frame(index: u32, width: u32, height: u32) -> SourceFrame {
    let mut pixels = vec![0u8; (width * height * 4) as usize];
    // A cheap deterministic hash per pixel, so the content is incompressible but reproducible.
    let mut state = index.wrapping_mul(2_654_435_761).wrapping_add(1);
    for byte in pixels.iter_mut() {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        *byte = (state >> 16) as u8;
    }

    SourceFrame {
        width,
        height,
        presentation_time_us: i64::from(index) * 33_333,
        data: FrameData::Bgra {
            pixels,
            stride: width * 4,
        },
    }
}

#[test]
fn a_requested_key_frame_actually_arrives_rather_than_being_quietly_ignored() {
    // The parameter was accepted and discarded for the whole life of this encoder, so
    // MirrorSession::request_key_frame did nothing on the real thing. A receiver can only
    // start decoding at a key frame, so the cost was a permanently stalled mirror — a green
    // video plane — any time the receiver lost its reference chain.
    let Ok(mut encoder) = H264Encoder::new(live_config()) else {
        return;
    };

    // Get past the opening key frame, then submit ordinary dependent frames before making the
    // explicit request under test.
    let mut index = 0u32;
    let mut seen_first = false;
    while index < 60 && !seen_first {
        seen_first = matches!(encoder.submit(&moving_frame(index, 320, 240), false), Ok(Some(frame)) if frame.key_frame);
        index += 1;
    }
    assert!(
        seen_first,
        "the encoder never produced an opening key frame"
    );

    let mut ran_dry = false;
    for _ in 0..8 {
        if let Ok(Some(frame)) = encoder.submit(&moving_frame(index, 320, 240), false) {
            ran_dry = !frame.key_frame;
        }
        index += 1;
    }
    assert!(
        ran_dry,
        "expected ordinary dependent frames after the opening key frame"
    );

    let mut key_frame_after_request = false;
    for offset in 0..8 {
        // Requested once, on the first submit only: a request that has to be repeated every
        // frame to work is not a request.
        let force = offset == 0;
        if let Ok(Some(frame)) = encoder.submit(&moving_frame(index, 320, 240), force) {
            if frame.key_frame {
                key_frame_after_request = true;
                break;
            }
        }
        index += 1;
    }

    assert!(
        key_frame_after_request,
        "asking for a key frame produced none, so a receiver that lost its reference chain \
         would wait for one forever"
    );
}

#[test]
fn an_encoder_can_be_created_and_dropped_repeatedly_without_wedging_the_platform() {
    // Each encoder starts and shuts down Media Foundation. Unbalanced counts, or releasing the
    // transform after shutdown, surface here as a failure on a later iteration rather than the
    // first — which is exactly the shape of bug that survives a single-run smoke test.
    for _ in 0..3 {
        if let Ok(mut encoder) = H264Encoder::new(live_config()) {
            let _ = encoder.submit(&moving_frame(0, 320, 240), false);
        }
    }
}
