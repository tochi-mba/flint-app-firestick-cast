#![allow(
    clippy::undocumented_unsafe_blocks,
    reason = "test-only playback: every block is a Core Audio call with the arguments it names"
)]

use std::time::{Duration, Instant};

use windows::Win32::Foundation::E_FAIL;
use windows::Win32::Media::Audio::{IAudioRenderClient, IMMDevice};
use windows::Win32::System::Com::{CoInitializeEx, COINIT_MULTITHREADED};

use super::*;

fn com() {
    // SAFETY: joins this test thread to the multithreaded apartment, which it never leaves.
    let _ = unsafe { CoInitializeEx(None, COINIT_MULTITHREADED) };
}

/// Plays through `device` for as long as the client lives: a square wave of `amplitude`, far too
/// quiet to hear, or a buffer marked as silence.
fn play(device: &IMMDevice, amplitude: Option<i16>) -> IAudioClient {
    unsafe {
        let client: IAudioClient = device.Activate(CLSCTX_ALL, None).unwrap();
        let format = WAVEFORMATEX {
            wFormatTag: WAVE_FORMAT_PCM as u16,
            nChannels: 2,
            nSamplesPerSec: SAMPLE_RATE,
            nAvgBytesPerSec: SAMPLE_RATE * 4,
            nBlockAlign: 4,
            wBitsPerSample: 16,
            cbSize: 0,
        };
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
        for (index, sample) in samples.iter_mut().enumerate() {
            *sample = amplitude.map_or(0, |amplitude| {
                if index / 96 % 2 == 0 {
                    amplitude
                } else {
                    -amplitude
                }
            });
        }
        let flags = if amplitude.is_some() {
            0
        } else {
            AUDCLNT_BUFFERFLAGS_SILENT.0 as u32
        };
        render.ReleaseBuffer(frames, flags).unwrap();
        client.Start().unwrap();
        client
    }
}

/// Everything `capture` reads over `length`.
fn read_for(capture: &mut Loopback, length: Duration) -> Vec<i16> {
    let mut heard = Vec::new();
    let until = Instant::now() + length;
    while Instant::now() < until {
        std::thread::sleep(Duration::from_millis(10));
        Capture::read(capture, &mut heard).unwrap();
    }
    heard
}

#[test]
fn a_lost_output_is_told_apart_from_other_failures() {
    assert_eq!(
        lost_or_platform(AUDCLNT_E_DEVICE_INVALIDATED.into()),
        AudioError::DeviceLost
    );
    assert!(
        matches!(lost_or_platform(E_FAIL.into()), AudioError::Platform(message) if !message.is_empty())
    );
    assert!(
        matches!(platform(E_FAIL.into()), AudioError::Platform(message) if !message.is_empty())
    );
}

/// Runs where this PC has a sound output.
#[test]
fn the_default_output_is_the_one_followed() {
    com();
    let outputs = WindowsOutputs;
    if let Ok((_, id)) = outputs.open(None) {
        assert_eq!(outputs.default_id(), Some(id));
    }
}

/// Runs where this PC has a sound output. Nothing it plays can be heard.
#[test]
fn loopback_reads_whatever_the_output_plays() {
    com();
    if let Ok(device) = endpoint::open(None) {
        let mut capture = Loopback::open(&device).unwrap();

        let silence = play(&device, None);
        assert!(!read_for(&mut capture, Duration::from_millis(300)).is_empty());
        drop(silence);

        let quiet = play(&device, Some(4));
        assert!(!read_for(&mut capture, Duration::from_millis(300)).is_empty());
        assert!((0.0..=1.0).contains(&Capture::meter(&capture)));
        drop(quiet);
    }
}
