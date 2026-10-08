//! Questions about this PC's sound, answered by running them rather than by reading documentation.
//!
//! Ignored by default: they play a short, quiet tone through the default output and briefly mute
//! it, which nobody running the ordinary tests asked for. Run them deliberately with
//! `cargo test --lib audio::spike_tests -- --ignored --nocapture`.

#![allow(
    clippy::undocumented_unsafe_blocks,
    reason = "a report, not product code: every block is a Core Audio call with the arguments it names"
)]

use std::time::{Duration, Instant};

use windows::Win32::Media::Audio::Endpoints::{IAudioEndpointVolume, IAudioMeterInformation};
use windows::Win32::Media::Audio::{
    eConsole, eRender, IAudioCaptureClient, IAudioClient, IAudioRenderClient, IMMDevice,
    IMMDeviceEnumerator, MMDeviceEnumerator, AUDCLNT_BUFFERFLAGS_SILENT, AUDCLNT_SHAREMODE_SHARED,
    AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM, AUDCLNT_STREAMFLAGS_LOOPBACK,
    AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY, WAVEFORMATEX, WAVE_FORMAT_PCM,
};
use windows::Win32::System::Com::{
    CoCreateInstance, CoInitializeEx, CLSCTX_ALL, COINIT_MULTITHREADED,
};

const RATE: u32 = 48_000;

fn stereo_16_bit() -> WAVEFORMATEX {
    WAVEFORMATEX {
        wFormatTag: WAVE_FORMAT_PCM as u16,
        nChannels: 2,
        nSamplesPerSec: RATE,
        nAvgBytesPerSec: RATE * 4,
        nBlockAlign: 4,
        wBitsPerSample: 16,
        cbSize: 0,
    }
}

fn default_output() -> IMMDevice {
    unsafe {
        let _ = CoInitializeEx(None, COINIT_MULTITHREADED);
        let devices: IMMDeviceEnumerator =
            CoCreateInstance(&MMDeviceEnumerator, None, CLSCTX_ALL).unwrap();
        devices.GetDefaultAudioEndpoint(eRender, eConsole).unwrap()
    }
}

/// Opens a loopback capture of `device`, converting to 48 kHz 16-bit stereo, or says why not.
fn loopback(device: &IMMDevice) -> windows::core::Result<(IAudioClient, IAudioCaptureClient)> {
    unsafe {
        let client: IAudioClient = device.Activate(CLSCTX_ALL, None)?;
        let format = stereo_16_bit();
        client.Initialize(
            AUDCLNT_SHAREMODE_SHARED,
            AUDCLNT_STREAMFLAGS_LOOPBACK
                | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM
                | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
            2_000_000,
            0,
            &raw const format,
            None,
        )?;
        let capture: IAudioCaptureClient = client.GetService()?;
        Ok((client, capture))
    }
}

/// Plays a 1 kHz tone at about -30 dBFS for half a second on `device`.
fn play_tone(device: &IMMDevice) -> IAudioClient {
    unsafe {
        let client: IAudioClient = device.Activate(CLSCTX_ALL, None).unwrap();
        let format = stereo_16_bit();
        client
            .Initialize(
                AUDCLNT_SHAREMODE_SHARED,
                AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
                5_000_000,
                0,
                &raw const format,
                None,
            )
            .unwrap();
        let render: IAudioRenderClient = client.GetService().unwrap();
        let frames = client.GetBufferSize().unwrap();
        let buffer = render.GetBuffer(frames).unwrap().cast::<i16>();
        let samples = std::slice::from_raw_parts_mut(buffer, frames as usize * 2);
        for (frame, pair) in samples.chunks_exact_mut(2).enumerate() {
            let phase = frame as f64 * 1_000.0 * std::f64::consts::TAU / f64::from(RATE);
            let value = (phase.sin() * 1_000.0) as i16;
            pair[0] = value;
            pair[1] = value;
        }
        render.ReleaseBuffer(frames, 0).unwrap();
        client.Start().unwrap();
        client
    }
}

/// Captures for `length` and returns the RMS level of what was heard, and how many frames.
fn listen(capture: &IAudioCaptureClient, length: Duration) -> (f64, usize) {
    let mut squares = 0f64;
    let mut count = 0usize;
    let until = Instant::now() + length;
    while Instant::now() < until {
        unsafe {
            while capture.GetNextPacketSize().unwrap() > 0 {
                let mut data = std::ptr::null_mut();
                let mut frames = 0u32;
                let mut flags = 0u32;
                capture
                    .GetBuffer(&raw mut data, &raw mut frames, &raw mut flags, None, None)
                    .unwrap();
                if flags & (AUDCLNT_BUFFERFLAGS_SILENT.0 as u32) == 0 && frames > 0 {
                    let samples =
                        std::slice::from_raw_parts(data.cast::<i16>(), frames as usize * 2);
                    for &sample in samples {
                        squares += f64::from(sample) * f64::from(sample);
                    }
                }
                count += frames as usize;
                capture.ReleaseBuffer(frames).unwrap();
            }
        }
        std::thread::sleep(Duration::from_millis(10));
    }
    let rms = if count == 0 {
        0.0
    } else {
        (squares / (count * 2) as f64).sqrt()
    };
    (rms, count)
}

#[test]
#[ignore = "plays a quiet tone and briefly mutes this PC; run deliberately"]
fn report_what_loopback_capture_hears() {
    let device = default_output();
    if let Some(output) = crate::audio::endpoint::list()
        .unwrap_or_default()
        .into_iter()
        .find(|output| output.is_default)
    {
        println!("default output: {}", output.name);
    }
    unsafe {
        let probe: IAudioClient = device.Activate(CLSCTX_ALL, None).unwrap();
        // Copied out: the structure is packed, so its fields cannot be borrowed in place.
        let mix = std::ptr::read_unaligned(probe.GetMixFormat().unwrap());
        let (tag, channels, rate, bits) = (
            mix.wFormatTag,
            mix.nChannels,
            mix.nSamplesPerSec,
            mix.wBitsPerSample,
        );
        println!("mix format: tag {tag:#x}, {channels} channels, {rate} Hz, {bits} bits");
    }

    // Question 1: does Windows convert loopback capture to 48 kHz 16-bit stereo when asked?
    let (client, capture) = match loopback(&device) {
        Ok(opened) => {
            println!("Q1 loopback as 48 kHz 16-bit stereo: accepted");
            opened
        }
        Err(error) => {
            println!("Q1 loopback as 48 kHz 16-bit stereo: refused ({error})");
            return;
        }
    };
    unsafe { client.Start().unwrap() };

    let (quiet, quiet_frames) = listen(&capture, Duration::from_millis(300));
    println!("nothing playing: rms {quiet:.1} over {quiet_frames} frames");

    let tone = play_tone(&device);
    let (heard, heard_frames) = listen(&capture, Duration::from_millis(400));
    let meter_heard = peak(&device);
    unsafe { tone.Stop().unwrap() };
    println!("meter with the tone playing: {meter_heard:.4}");
    println!("tone playing: rms {heard:.1} over {heard_frames} frames (the tone itself is 707; more means the output adds gain)");

    // Question 2: does capture still hear sound while the output is muted?
    let volume: IAudioEndpointVolume = unsafe { device.Activate(CLSCTX_ALL, None).unwrap() };
    let was_muted = unsafe { volume.GetMute().unwrap().as_bool() };
    unsafe { volume.SetMute(true, std::ptr::null()).unwrap() };
    let tone = play_tone(&device);
    let (muted, muted_frames) = listen(&capture, Duration::from_millis(400));
    let meter_muted = peak(&device);
    println!("meter with the tone playing while muted: {meter_muted:.4}");
    unsafe {
        tone.Stop().unwrap();
        volume.SetMute(was_muted, std::ptr::null()).unwrap();
        client.Stop().unwrap();
    }
    println!("tone playing while muted: rms {muted:.1} over {muted_frames} frames");
    println!(
        "Q2 capture hears sound while the output is muted: {}",
        if muted > heard * 0.5 { "yes" } else { "no" }
    );
    println!("mute restored to {was_muted}");
}

#[test]
#[ignore = "a report; run deliberately"]
fn report_the_aac_encoder() {
    match crate::audio::aac::AacEncoder::new(128) {
        Ok(encoder) => println!(
            "AAC encoder: present, setup {:02x?}",
            encoder.audio_specific_config()
        ),
        Err(error) => println!("AAC encoder: {error}"),
    }
}

/// What Windows' own meter shows for `device`.
fn peak(device: &IMMDevice) -> f32 {
    unsafe {
        let meter: IAudioMeterInformation = device.Activate(CLSCTX_ALL, None).unwrap();
        meter.GetPeakValue().unwrap()
    }
}
