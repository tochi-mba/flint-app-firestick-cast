//! Windows' own AAC encoder: 48 kHz stereo PCM in, raw AAC-LC frames out.
//!
//! The TV's decoder is configured with the two-byte `AudioSpecificConfig` this encoder reports, the
//! same setup the phone app sends, and is then fed one raw frame per packet. Every frame holds
//! 1024 samples per channel, so a frame's presentation time follows from how many came before.

use windows::core::GUID;
use windows::Win32::Media::MediaFoundation::{
    IMFActivate, IMFMediaType, IMFSample, IMFTransform, MFAudioFormat_AAC, MFAudioFormat_PCM,
    MFCreateMediaType, MFCreateMemoryBuffer, MFCreateSample, MFMediaType_Audio, MFTEnumEx,
    MFT_CATEGORY_AUDIO_ENCODER, MFT_ENUM_FLAG, MFT_ENUM_FLAG_SORTANDFILTER, MFT_ENUM_FLAG_SYNCMFT,
    MFT_OUTPUT_DATA_BUFFER, MFT_OUTPUT_STREAM_INFO, MFT_OUTPUT_STREAM_PROVIDES_SAMPLES,
    MFT_REGISTER_TYPE_INFO, MF_E_TRANSFORM_NEED_MORE_INPUT,
    MF_MT_AAC_AUDIO_PROFILE_LEVEL_INDICATION, MF_MT_AAC_PAYLOAD_TYPE,
    MF_MT_AUDIO_AVG_BYTES_PER_SECOND, MF_MT_AUDIO_BITS_PER_SAMPLE, MF_MT_AUDIO_BLOCK_ALIGNMENT,
    MF_MT_AUDIO_NUM_CHANNELS, MF_MT_AUDIO_SAMPLES_PER_SECOND, MF_MT_MAJOR_TYPE, MF_MT_SUBTYPE,
    MF_MT_USER_DATA,
};

use super::AudioError;
use crate::encode::mediafoundation::MediaFoundationPlatform;

/// The sample rate everything is captured, encoded and sent at.
pub const SAMPLE_RATE: u32 = 48_000;

/// Channels: stereo, whatever the PC's own speakers are.
pub const CHANNELS: u32 = 2;

/// Samples per channel in one AAC frame.
pub const SAMPLES_PER_FRAME: u32 = 1024;

/// The data rates Windows' AAC encoder offers, in kilobits a second.
pub const BITRATES_KBPS: [u32; 4] = [96, 128, 160, 192];

/// The bytes of `HEAACWAVEINFO` that come before the `AudioSpecificConfig` in the media type's user
/// data: payload type, profile, structure type and two reserved fields.
const WAVE_INFO_BYTES: usize = 12;

/// One encoded AAC frame.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AudioPacket {
    /// The raw frame, without an ADTS header.
    pub data: Vec<u8>,
    /// When the first sample in the frame is heard, in microseconds from the start of the stream.
    pub presentation_time_us: i64,
}

/// Windows' AAC encoder, set up for low-complexity stereo at 48 kHz.
pub struct AacEncoder {
    transform: IMFTransform,
    config: Vec<u8>,
    provides_samples: bool,
    output_size: u32,
    samples_in: u64,
    frames_out: u64,
    _platform: MediaFoundationPlatform,
}

impl AacEncoder {
    /// Opens the encoder at `bitrate_kbps`, one of [`BITRATES_KBPS`].
    ///
    /// # Errors
    /// [`AudioError::NoEncoder`] when Windows has none, [`AudioError::UnsupportedBitrate`] for a rate
    /// it does not offer, and [`AudioError::Platform`] when it refuses the setup.
    pub fn new(bitrate_kbps: u32) -> Result<Self, AudioError> {
        if !BITRATES_KBPS.contains(&bitrate_kbps) {
            return Err(AudioError::UnsupportedBitrate(bitrate_kbps));
        }

        let media_foundation = MediaFoundationPlatform::start().map_err(platform)?;
        let transform = find_encoder()?;

        // SAFETY: the transform is live and both media types are fully described before use.
        unsafe {
            transform
                .SetInputType(0, &pcm_type().map_err(platform)?, 0)
                .map_err(platform)?;
            transform
                .SetOutputType(0, &aac_type(bitrate_kbps).map_err(platform)?, 0)
                .map_err(platform)?;
        }

        let config = read_config(&transform)?;
        // SAFETY: the transform is live and has a stream 0 once its types are set.
        let info: MFT_OUTPUT_STREAM_INFO =
            unsafe { transform.GetOutputStreamInfo(0) }.map_err(platform)?;

        Ok(Self {
            transform,
            config,
            provides_samples: info.dwFlags & MFT_OUTPUT_STREAM_PROVIDES_SAMPLES.0 as u32 != 0,
            output_size: info.cbSize.max(1024 * 2),
            samples_in: 0,
            frames_out: 0,
            _platform: media_foundation,
        })
    }

    /// The two-byte `AudioSpecificConfig` the TV's decoder needs before the first frame.
    #[must_use]
    pub fn audio_specific_config(&self) -> &[u8] {
        &self.config
    }

    /// Hands the encoder interleaved 16-bit stereo samples and collects every finished frame.
    ///
    /// # Errors
    /// [`AudioError::Platform`] when Windows refuses the samples or the frames.
    pub fn encode(&mut self, pcm: &[i16], out: &mut Vec<AudioPacket>) -> Result<(), AudioError> {
        if !pcm.is_empty() {
            let sample = self.input_sample(pcm)?;
            // SAFETY: the sample was just built from `pcm` and the transform is configured.
            unsafe { self.transform.ProcessInput(0, &sample, 0) }.map_err(platform)?;
            self.samples_in += (pcm.len() / CHANNELS as usize) as u64;
        }

        while let Some(data) = self.drain()? {
            let presentation_time_us = i64::try_from(
                self.frames_out * u64::from(SAMPLES_PER_FRAME) * 1_000_000 / u64::from(SAMPLE_RATE),
            )
            .unwrap_or(i64::MAX);
            self.frames_out += 1;
            out.push(AudioPacket {
                data,
                presentation_time_us,
            });
        }

        Ok(())
    }

    fn input_sample(&self, pcm: &[i16]) -> Result<IMFSample, AudioError> {
        let bytes = std::mem::size_of_val(pcm);
        let length = u32::try_from(bytes)
            .map_err(|_| AudioError::Platform("too many samples at once".into()))?;
        // SAFETY: a buffer of `length` bytes is created, locked, filled with exactly that many
        // bytes from `pcm`, and unlocked before it is attached to a new sample.
        unsafe {
            let buffer = MFCreateMemoryBuffer(length).map_err(platform)?;
            let mut data = std::ptr::null_mut();
            buffer.Lock(&raw mut data, None, None).map_err(platform)?;
            std::ptr::copy_nonoverlapping(pcm.as_ptr().cast::<u8>(), data, bytes);
            buffer.Unlock().map_err(platform)?;
            buffer.SetCurrentLength(length).map_err(platform)?;

            let sample = MFCreateSample().map_err(platform)?;
            sample.AddBuffer(&buffer).map_err(platform)?;
            let time_100ns = i64::try_from(self.samples_in * 10_000_000 / u64::from(SAMPLE_RATE))
                .unwrap_or(i64::MAX);
            sample.SetSampleTime(time_100ns).map_err(platform)?;
            let duration =
                (pcm.len() / CHANNELS as usize) as i64 * 10_000_000 / i64::from(SAMPLE_RATE);
            sample.SetSampleDuration(duration).map_err(platform)?;
            Ok(sample)
        }
    }

    /// Takes one finished frame, if the encoder has one.
    fn drain(&mut self) -> Result<Option<Vec<u8>>, AudioError> {
        let mut buffers = [MFT_OUTPUT_DATA_BUFFER {
            pSample: std::mem::ManuallyDrop::new(self.output_sample()?),
            ..Default::default()
        }];
        let mut status = 0u32;
        // SAFETY: one correctly initialised output entry, as the transform's stream info asked.
        let produced = unsafe {
            self.transform
                .ProcessOutput(0, &mut buffers, &raw mut status)
        };
        // Taken back whatever happened, so the sample is released rather than leaked.
        let sample = buffers[0].pSample.take();
        match produced {
            Err(error) if error.code() == MF_E_TRANSFORM_NEED_MORE_INPUT => Ok(None),
            produced => {
                produced.map_err(platform)?;
                sample.map(|sample| read_bytes(&sample)).transpose()
            }
        }
    }

    /// The sample the encoder writes into, or none when it brings its own.
    fn output_sample(&self) -> Result<Option<IMFSample>, AudioError> {
        (!self.provides_samples)
            .then(|| {
                // SAFETY: a fresh sample with one buffer the size the encoder asked for.
                unsafe {
                    let buffer = MFCreateMemoryBuffer(self.output_size).map_err(platform)?;
                    let sample = MFCreateSample().map_err(platform)?;
                    sample.AddBuffer(&buffer).map_err(platform)?;
                    Ok(sample)
                }
            })
            .transpose()
    }
}

/// Reads every byte of a sample into one vector.
fn read_bytes(sample: &IMFSample) -> Result<Vec<u8>, AudioError> {
    // SAFETY: the sample is live; its contiguous buffer is locked only while it is copied.
    unsafe {
        let buffer = sample.ConvertToContiguousBuffer().map_err(platform)?;
        let mut data = std::ptr::null_mut();
        let mut length = 0u32;
        buffer
            .Lock(&raw mut data, None, Some(&raw mut length))
            .map_err(platform)?;
        let bytes = std::slice::from_raw_parts(data, length as usize).to_vec();
        buffer.Unlock().map_err(platform)?;
        Ok(bytes)
    }
}

/// 48 kHz 16-bit stereo PCM.
fn pcm_type() -> windows::core::Result<IMFMediaType> {
    // SAFETY: a new media type, described attribute by attribute.
    unsafe {
        let media = MFCreateMediaType()?;
        media.SetGUID(&MF_MT_MAJOR_TYPE, &MFMediaType_Audio)?;
        media.SetGUID(&MF_MT_SUBTYPE, &MFAudioFormat_PCM)?;
        media.SetUINT32(&MF_MT_AUDIO_BITS_PER_SAMPLE, 16)?;
        media.SetUINT32(&MF_MT_AUDIO_SAMPLES_PER_SECOND, SAMPLE_RATE)?;
        media.SetUINT32(&MF_MT_AUDIO_NUM_CHANNELS, CHANNELS)?;
        media.SetUINT32(&MF_MT_AUDIO_BLOCK_ALIGNMENT, CHANNELS * 2)?;
        media.SetUINT32(
            &MF_MT_AUDIO_AVG_BYTES_PER_SECOND,
            SAMPLE_RATE * CHANNELS * 2,
        )?;
        Ok(media)
    }
}

/// Raw AAC-LC at `bitrate_kbps`, 48 kHz stereo.
fn aac_type(bitrate_kbps: u32) -> windows::core::Result<IMFMediaType> {
    // SAFETY: a new media type, described attribute by attribute.
    unsafe {
        let media = MFCreateMediaType()?;
        media.SetGUID(&MF_MT_MAJOR_TYPE, &MFMediaType_Audio)?;
        media.SetGUID(&MF_MT_SUBTYPE, &MFAudioFormat_AAC)?;
        media.SetUINT32(&MF_MT_AUDIO_BITS_PER_SAMPLE, 16)?;
        media.SetUINT32(&MF_MT_AUDIO_SAMPLES_PER_SECOND, SAMPLE_RATE)?;
        media.SetUINT32(&MF_MT_AUDIO_NUM_CHANNELS, CHANNELS)?;
        media.SetUINT32(&MF_MT_AUDIO_AVG_BYTES_PER_SECOND, bitrate_kbps * 1000 / 8)?;
        // Raw frames, no ADTS header: the TV's decoder is told the format once, in the setup data.
        media.SetUINT32(&MF_MT_AAC_PAYLOAD_TYPE, 0)?;
        // AAC profile, level 2: low complexity, stereo at 48 kHz.
        media.SetUINT32(&MF_MT_AAC_AUDIO_PROFILE_LEVEL_INDICATION, 0x29)?;
        Ok(media)
    }
}

/// The `AudioSpecificConfig`, which Windows keeps at the end of the output type's user data.
fn read_config(transform: &IMFTransform) -> Result<Vec<u8>, AudioError> {
    // SAFETY: the output type is set; its user data blob is read into a buffer of its own size.
    let blob = unsafe {
        let media = transform.GetOutputCurrentType(0).map_err(platform)?;
        let size = media.GetBlobSize(&MF_MT_USER_DATA).map_err(platform)?;
        let mut blob = vec![0u8; size as usize];
        media
            .GetBlob(&MF_MT_USER_DATA, &mut blob, None)
            .map_err(platform)?;
        blob
    };
    audio_specific_config(&blob)
}

/// Takes the `AudioSpecificConfig` from the end of `HEAACWAVEINFO` user data.
pub(crate) fn audio_specific_config(user_data: &[u8]) -> Result<Vec<u8>, AudioError> {
    match user_data.get(WAVE_INFO_BYTES..) {
        Some(config) if config.len() >= 2 => Ok(config.to_vec()),
        _ => Err(AudioError::Platform(format!(
            "the AAC encoder described its output in {} bytes, too few to hold its setup",
            user_data.len()
        ))),
    }
}

/// Finds a synchronous AAC encoder.
fn find_encoder() -> Result<IMFTransform, AudioError> {
    let input = MFT_REGISTER_TYPE_INFO {
        guidMajorType: MFMediaType_Audio,
        guidSubtype: MFAudioFormat_PCM,
    };
    let output = MFT_REGISTER_TYPE_INFO {
        guidMajorType: MFMediaType_Audio,
        guidSubtype: MFAudioFormat_AAC,
    };
    let transform = activate_first(MFT_CATEGORY_AUDIO_ENCODER, &input, &output)?;
    transform.ok_or(AudioError::NoEncoder)
}

/// Activates the best transform in `category` from `input` to `output`, or `None` when there is none.
pub(crate) fn activate_first(
    category: GUID,
    input: &MFT_REGISTER_TYPE_INFO,
    output: &MFT_REGISTER_TYPE_INFO,
) -> Result<Option<IMFTransform>, AudioError> {
    let mut activates = std::ptr::null_mut();
    let mut count = 0u32;
    // SAFETY: both out-parameters are valid and the returned array is released below.
    unsafe {
        MFTEnumEx(
            category,
            MFT_ENUM_FLAG(MFT_ENUM_FLAG_SYNCMFT.0 | MFT_ENUM_FLAG_SORTANDFILTER.0),
            Some(std::ptr::from_ref(input)),
            Some(std::ptr::from_ref(output)),
            &raw mut activates,
            &raw mut count,
        )
        .map_err(platform)?;
    }

    // SAFETY: MFTEnumEx returned `count` activation objects ranked best first, or none at all;
    // each is taken once and the array itself is freed, as it documents.
    let found: Vec<IMFActivate> = unsafe {
        let count = if activates.is_null() {
            0
        } else {
            count as usize
        };
        let found = (0..count)
            .filter_map(|index| (*activates.add(index)).take())
            .collect();
        windows::Win32::System::Com::CoTaskMemFree(Some(activates.cast()));
        found
    };
    found
        .first()
        .map(|activate| {
            // SAFETY: the activation object is live and returns the transform it describes.
            unsafe { activate.ActivateObject::<IMFTransform>() }.map_err(platform)
        })
        .transpose()
}

fn platform(error: windows::core::Error) -> AudioError {
    AudioError::Platform(error.message())
}

#[cfg(test)]
#[path = "aac_tests.rs"]
mod tests;
