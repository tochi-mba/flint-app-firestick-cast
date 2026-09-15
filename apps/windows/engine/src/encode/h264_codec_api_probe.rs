#![allow(
    clippy::undocumented_unsafe_blocks,
    reason = "a report run by hand against this machine's encoder; nothing here ships"
)]

use super::*;
use windows::Win32::Media::MediaFoundation::{
    CODECAPI_AVEncCommonBufferSize, CODECAPI_AVEncCommonMaxBitRate, CODECAPI_AVEncCommonQuality,
    CODECAPI_AVEncCommonQualityVsSpeed, CODECAPI_AVEncVideoEncodeQP,
};

/// Reports what rate control this machine's encoder accepts, and what value actually sticks.
///
/// `SetValue` returning OK does not mean a property took effect: this encoder accepts
/// properties it then ignores. Reading the value back afterwards is the only way to tell.
#[test]
#[ignore = "reports rather than asserts; run deliberately"]
fn report_rate_control_state() {
    let config = EncoderConfig {
        width: 1280,
        height: 720,
        frame_rate: 30,
        bitrate_bits_per_second: 8_000_000,
        codec: VideoCodec::H264,
    };

    let Ok(mut encoder) = H264Encoder::new(config) else {
        println!("NO ENCODER");
        return;
    };

    let Ok(codec_api) = encoder.transform.cast::<ICodecAPI>() else {
        println!("ICodecAPI: NOT AVAILABLE");
        return;
    };

    for (name, guid) in [
        (
            "AVEncCommonRateControlMode",
            CODECAPI_AVEncCommonRateControlMode,
        ),
        ("AVEncCommonMeanBitRate", CODECAPI_AVEncCommonMeanBitRate),
        ("AVEncCommonQuality", CODECAPI_AVEncCommonQuality),
        (
            "AVEncCommonQualityVsSpeed",
            CODECAPI_AVEncCommonQualityVsSpeed,
        ),
        ("AVEncCommonMaxBitRate", CODECAPI_AVEncCommonMaxBitRate),
        ("AVEncCommonBufferSize", CODECAPI_AVEncCommonBufferSize),
        ("AVEncMPVGOPSize", CODECAPI_AVEncMPVGOPSize),
        ("AVEncVideoEncodeQP", CODECAPI_AVEncVideoEncodeQP),
    ] {
        let supported = unsafe { codec_api.IsSupported(&raw const guid) }.is_ok();
        let modifiable = unsafe { codec_api.IsModifiable(&raw const guid) }.is_ok();
        let current = unsafe { codec_api.GetValue(&raw const guid) };
        let readback = match current {
            Ok(value) => format!("{:?}", u32::try_from(&value)),
            Err(error) => format!("unreadable ({})", error.code().0),
        };
        println!("  {name}: supported={supported} modifiable={modifiable} value={readback}");
    }

    // What does a noisy frame actually cost at this bitrate, and is the encoder even seeing a
    // different picture each time? A hundred-byte access unit is what an encoder emits when
    // nothing changed, so the input checksum is the thing that separates "the encoder is
    // starved" from "the encoder is being handed the same frame repeatedly".
    for index in 0..6u32 {
        let source = tests::noisy_frame(index, 1280, 720);
        // This probe builds its own system-memory frames; a texture cannot reach it.
        let FrameData::Bgra { pixels, .. } = &source.data else {
            unreachable!("the probe only ever constructs system-memory frames")
        };
        let source_sum: u64 = pixels.iter().map(|&b| u64::from(b)).sum();

        let outcome = encoder.submit(&source, index == 0);
        let nv12_sum: u64 = encoder.nv12.iter().map(|&b| u64::from(b)).sum();

        match outcome {
            Ok(Some(frame)) => println!(
                "  frame {index}: bgra_sum={source_sum} nv12_sum={nv12_sum} -> {} bytes, key={}",
                frame.data.len(),
                frame.key_frame
            ),
            Ok(None) => {
                println!("  frame {index}: bgra_sum={source_sum} nv12_sum={nv12_sum} -> buffered");
            }
            Err(error) => println!("  frame {index}: FAILED {error}"),
        }
    }
}

/// Encodes noise through every H.264 encoder on this machine and reports what each produced.
///
/// The question this answers: is a flat picture something about our configuration, or about one
/// particular encoder? Incompressible noise is the probe, because a key frame of noise that
/// comes back tiny cannot be anything but wrong.
#[test]
#[ignore = "reports rather than asserts; run deliberately"]
fn report_every_encoder_against_noise() {
    use windows::Win32::Media::MediaFoundation::{IMFActivate, MFT_FRIENDLY_NAME_Attribute};

    let Ok(_platform) = MediaFoundationPlatform::start() else {
        println!("NO MEDIA FOUNDATION");
        return;
    };

    let output_info = MFT_REGISTER_TYPE_INFO {
        guidMajorType: MFMediaType_Video,
        guidSubtype: MFVideoFormat_H264,
    };

    let mut activates: *mut Option<IMFActivate> = std::ptr::null_mut();
    let mut count = 0u32;
    // SAFETY: both out-parameters are valid; the array is freed below.
    let enumerated = unsafe {
        MFTEnumEx(
            MFT_CATEGORY_VIDEO_ENCODER,
            MFT_ENUM_FLAG(MFT_ENUM_FLAG_SYNCMFT.0 | MFT_ENUM_FLAG_SORTANDFILTER.0),
            None,
            Some(&raw const output_info),
            &raw mut activates,
            &raw mut count,
        )
    };
    if enumerated.is_err() || activates.is_null() {
        println!("NO ENCODERS ENUMERATED");
        return;
    }
    println!("{count} H.264 encoder(s) on this machine");

    for index in 0..count as usize {
        // SAFETY: MFTEnumEx populated `count` entries; each is addressed once.
        let entry = unsafe { (*activates.add(index)).clone() };
        let Some(activate) = entry else {
            continue;
        };

        let mut name = [0u16; 128];
        // SAFETY: the buffer outlives the call and its length is passed correctly.
        let name =
            match unsafe { activate.GetString(&MFT_FRIENDLY_NAME_Attribute, &mut name, None) } {
                Ok(()) => String::from_utf16_lossy(&name)
                    .trim_end_matches('\0')
                    .to_string(),
                Err(_) => format!("encoder {index}"),
            };

        // SAFETY: the activation object is live and produces a transform or an error.
        let activated = unsafe { activate.ActivateObject::<IMFTransform>() };
        let Ok(transform) = activated else {
            println!("  {name}: would not activate");
            continue;
        };

        let Ok(session_platform) = MediaFoundationPlatform::start() else {
            println!("  {name}: Media Foundation would not start");
            continue;
        };

        let config = EncoderConfig {
            width: 1280,
            height: 720,
            frame_rate: 30,
            bitrate_bits_per_second: 8_000_000,
            codec: VideoCodec::H264,
        };

        match H264Encoder::from_transform(config, transform, session_platform) {
            Ok(mut encoder) => {
                let mut largest = 0usize;
                let mut total = 0usize;
                for frame_index in 0..20u32 {
                    if let Ok(Some(frame)) = encoder.submit(
                        &tests::noisy_frame(frame_index, 1280, 720),
                        frame_index == 0,
                    ) {
                        total += frame.data.len();
                        largest = largest.max(frame.data.len());
                    }
                }
                println!("  {name}: largest={largest} bytes total={total} bytes");
            }
            Err(error) => println!("  {name}: would not configure ({error})"),
        }
    }

    // SAFETY: every entry was addressed above; the array itself is freed once.
    unsafe {
        for index in 0..count as usize {
            let _ = (*activates.add(index)).take();
        }
        windows::Win32::System::Com::CoTaskMemFree(Some(activates.cast()));
    }
}

/// Reports which low-latency controls this machine's encoder actually accepts.
#[test]
#[ignore = "reports rather than asserts; run deliberately"]
fn report_codec_api_support() {
    let config = EncoderConfig {
        width: 1280,
        height: 720,
        frame_rate: 30,
        bitrate_bits_per_second: 8_000_000,
        codec: VideoCodec::H264,
    };

    let Ok(encoder) = H264Encoder::new(config) else {
        println!("NO ENCODER");
        return;
    };

    match encoder.transform.cast::<ICodecAPI>() {
        Ok(codec_api) => {
            println!("ICodecAPI: available");
            for (name, guid) in [
                ("AVLowLatencyMode", CODECAPI_AVLowLatencyMode),
                ("AVEncCommonRealTime", CODECAPI_AVEncCommonRealTime),
            ] {
                let supported = unsafe { codec_api.IsSupported(&raw const guid) }.is_ok();
                let modifiable = unsafe { codec_api.IsModifiable(&raw const guid) }.is_ok();
                let set = unsafe { codec_api.SetValue(&raw const guid, &VARIANT::from(true)) };
                println!("  {name}: supported={supported} modifiable={modifiable} set={set:?}");
            }
            for (name, guid, value) in [
                (
                    "AVEncMPVDefaultBPictureCount",
                    CODECAPI_AVEncMPVDefaultBPictureCount,
                    0u32,
                ),
                (
                    "AVEncCommonRateControlMode",
                    CODECAPI_AVEncCommonRateControlMode,
                    RATE_CONTROL_CBR,
                ),
            ] {
                let supported = unsafe { codec_api.IsSupported(&raw const guid) }.is_ok();
                let set = unsafe { codec_api.SetValue(&raw const guid, &VARIANT::from(value)) };
                println!("  {name}: supported={supported} set={set:?}");
            }
        }
        Err(error) => println!("ICodecAPI: NOT AVAILABLE ({error})"),
    }

    // How many frames before the first access unit appears?
    let mut encoder = encoder;
    let mut first_at = None;
    for index in 0..60u32 {
        if let Ok(Some(_)) = encoder.submit(&tests::moving_frame(index, 1280, 720), false) {
            first_at = Some(index);
            break;
        }
    }
    match first_at {
        Some(index) => println!("first access unit after {} frames", index + 1),
        None => println!("no access unit within 60 frames"),
    }
}
