#![allow(
    clippy::undocumented_unsafe_blocks,
    reason = "test-only decoding: every block is a Media Foundation call with the arguments it names"
)]

use super::*;
use windows::Win32::Media::MediaFoundation::{CLSID_MSAACDecMFT, MFT_CATEGORY_AUDIO_DECODER};

/// Opens the encoder, or `None` on a Windows without one, where these tests have nothing to check.
fn encoder(kbps: u32) -> Option<AacEncoder> {
    match AacEncoder::new(kbps) {
        Ok(encoder) => Some(encoder),
        Err(AudioError::NoEncoder) => None,
        Err(error) => panic!("the AAC encoder would not open: {error}"),
    }
}

/// `seconds` of a tone at `hz`, interleaved stereo, at about -12 dBFS.
fn tone(hz: f64, seconds: f64) -> Vec<i16> {
    let frames = (f64::from(SAMPLE_RATE) * seconds) as usize;
    (0..frames)
        .flat_map(|frame| {
            let value = ((frame as f64 * hz * std::f64::consts::TAU / f64::from(SAMPLE_RATE)).sin()
                * 8_000.0) as i16;
            [value, value]
        })
        .collect()
}

/// Noise, which makes the encoder spend its whole data rate.
fn noise(seconds: f64) -> Vec<i16> {
    let mut state = 0x1234_5678u32;
    (0..(f64::from(SAMPLE_RATE) * seconds) as usize * 2)
        .map(|_| {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            (state >> 18) as i16 - 8_000
        })
        .collect()
}

/// Encodes `pcm` in 10 ms pieces, as the capture thread hands it over.
fn encode_all(encoder: &mut AacEncoder, pcm: &[i16]) -> Vec<AudioPacket> {
    let mut packets = Vec::new();
    for piece in pcm.chunks(480 * 2) {
        encoder.encode(piece, &mut packets).unwrap();
    }
    packets
}

/// Decodes raw AAC frames with Windows' own decoder, back to interleaved 16-bit stereo.
fn decode(config: &[u8], packets: &[AudioPacket]) -> Vec<i16> {
    let _platform = MediaFoundationPlatform::start().unwrap();
    let input = MFT_REGISTER_TYPE_INFO {
        guidMajorType: MFMediaType_Audio,
        guidSubtype: MFAudioFormat_AAC,
    };
    let output = MFT_REGISTER_TYPE_INFO {
        guidMajorType: MFMediaType_Audio,
        guidSubtype: MFAudioFormat_PCM,
    };
    let decoder = activate_first(MFT_CATEGORY_AUDIO_DECODER, &input, &output)
        .unwrap()
        .unwrap_or_else(|| unsafe {
            windows::Win32::System::Com::CoCreateInstance(
                &CLSID_MSAACDecMFT,
                None,
                windows::Win32::System::Com::CLSCTX_INPROC_SERVER,
            )
            .unwrap()
        });

    let mut user_data = vec![0u8; WAVE_INFO_BYTES];
    user_data[2] = 0x29;
    user_data.extend_from_slice(config);
    unsafe {
        let media = MFCreateMediaType().unwrap();
        media
            .SetGUID(&MF_MT_MAJOR_TYPE, &MFMediaType_Audio)
            .unwrap();
        media.SetGUID(&MF_MT_SUBTYPE, &MFAudioFormat_AAC).unwrap();
        media
            .SetUINT32(&MF_MT_AUDIO_SAMPLES_PER_SECOND, SAMPLE_RATE)
            .unwrap();
        media
            .SetUINT32(&MF_MT_AUDIO_NUM_CHANNELS, CHANNELS)
            .unwrap();
        media.SetUINT32(&MF_MT_AAC_PAYLOAD_TYPE, 0).unwrap();
        media.SetBlob(&MF_MT_USER_DATA, &user_data).unwrap();
        decoder.SetInputType(0, &media, 0).unwrap();
        decoder.SetOutputType(0, &pcm_type().unwrap(), 0).unwrap();
    }

    let mut pcm = Vec::new();
    for (index, packet) in packets.iter().enumerate() {
        unsafe {
            let length = packet.data.len() as u32;
            let buffer = MFCreateMemoryBuffer(length).unwrap();
            let mut data = std::ptr::null_mut();
            buffer.Lock(&raw mut data, None, None).unwrap();
            std::ptr::copy_nonoverlapping(packet.data.as_ptr(), data, packet.data.len());
            buffer.Unlock().unwrap();
            buffer.SetCurrentLength(length).unwrap();
            let sample = MFCreateSample().unwrap();
            sample.AddBuffer(&buffer).unwrap();
            sample.SetSampleTime(index as i64 * 213_333).unwrap();
            decoder.ProcessInput(0, &sample, 0).unwrap();

            loop {
                let info = decoder.GetOutputStreamInfo(0).unwrap();
                let mut buffers = [MFT_OUTPUT_DATA_BUFFER::default(); 1];
                if info.dwFlags & MFT_OUTPUT_STREAM_PROVIDES_SAMPLES.0 as u32 == 0 {
                    let out = MFCreateMemoryBuffer(info.cbSize.max(8192)).unwrap();
                    let sample = MFCreateSample().unwrap();
                    sample.AddBuffer(&out).unwrap();
                    buffers[0].pSample = std::mem::ManuallyDrop::new(Some(sample));
                }
                let mut status = 0u32;
                if decoder
                    .ProcessOutput(0, &mut buffers, &raw mut status)
                    .is_err()
                {
                    std::mem::ManuallyDrop::drop(&mut buffers[0].pSample);
                    break;
                }
                let sample = buffers[0].pSample.take().unwrap();
                let bytes = read_bytes(&sample).unwrap();
                pcm.extend(
                    bytes
                        .chunks_exact(2)
                        .map(|pair| i16::from_le_bytes([pair[0], pair[1]])),
                );
            }
        }
    }
    pcm
}

/// The power at `hz` in the left channel, by the Goertzel algorithm.
fn power_at(pcm: &[i16], hz: f64) -> f64 {
    let coefficient = 2.0 * (std::f64::consts::TAU * hz / f64::from(SAMPLE_RATE)).cos();
    let (mut previous, mut before) = (0.0f64, 0.0f64);
    for &sample in pcm.iter().step_by(2) {
        let current = f64::from(sample) + coefficient * previous - before;
        before = previous;
        previous = current;
    }
    previous * previous + before * before - coefficient * previous * before
}

fn rms(pcm: &[i16]) -> f64 {
    (pcm.iter()
        .map(|&sample| f64::from(sample).powi(2))
        .sum::<f64>()
        / pcm.len().max(1) as f64)
        .sqrt()
}

#[test]
fn a_tone_survives_the_round_trip_at_its_own_pitch() {
    let Some(mut encoder) = encoder(128) else {
        return;
    };

    let packets = encode_all(&mut encoder, &tone(1_000.0, 1.0));
    let decoded = decode(encoder.audio_specific_config(), &packets);

    assert!(
        decoded.len() > SAMPLE_RATE as usize,
        "about a second came back, got {} samples",
        decoded.len()
    );
    let at_tone = power_at(&decoded, 1_000.0);
    let elsewhere = power_at(&decoded, 3_000.0);
    assert!(
        at_tone > elsewhere * 100.0,
        "1 kHz {at_tone:.3e} against 3 kHz {elsewhere:.3e}"
    );
    assert!(
        rms(&decoded) > 2_000.0,
        "the tone came back at its level, rms {}",
        rms(&decoded)
    );
}

#[test]
fn silence_comes_back_as_silence() {
    let Some(mut encoder) = encoder(128) else {
        return;
    };

    let packets = encode_all(&mut encoder, &vec![0i16; SAMPLE_RATE as usize * 2]);
    let decoded = decode(encoder.audio_specific_config(), &packets);

    assert!(rms(&decoded) < 4.0, "rms {}", rms(&decoded));
}

#[test]
fn the_setup_data_declares_low_complexity_48_khz_stereo() {
    let Some(encoder) = encoder(128) else { return };

    // Object type 2 (low complexity), sample rate index 3 (48 kHz), channel configuration 2.
    assert_eq!(encoder.audio_specific_config(), [0x11, 0x90]);
}

#[test]
fn each_quality_spends_its_data_rate() {
    for kbps in BITRATES_KBPS {
        let Some(mut encoder) = encoder(kbps) else {
            return;
        };

        let packets = encode_all(&mut encoder, &noise(5.0));
        let bytes: usize = packets.iter().map(|packet| packet.data.len()).sum();
        let seconds = packets.len() as f64 * f64::from(SAMPLES_PER_FRAME) / f64::from(SAMPLE_RATE);
        let measured = bytes as f64 * 8.0 / seconds / 1000.0;

        let target = f64::from(kbps);
        assert!(
            (measured - target).abs() <= target * 0.15,
            "{kbps} kbps measured {measured:.1}"
        );
    }
}

#[test]
fn frames_are_timed_by_how_many_samples_came_before() {
    let Some(mut encoder) = encoder(128) else {
        return;
    };

    let packets = encode_all(&mut encoder, &tone(440.0, 0.5));

    assert!(packets.len() >= 20);
    for (index, packet) in packets.iter().enumerate() {
        assert_eq!(
            packet.presentation_time_us,
            index as i64 * 1024 * 1_000_000 / 48_000
        );
    }
}

#[test]
fn only_the_rates_windows_offers_are_accepted() {
    assert!(matches!(
        AacEncoder::new(64),
        Err(AudioError::UnsupportedBitrate(64))
    ));
    assert!(AudioError::UnsupportedBitrate(64)
        .to_string()
        .contains("64 kbps"));
    assert!(AudioError::NoEncoder
        .to_string()
        .contains("Media Feature Pack"));
    assert_eq!(
        AudioError::Platform("refused".into()).to_string(),
        "refused"
    );
}

#[test]
fn setup_data_too_short_to_hold_the_config_is_refused() {
    assert_eq!(audio_specific_config(&[0; 14]).unwrap(), [0, 0]);
    assert!(matches!(
        audio_specific_config(&[0; 13]),
        Err(AudioError::Platform(_))
    ));
}

#[test]
fn nothing_in_is_nothing_out() {
    let Some(mut encoder) = encoder(96) else {
        return;
    };
    let mut packets = Vec::new();

    encoder.encode(&[], &mut packets).unwrap();

    assert!(packets.is_empty());
}

#[test]
fn a_transform_nobody_offers_is_none_rather_than_a_failure() {
    if encoder(128).is_none() {
        return;
    }
    let input = MFT_REGISTER_TYPE_INFO {
        guidMajorType: MFMediaType_Audio,
        guidSubtype: MFAudioFormat_PCM,
    };
    let nobody = MFT_REGISTER_TYPE_INFO {
        guidMajorType: MFMediaType_Audio,
        guidSubtype: GUID::from_u128(0x464c_494e_545f_4e4f_5f53_5543_485f_5459),
    };

    assert!(matches!(
        activate_first(MFT_CATEGORY_AUDIO_ENCODER, &input, &nobody),
        Ok(None)
    ));
}

#[test]
fn a_media_foundation_failure_keeps_windows_words() {
    let error = platform(windows::Win32::Foundation::E_FAIL.into());
    assert!(matches!(error, AudioError::Platform(message) if !message.is_empty()));
}
