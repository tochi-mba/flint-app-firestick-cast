//! Loopback capture: what an output is playing, as 48 kHz 16-bit stereo.
//!
//! Windows converts from the output's own format when asked, which on the development PC is
//! 32-bit float. It delivers nothing at all while nothing plays, rather than silent buffers; the
//! session fills those gaps so the TV's clock keeps running.

use windows::Win32::Media::Audio::Endpoints::IAudioMeterInformation;
use windows::Win32::Media::Audio::{
    IAudioCaptureClient, IAudioClient, IMMDevice, AUDCLNT_BUFFERFLAGS_SILENT,
    AUDCLNT_E_DEVICE_INVALIDATED, AUDCLNT_SHAREMODE_SHARED, AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM,
    AUDCLNT_STREAMFLAGS_LOOPBACK, AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY, WAVEFORMATEX,
    WAVE_FORMAT_PCM,
};
use windows::Win32::System::Com::CLSCTX_ALL;

use super::aac::{CHANNELS, SAMPLE_RATE};
use super::follow::{Capture, Outputs};
use super::{endpoint, AudioError};

/// How much Windows buffers between reads: 200 ms, so a slow read loses nothing.
const BUFFER_100NS: i64 = 2_000_000;

/// A running loopback capture of one output.
pub(crate) struct Loopback {
    client: IAudioClient,
    capture: IAudioCaptureClient,
    meter: Option<IAudioMeterInformation>,
}

impl Loopback {
    /// Starts capturing what `device` plays.
    ///
    /// # Errors
    /// [`AudioError::Platform`] when Windows will not capture it.
    pub(crate) fn open(device: &IMMDevice) -> Result<Self, AudioError> {
        let format = WAVEFORMATEX {
            wFormatTag: WAVE_FORMAT_PCM as u16,
            nChannels: CHANNELS as u16,
            nSamplesPerSec: SAMPLE_RATE,
            nAvgBytesPerSec: SAMPLE_RATE * CHANNELS * 2,
            nBlockAlign: (CHANNELS * 2) as u16,
            wBitsPerSample: 16,
            cbSize: 0,
        };

        // SAFETY: the device is live; the client is initialised once with a complete format and
        // started only once its capture service exists.
        unsafe {
            let client: IAudioClient = device.Activate(CLSCTX_ALL, None).map_err(platform)?;
            client
                .Initialize(
                    AUDCLNT_SHAREMODE_SHARED,
                    AUDCLNT_STREAMFLAGS_LOOPBACK
                        | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM
                        | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
                    BUFFER_100NS,
                    0,
                    &raw const format,
                    None,
                )
                .map_err(platform)?;
            let capture: IAudioCaptureClient = client.GetService().map_err(platform)?;
            // Without a meter, capture still works; only the check for a mute that silences it
            // goes without.
            let meter = device.Activate(CLSCTX_ALL, None).ok();
            client.Start().map_err(platform)?;
            Ok(Self {
                client,
                capture,
                meter,
            })
        }
    }

    /// Appends everything captured since the last read to `out`, as interleaved samples.
    ///
    /// # Errors
    /// [`AudioError::DeviceLost`] when the output went away, and [`AudioError::Platform`] for
    /// anything else Windows refuses.
    pub(crate) fn read(&mut self, out: &mut Vec<i16>) -> Result<(), AudioError> {
        loop {
            // SAFETY: the capture client is live; each buffer is read and released before the next.
            unsafe {
                let waiting = self.capture.GetNextPacketSize().map_err(lost_or_platform)?;
                if waiting == 0 {
                    return Ok(());
                }

                let mut data = std::ptr::null_mut();
                let mut frames = 0u32;
                let mut flags = 0u32;
                self.capture
                    .GetBuffer(&raw mut data, &raw mut frames, &raw mut flags, None, None)
                    .map_err(lost_or_platform)?;
                let samples = frames as usize * CHANNELS as usize;
                if flags & (AUDCLNT_BUFFERFLAGS_SILENT.0 as u32) != 0 || data.is_null() {
                    out.resize(out.len() + samples, 0);
                } else {
                    out.extend_from_slice(std::slice::from_raw_parts(data.cast::<i16>(), samples));
                }
                self.capture
                    .ReleaseBuffer(frames)
                    .map_err(lost_or_platform)?;
            }
        }
    }
}

impl Capture for Loopback {
    fn read(&mut self, out: &mut Vec<i16>) -> Result<(), AudioError> {
        Self::read(self, out)
    }

    fn meter(&self) -> f32 {
        self.meter.as_ref().map_or(0.0, |meter| {
            // SAFETY: the meter is live; its peak is read once.
            unsafe { meter.GetPeakValue() }.unwrap_or(0.0)
        })
    }
}

/// This PC's own outputs.
pub(crate) struct WindowsOutputs;

impl Outputs for WindowsOutputs {
    type Capture = Loopback;

    fn open(&self, id: Option<&str>) -> Result<(Loopback, String), AudioError> {
        let device = endpoint::open(id)?;
        let capture = Loopback::open(&device)?;
        Ok((capture, endpoint::id_of(&device)?))
    }

    fn default_id(&self) -> Option<String> {
        endpoint::open(None)
            .and_then(|device| endpoint::id_of(&device))
            .ok()
    }
}

impl Drop for Loopback {
    fn drop(&mut self) {
        // SAFETY: the client is live; stopping a capture that already failed is harmless.
        let _ = unsafe { self.client.Stop() };
    }
}

fn lost_or_platform(error: windows::core::Error) -> AudioError {
    if error.code() == AUDCLNT_E_DEVICE_INVALIDATED {
        AudioError::DeviceLost
    } else {
        AudioError::Platform(error.message())
    }
}

fn platform(error: windows::core::Error) -> AudioError {
    AudioError::Platform(error.message())
}

#[cfg(test)]
#[path = "capture_tests.rs"]
mod tests;
