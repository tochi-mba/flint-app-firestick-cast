#![allow(
    clippy::undocumented_unsafe_blocks,
    reason = "tests driving Media Foundation directly to find where a round trip breaks"
)]

use crate::convert::nv12::bgra_to_nv12;
use crate::decode::h264::H264Decoder;
use crate::encode::selected::SelectedEncoder;
use crate::encode::video::{EncoderConfig, FrameData, SourceFrame, VideoEncoder};
use crate::encode::VideoCodec;

/// The size these round trips run at. Small enough to be quick, large enough to be a real picture.
const WIDTH: u32 = 640;
const HEIGHT: u32 = 480;

fn config() -> EncoderConfig {
    EncoderConfig {
        width: WIDTH,
        height: HEIGHT,
        frame_rate: 30,
        bitrate_bits_per_second: 8_000_000,
        codec: VideoCodec::H264,
    }
}

/// A frame with strong, unmistakable structure: a bright bar on a dark ground, moving with `index`.
///
/// Deliberately high contrast. A washed-out or half-decoded picture still has *some* variation, so
/// a subtle test pattern cannot distinguish "decoded correctly" from "decoded to mush"; a hard edge
/// between near-black and near-white can.
fn frame(index: u32) -> SourceFrame {
    let stride = WIDTH * 4;
    let mut pixels = vec![0u8; (stride * HEIGHT) as usize];
    let bar = (index * 37) % WIDTH;
    for y in 0..HEIGHT {
        for x in 0..WIDTH {
            let offset = (y * stride + x * 4) as usize;
            let inside = x.abs_diff(bar) < 60 && y > HEIGHT / 4 && y < HEIGHT * 3 / 4;
            let value = if inside { 235 } else { 20 };
            pixels[offset] = value;
            pixels[offset + 1] = value;
            pixels[offset + 2] = value;
            pixels[offset + 3] = 255;
        }
    }
    SourceFrame {
        width: WIDTH,
        height: HEIGHT,
        data: FrameData::Bgra { pixels, stride },
        presentation_time_us: i64::from(index) * 33_333,
    }
}

/// Mean and range of a plane, for saying plainly whether it carries a picture.
fn spread(bytes: &[u8]) -> (u8, u8, f64) {
    let mut lowest = u8::MAX;
    let mut highest = u8::MIN;
    let mut total = 0u64;
    for &byte in bytes {
        lowest = lowest.min(byte);
        highest = highest.max(byte);
        total += u64::from(byte);
    }
    (lowest, highest, total as f64 / bytes.len().max(1) as f64)
}

/// The adapter desktop duplication runs on, which is the one a live session encodes on.
fn capture_adapter() -> Option<i64> {
    crate::capture::duplication::DesktopDuplication::open_primary()
        .ok()
        .map(|duplication| duplication.format().adapter_luid)
}

#[test]
#[ignore = "needs a hardware encoder; run deliberately"]
fn what_the_hardware_encoder_produces_decodes_back_into_a_picture() {
    // The software round trip below proves the colour conversion and the bitstream framing. This
    // proves the path a live session actually takes, which is the hardware encoder on the capture
    // adapter — a different transform, a different driver, and its own chances to emit something
    // that is structurally valid and visually blank.
    let Some(adapter) = capture_adapter() else {
        println!("NO DESKTOP");
        return;
    };
    // Constructed directly rather than through selection, which now defaults to software: a test
    // named for the hardware encoder must exercise the hardware encoder or say it could not.
    let Ok(mut encoder) = crate::encode::h264_hardware::HardwareH264Encoder::new(config(), adapter)
    else {
        println!("NO HARDWARE ENCODER");
        return;
    };
    println!("encoder: Hardware");

    let Ok(mut decoder) = H264Decoder::new(WIDTH, HEIGHT) else {
        println!("NO DECODER");
        return;
    };

    for block in encoder.codec_specific_data() {
        let _ = decoder
            .decode(block)
            .expect("parameter sets must be accepted");
    }

    let mut decoded = Vec::new();
    for index in 0..40 {
        if let Some(unit) = encoder
            .submit(&frame(index), false)
            .expect("encoding must not fail")
        {
            if let Some(picture) = decoder.decode(&unit.data).expect("decoding must not fail") {
                decoded.push(picture);
            }
        }
    }
    decoded.extend(decoder.drain().expect("draining must not fail"));

    let picture = decoded
        .first()
        .expect("the decoder produced nothing from the hardware encoder's output");

    let (luma_low, luma_high, luma_mean) = spread(picture.luma());
    let (_, _, chroma_mean) = spread(picture.chroma());
    println!("decoded {} frames", decoded.len());
    println!(
        "buffer {} bytes, expected {} for {WIDTH}x{HEIGHT}",
        picture.nv12.len(),
        WIDTH * HEIGHT * 3 / 2
    );
    match decoder.negotiated_output() {
        Ok((width, height, stride)) => {
            println!("decoder negotiated {width}x{height}, stride {stride}");
        }
        Err(error) => println!("no negotiated output type: {error}"),
    }
    println!("luma {luma_low}..{luma_high} mean {luma_mean:.1}, chroma mean {chroma_mean:.1}");

    assert!(
        luma_high > luma_low,
        "the hardware encoder's output decodes to a uniform field ({luma_low}) — a green screen"
    );
    assert!(
        luma_high > 150,
        "the bright bar never survived the hardware round trip (peak {luma_high})"
    );
    assert!(
        (chroma_mean - 128.0).abs() < 24.0,
        "a grey test pattern must decode to neutral chroma, got mean {chroma_mean:.1}"
    );
}

// Not ignored. This is the test that catches a green television, it needs only Media Foundation
// rather than a desktop or a GPU, and it runs in under two seconds. Every structural test in this
// crate passed while the shipped encoder produced nothing but zeroes; this one did not.
#[test]
fn what_flint_encodes_decodes_back_into_a_picture() {
    // The test that separates a host bug from a receiver bug. If this passes, the bytes leaving
    // this machine are a real picture and a green television is the receiver's doing; if it fails,
    // the fault never left the host. Before this existed the only way to tell was to look at a
    // television across the room.
    let Ok(mut encoder) = SelectedEncoder::open(config(), i64::MAX) else {
        println!("NO ENCODER");
        return;
    };
    println!("encoder: {:?}", encoder.kind());

    let Ok(mut decoder) = H264Decoder::new(WIDTH, HEIGHT) else {
        println!("NO DECODER");
        return;
    };

    // Parameter sets first, exactly as the receiver is given them in VIDEO_CONFIG.
    for block in encoder.codec_specific_data() {
        let _ = decoder
            .decode(block)
            .expect("parameter sets must be accepted");
    }

    let mut decoded = Vec::new();
    for index in 0..40 {
        let Some(unit) = encoder
            .submit(&frame(index), false)
            .expect("encoding must not fail")
        else {
            continue;
        };
        if let Some(picture) = decoder.decode(&unit.data).expect("decoding must not fail") {
            decoded.push(picture);
        }
    }
    decoded.extend(decoder.drain().expect("draining must not fail"));

    let picture = decoded
        .first()
        .unwrap_or_else(|| panic!("the decoder produced nothing from {} access units", 40));

    let (luma_low, luma_high, luma_mean) = spread(picture.luma());
    let (chroma_low, chroma_high, chroma_mean) = spread(picture.chroma());
    println!("decoded {} frames", decoded.len());
    println!("luma   {luma_low}..{luma_high} mean {luma_mean:.1}");
    println!("chroma {chroma_low}..{chroma_high} mean {chroma_mean:.1}");

    // A flat green television is exactly an all-zero decode, so that is the thing to assert
    // against, in the terms the symptom presents itself in.
    assert!(
        luma_high > luma_low,
        "the decoded luma plane is uniform ({luma_low}); this is what a green screen is made of"
    );
    assert!(
        luma_mean > 8.0,
        "the decoded picture is essentially black (mean {luma_mean:.1})"
    );
    assert!(
        luma_high > 150,
        "the bright bar never survived the round trip (peak {luma_high})"
    );
    assert!(
        (chroma_mean - 128.0).abs() < 24.0,
        "a grey test pattern must decode to neutral chroma, got mean {chroma_mean:.1}"
    );
}

#[test]
#[ignore = "needs a real encoder and decoder; run deliberately"]
fn the_decoded_picture_is_written_out_for_a_person_to_look_at() {
    // Numbers can agree while a picture is still visibly wrong — shifted planes, swapped chroma,
    // half a frame. This writes the round trip to disk so it can be judged by eye.
    // The hardware encoder where one exists, because that is the path a live session takes and the
    // one whose output has never been looked at.
    let adapter = capture_adapter().unwrap_or(i64::MAX);
    let Ok(mut encoder) = SelectedEncoder::open(config(), adapter) else {
        return;
    };
    println!("encoder: {:?}", encoder.kind());
    let Ok(mut decoder) = H264Decoder::new(WIDTH, HEIGHT) else {
        return;
    };

    for block in encoder.codec_specific_data() {
        let _ = decoder.decode(block);
    }

    let mut last = None;
    for index in 0..40 {
        if let Ok(Some(unit)) = encoder.submit(&frame(index), false) {
            if let Ok(Some(picture)) = decoder.decode(&unit.data) {
                last = Some(picture);
            }
        }
    }
    if last.is_none() {
        last = decoder
            .drain()
            .ok()
            .and_then(|frames| frames.into_iter().next());
    }

    let Some(picture) = last else {
        println!("NO DECODED FRAME");
        return;
    };

    let path = std::env::temp_dir().join("flint-roundtrip-decoded.bmp");
    let bgra = crate::session::live_pipeline::nv12_to_bgra(&picture.nv12, WIDTH, HEIGHT);
    crate::session::live_pipeline::write_bmp(&path, &bgra, WIDTH, HEIGHT, WIDTH * 4);
    println!("wrote {}", path.display());
}

/// A sanity check on the conversion the round trip leans on.
#[test]
fn a_flat_grey_frame_converts_to_neutral_chroma() {
    // If this were wrong, every round-trip chroma assertion above would be measuring the converter
    // rather than the codec.
    let stride = 16 * 4;
    let pixels = vec![128u8; (stride * 16) as usize];
    let mut nv12 = Vec::new();
    bgra_to_nv12(&pixels, 16, 16, stride, &mut nv12).expect("conversion must succeed");

    let (low, high, mean) = spread(&nv12[(16 * 16) as usize..]);
    assert_eq!(low, high, "flat grey must give a flat chroma plane");
    assert!(
        (mean - 128.0).abs() < 1.5,
        "neutral grey must sit at chroma 128, got {mean}"
    );
}

#[test]
#[ignore = "needs a hardware encoder; run deliberately"]
fn report_what_the_hardware_encoder_actually_says_about_its_own_stream() {
    // Narrowing an all-zero decode. The stream is structurally valid H.264, so the question is
    // whether it describes the picture we think it does.
    let Some(adapter) = capture_adapter() else {
        println!("NO DESKTOP");
        return;
    };
    let Ok(mut encoder) = SelectedEncoder::open(config(), adapter) else {
        println!("NO ENCODER");
        return;
    };
    println!("encoder: {:?}, configured {WIDTH}x{HEIGHT}", encoder.kind());

    for (index, block) in encoder.codec_specific_data().iter().enumerate() {
        println!(
            "  csd-{index}: {} bytes, head {:02x?}",
            block.len(),
            &block[..block.len().min(10)]
        );
        match crate::encode::sps::dimensions_from_annex_b(block) {
            Ok(size) => println!("    SPS says {}x{}", size.width, size.height),
            Err(error) => println!("    no SPS here: {error}"),
        }
    }

    // Sizes over time, because an encoder that starts blank and becomes real is a priming problem,
    // while one that stays blank is not reading our buffer at all. 800 bytes is a blank 640x480
    // key frame; this test pattern should cost several kilobytes.
    let mut produced = 0;
    for index in 0..60 {
        if let Ok(Some(unit)) = encoder.submit(&frame(index), false) {
            produced += 1;
            if produced <= 12 {
                println!(
                    "  unit {produced}: {} bytes, key {}",
                    unit.data.len(),
                    unit.key_frame
                );
            }
        }
    }
    println!("produced {produced} access units from 60 frames");
}

/// Lists the NAL units in an access unit, by type and size.
fn describe_nals(access_unit: &[u8]) -> String {
    let mut parts = Vec::new();
    let mut starts = Vec::new();
    let mut index = 0usize;
    while index + 3 <= access_unit.len() {
        if access_unit[index..].starts_with(&[0, 0, 0, 1]) {
            starts.push((index + 4, 4));
            index += 4;
        } else if access_unit[index..].starts_with(&[0, 0, 1]) {
            starts.push((index + 3, 3));
            index += 3;
        } else {
            index += 1;
        }
    }
    for (position, (payload, _)) in starts.iter().enumerate() {
        let end = starts
            .get(position + 1)
            .map_or(access_unit.len(), |(next, prefix)| next - prefix);
        let header = access_unit[*payload];
        let name = match header & 0x1f {
            1 => "slice",
            5 => "IDR",
            6 => "SEI",
            7 => "SPS",
            8 => "PPS",
            9 => "AUD",
            other => return format!("{parts:?} unknown({other})"),
        };
        parts.push(format!(
            "{name}(ref={} {}B)",
            (header >> 5) & 0x3,
            end.saturating_sub(*payload)
        ));
    }
    parts.join(" ")
}

#[test]
#[ignore = "needs a hardware encoder; run deliberately"]
fn report_how_the_two_encoders_structure_their_bitstreams() {
    // Both streams carry correct parameter sets and correct dimensions, and one decodes into a
    // picture while the other decodes into nothing. Whatever separates them is in the units
    // themselves, so this prints them side by side rather than guessing.
    let adapter = capture_adapter().unwrap_or(i64::MAX);

    let mut software = SelectedEncoder::software_only(config()).ok();
    let mut hardware =
        crate::encode::h264_hardware::HardwareH264Encoder::new(config(), adapter).ok();

    if let Some(encoder) = software.as_mut() {
        println!(
            "SOFTWARE csd: {} blocks",
            encoder.codec_specific_data().len()
        );
        for (index, block) in encoder.codec_specific_data().iter().enumerate() {
            println!("  csd-{index}: {}", describe_nals(block));
        }
        let mut shown = 0;
        for index in 0..30 {
            if let Ok(Some(unit)) = encoder.submit(&frame(index), false) {
                println!(
                    "  unit: key={} {}",
                    unit.key_frame,
                    describe_nals(&unit.data)
                );
                shown += 1;
                if shown == 3 {
                    break;
                }
            }
        }
    }

    if let Some(encoder) = hardware.as_mut() {
        println!(
            "HARDWARE csd: {} blocks",
            encoder.codec_specific_data().len()
        );
        for (index, block) in encoder.codec_specific_data().iter().enumerate() {
            println!("  csd-{index}: {}", describe_nals(block));
        }
        let mut shown = 0;
        for index in 0..30 {
            if let Ok(Some(unit)) = encoder.submit(&frame(index), false) {
                println!(
                    "  unit: key={} {}",
                    unit.key_frame,
                    describe_nals(&unit.data)
                );
                shown += 1;
                if shown == 3 {
                    break;
                }
            }
        }
    } else {
        println!("NO HARDWARE ENCODER");
    }
}

#[test]
#[ignore = "needs a hardware encoder; run deliberately"]
fn write_both_bitstreams_out_for_an_independent_decoder() {
    // Two decoders inside this project disagree with the hardware encoder, but both were written
    // or configured here. Writing the raw streams out lets a third-party decoder settle it, which
    // is the only way to be sure the fault is in the encoder and not in how this project reads its
    // output.
    let adapter = capture_adapter().unwrap_or(i64::MAX);

    if let Ok(mut encoder) = SelectedEncoder::software_only(config()) {
        let mut stream = Vec::new();
        for block in encoder.codec_specific_data() {
            stream.extend_from_slice(block);
        }
        for index in 0..60 {
            if let Ok(Some(unit)) = encoder.submit(&frame(index), false) {
                stream.extend_from_slice(&unit.data);
            }
        }
        let path = std::env::temp_dir().join("flint-software.h264");
        std::fs::write(&path, &stream).expect("writing the software stream");
        println!("software: {} bytes -> {}", stream.len(), path.display());
    }

    if let Ok(mut encoder) =
        crate::encode::h264_hardware::HardwareH264Encoder::new(config(), adapter)
    {
        let mut stream = Vec::new();
        for block in encoder.codec_specific_data() {
            stream.extend_from_slice(block);
        }
        for index in 0..60 {
            if let Ok(Some(unit)) = encoder.submit(&frame(index), false) {
                stream.extend_from_slice(&unit.data);
            }
        }
        let path = std::env::temp_dir().join("flint-hardware.h264");
        std::fs::write(&path, &stream).expect("writing the hardware stream");
        println!("hardware: {} bytes -> {}", stream.len(), path.display());
    } else {
        println!("NO HARDWARE ENCODER");
    }
}

#[test]
#[ignore = "needs a hardware encoder; run deliberately"]
fn report_what_the_hardware_transform_expects_of_its_input() {
    // The encoder emits a valid, decodable stream of the right size that contains nothing but
    // zeroes, which means it is not reading the buffer it was handed. Rather than guess at why,
    // ask it what it wants: the stream info carries the size, the alignment and the flags that say
    // who allocates.
    let Some(adapter) = capture_adapter() else {
        println!("NO DESKTOP");
        return;
    };
    let Ok(hardware) = crate::encode::h264_hardware_transform::HardwareTransform::open(adapter)
    else {
        println!("NO HARDWARE ENCODER");
        return;
    };

    let transform = hardware.transform();
    let mut input_info = windows::Win32::Media::MediaFoundation::MFT_INPUT_STREAM_INFO::default();
    // SAFETY: stream 0 exists on every encoder and the out-parameter is valid.
    match unsafe { transform.GetInputStreamInfo(0, &raw mut input_info) } {
        Ok(()) => println!(
            "input: cbSize {} alignment {} maxLookahead {} flags {:#010x}",
            input_info.cbSize,
            input_info.cbAlignment,
            input_info.cbMaxLookahead,
            input_info.dwFlags
        ),
        Err(error) => println!("no input stream info: {error}"),
    }
    // SAFETY: as above.
    match unsafe { transform.GetOutputStreamInfo(0) } {
        Ok(info) => println!(
            "output: cbSize {} alignment {} flags {:#010x}",
            info.cbSize, info.cbAlignment, info.dwFlags
        ),
        Err(error) => println!("no output stream info: {error}"),
    }

    println!("our NV12 for 640x480 is {} bytes", 640 * 480 * 3 / 2);

    // Which input types the transform will actually accept, in its own order of preference.
    for index in 0..8u32 {
        // SAFETY: enumeration ends with an error, which breaks the loop.
        let Ok(media_type) = (unsafe { transform.GetInputAvailableType(0, index) }) else {
            break;
        };
        // SAFETY: the type is live.
        let subtype =
            unsafe { media_type.GetGUID(&windows::Win32::Media::MediaFoundation::MF_MT_SUBTYPE) };
        println!("  input type {index}: {subtype:?}");
    }
}

#[test]
#[ignore = "needs a hardware encoder; run deliberately"]
fn report_whether_the_hardware_encoder_reads_the_buffer_it_is_given() {
    // The narrowest possible question. A solid white frame and a solid black one differ in every
    // luma sample, so if the encoder reads its input at all the two must produce different output.
    // If both come back identical — and identically empty — the encoder is not reading the buffer
    // it was handed, and nothing about rate control, profiles or parameter sets matters yet.
    let Some(adapter) = capture_adapter() else {
        println!("NO DESKTOP");
        return;
    };

    for (label, value) in [("white", 235u8), ("black", 16u8), ("mid", 128u8)] {
        let Ok(mut encoder) =
            crate::encode::h264_hardware::HardwareH264Encoder::new(config(), adapter)
        else {
            println!("NO HARDWARE ENCODER");
            return;
        };

        let stride = WIDTH * 4;
        let flat = SourceFrame {
            width: WIDTH,
            height: HEIGHT,
            data: FrameData::Bgra {
                pixels: vec![value; (stride * HEIGHT) as usize],
                stride,
            },
            presentation_time_us: 0,
        };

        let mut total = 0usize;
        let mut units = 0;
        for _ in 0..30 {
            if let Ok(Some(unit)) = encoder.submit(&flat, false) {
                total += unit.data.len();
                units += 1;
            }
        }
        println!("{label:5}: {units} units, {total} bytes total");
    }
}

#[test]
#[ignore = "needs a hardware encoder; run deliberately"]
fn report_the_input_layout_the_hardware_transform_settled_on() {
    // `SetInputType` succeeding does not mean the encoder accepted the layout that was offered.
    let Some(adapter) = capture_adapter() else {
        println!("NO DESKTOP");
        return;
    };
    let Ok(encoder) = crate::encode::h264_hardware::HardwareH264Encoder::new(config(), adapter)
    else {
        println!("NO HARDWARE ENCODER");
        return;
    };

    match encoder.negotiated_input() {
        Ok((width, height, stride, buffer_size)) => {
            println!(
                "we offered {WIDTH}x{HEIGHT}, stride {WIDTH}, buffer {} bytes",
                WIDTH * HEIGHT * 3 / 2
            );
            println!(
                "transform settled on {width}x{height}, stride {stride}, buffer {buffer_size}"
            );
            if stride as u32 != WIDTH {
                println!(
                    "STRIDE MISMATCH: filling rows at {WIDTH} while it reads them at {stride}"
                );
            }
            if buffer_size != 0 && buffer_size != WIDTH * HEIGHT * 3 / 2 {
                println!("SIZE MISMATCH: it wants {buffer_size} bytes per frame");
            }
        }
        Err(error) => println!("no negotiated input type: {error}"),
    }
}

#[test]
#[ignore = "needs a hardware encoder; run deliberately"]
fn report_why_the_hardware_encoder_will_not_open() {
    // Construction failing is an ordinary answer that selection swallows on purpose, which is
    // exactly the wrong behaviour while the hardware path is being repaired.
    let Some(adapter) = capture_adapter() else {
        println!("NO DESKTOP");
        return;
    };
    match crate::encode::h264_hardware::HardwareH264Encoder::new(config(), adapter) {
        Ok(encoder) => println!("opened; csd blocks {}", encoder.codec_specific_data().len()),
        Err(error) => println!("FAILED: {error:?}"),
    }
}

#[test]
#[ignore = "needs a hardware encoder; run deliberately"]
fn report_which_configuration_step_the_hardware_encoder_rejects() {
    // "The parameter is incorrect" names no parameter, so the steps are walked one at a time.
    use windows::Win32::Media::MediaFoundation as mf;

    let Some(adapter) = capture_adapter() else {
        println!("NO DESKTOP");
        return;
    };
    let Ok(hardware) = crate::encode::h264_hardware_transform::HardwareTransform::open(adapter)
    else {
        println!("open FAILED");
        return;
    };
    println!("open ok (device manager set)");

    let transform = hardware.transform().clone();

    let devices = hardware.devices();
    match crate::encode::nv12_texture::Nv12TexturePool::new(
        devices.device().clone(),
        devices.context().clone(),
        WIDTH,
        HEIGHT,
    ) {
        Ok(_) => println!("texture pool ok"),
        Err(error) => println!("texture pool FAILED: {error:?}"),
    }

    // Output type first, exactly as configuration does it.
    let output = unsafe { mf::MFCreateMediaType() }.expect("a media type");
    unsafe {
        let _ = output.SetGUID(&mf::MF_MT_MAJOR_TYPE, &mf::MFMediaType_Video);
        let _ = output.SetGUID(&mf::MF_MT_SUBTYPE, &mf::MFVideoFormat_H264);
        let _ = output.SetUINT32(&mf::MF_MT_INTERLACE_MODE, 2);
        let _ = output.SetUINT64(
            &mf::MF_MT_FRAME_SIZE,
            crate::encode::h264::pack(WIDTH, HEIGHT),
        );
        let _ = output.SetUINT64(&mf::MF_MT_FRAME_RATE, crate::encode::h264::pack(30, 1));
        let _ = output.SetUINT64(
            &mf::MF_MT_PIXEL_ASPECT_RATIO,
            crate::encode::h264::pack(1, 1),
        );
        let _ = output.SetUINT32(&mf::MF_MT_AVG_BITRATE, 8_000_000);
    }
    match unsafe { transform.SetOutputType(0, &output, 0) } {
        Ok(()) => println!("SetOutputType ok"),
        Err(error) => {
            println!("SetOutputType FAILED: {error}");
            return;
        }
    }

    // Then the input, with and without an explicit stride, to see which the transform objects to.
    for stride in [Some(WIDTH), None] {
        let input = unsafe { mf::MFCreateMediaType() }.expect("a media type");
        unsafe {
            let _ = input.SetGUID(&mf::MF_MT_MAJOR_TYPE, &mf::MFMediaType_Video);
            let _ = input.SetGUID(&mf::MF_MT_SUBTYPE, &mf::MFVideoFormat_NV12);
            let _ = input.SetUINT32(&mf::MF_MT_INTERLACE_MODE, 2);
            let _ = input.SetUINT64(
                &mf::MF_MT_FRAME_SIZE,
                crate::encode::h264::pack(WIDTH, HEIGHT),
            );
            let _ = input.SetUINT64(&mf::MF_MT_FRAME_RATE, crate::encode::h264::pack(30, 1));
            let _ = input.SetUINT64(
                &mf::MF_MT_PIXEL_ASPECT_RATIO,
                crate::encode::h264::pack(1, 1),
            );
            if let Some(value) = stride {
                let _ = input.SetUINT32(&mf::MF_MT_DEFAULT_STRIDE, value);
            }
        }
        match unsafe { transform.SetInputType(0, &input, 0) } {
            Ok(()) => println!("SetInputType stride={stride:?} ok"),
            Err(error) => println!("SetInputType stride={stride:?} FAILED: {error}"),
        }
    }

    // And what the transform says it will accept, now that a D3D manager is attached.
    for index in 0..8u32 {
        let Ok(media_type) = (unsafe { transform.GetInputAvailableType(0, index) }) else {
            break;
        };
        let subtype = unsafe { media_type.GetGUID(&mf::MF_MT_SUBTYPE) };
        println!("  accepts input {index}: {subtype:?}");
    }
}
