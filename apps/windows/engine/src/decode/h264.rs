//! A software H.264 decoder, for proving that what Flint sends is actually a picture.
//!
//! This is not part of the cast path and never runs in a session. It exists because of a class of
//! bug that is otherwise almost impossible to pin down: the host encodes, the network carries, the
//! television decodes and renders, every counter on both sides reports success, and the panel shows
//! a flat green field. Green is what a YUV surface looks like when it is handed a buffer of zeroes,
//! so the question "are we sending a picture at all?" has to be answerable on the host, without a
//! television in the loop.
//!
//! Decoding Flint's own output and looking at the result answers it in one step. If the decoded
//! frame is the desktop, the bitstream is good and the fault is on the receiver; if it is blank,
//! the fault is here.

use windows::Win32::Media::MediaFoundation::{
    IMFMediaType, IMFSample, IMFTransform, MFCreateMediaType, MFCreateMemoryBuffer, MFCreateSample,
    MFMediaType_Video, MFTEnumEx, MFVideoFormat_H264, MFVideoFormat_NV12,
    MFVideoInterlace_Progressive, MFT_CATEGORY_VIDEO_DECODER, MFT_ENUM_FLAG_SORTANDFILTER,
    MFT_ENUM_FLAG_SYNCMFT, MFT_MESSAGE_COMMAND_FLUSH, MFT_MESSAGE_NOTIFY_BEGIN_STREAMING,
    MFT_MESSAGE_NOTIFY_END_OF_STREAM, MFT_MESSAGE_NOTIFY_START_OF_STREAM, MFT_OUTPUT_DATA_BUFFER,
    MFT_REGISTER_TYPE_INFO, MF_E_TRANSFORM_NEED_MORE_INPUT, MF_E_TRANSFORM_STREAM_CHANGE,
    MF_MT_DEFAULT_STRIDE, MF_MT_FRAME_SIZE, MF_MT_INTERLACE_MODE, MF_MT_MAJOR_TYPE, MF_MT_SUBTYPE,
};

use crate::encode::h264::{pack, platform};
use crate::encode::mediafoundation::MediaFoundationPlatform;
use crate::encode::video::EncodeError;

/// One decoded picture, in NV12.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DecodedFrame {
    /// Width in luma samples.
    pub width: u32,
    /// Height in luma samples.
    pub height: u32,
    /// NV12 bytes: a full-size luma plane followed by interleaved chroma at half resolution.
    pub nv12: Vec<u8>,
}

impl DecodedFrame {
    /// The luma plane.
    #[must_use]
    pub fn luma(&self) -> &[u8] {
        &self.nv12[..(self.width * self.height) as usize]
    }

    /// The interleaved chroma plane.
    #[must_use]
    pub fn chroma(&self) -> &[u8] {
        &self.nv12[(self.width * self.height) as usize..]
    }
}

/// Media Foundation's H.264 decoder, driven synchronously.
pub struct H264Decoder {
    transform: IMFTransform,
    width: u32,
    height: u32,
    // Declared last so it outlives the transform: releasing a COM object after MFShutdown is
    // undefined behaviour.
    _platform: MediaFoundationPlatform,
}

impl H264Decoder {
    /// Opens a software H.264 decoder for a stream of the given size.
    ///
    /// # Errors
    /// [`EncodeError::Platform`] when the host has no software H.264 decoder.
    pub fn new(width: u32, height: u32) -> Result<Self, EncodeError> {
        let platform_guard = MediaFoundationPlatform::start().map_err(platform)?;

        let input_info = MFT_REGISTER_TYPE_INFO {
            guidMajorType: MFMediaType_Video,
            guidSubtype: MFVideoFormat_H264,
        };

        let mut activates = std::ptr::null_mut();
        let mut count = 0u32;
        // A synchronous decoder deliberately: this is a diagnostic, and the asynchronous event pump
        // is exactly the machinery a diagnostic should not depend on to prove something else.
        // SAFETY: both out-parameters are valid; the array is released below.
        unsafe {
            MFTEnumEx(
                MFT_CATEGORY_VIDEO_DECODER,
                MFT_ENUM_FLAG_SYNCMFT | MFT_ENUM_FLAG_SORTANDFILTER,
                Some(&raw const input_info),
                None,
                &raw mut activates,
                &raw mut count,
            )
            .map_err(platform)?;
        }

        if count == 0 || activates.is_null() {
            return Err(EncodeError::Platform("no software H.264 decoder".into()));
        }

        // SAFETY: enumeration populated `count` entries; the first is the platform's best match.
        let activate = unsafe { (*activates).clone() }
            .ok_or_else(|| EncodeError::Platform("the decoder activation was empty".into()))?;
        // SAFETY: every remaining entry is released once, then the array itself is freed.
        unsafe {
            for index in 1..count as usize {
                let _ = (*activates.add(index)).take();
            }
            windows::Win32::System::Com::CoTaskMemFree(Some(activates.cast()));
        }

        // SAFETY: the activation object is live and describes a transform.
        let transform: IMFTransform = unsafe { activate.ActivateObject() }.map_err(platform)?;

        let input = media_type(&MFVideoFormat_H264, width, height)?;
        // SAFETY: the transform is live and stream 0 exists on every decoder.
        unsafe { transform.SetInputType(0, &input, 0) }.map_err(platform)?;

        let output = media_type(&MFVideoFormat_NV12, width, height)?;
        // SAFETY: as above.
        unsafe { transform.SetOutputType(0, &output, 0) }.map_err(platform)?;

        // SAFETY: the transform is configured, which is what these messages require.
        unsafe {
            transform
                .ProcessMessage(MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, 0)
                .map_err(platform)?;
            transform
                .ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM, 0)
                .map_err(platform)?;
        }

        Ok(Self {
            transform,
            width,
            height,
            _platform: platform_guard,
        })
    }

    /// Offers one access unit, returning a picture when the decoder produces one.
    ///
    /// # Errors
    /// [`EncodeError::Platform`] when the decoder rejects the data outright.
    pub fn decode(&mut self, access_unit: &[u8]) -> Result<Option<DecodedFrame>, EncodeError> {
        let sample = self.sample_from(access_unit)?;
        // SAFETY: the transform is streaming and the sample is live for the call.
        unsafe { self.transform.ProcessInput(0, &sample, 0) }.map_err(platform)?;
        self.collect()
    }

    /// Drains whatever the decoder is still holding.
    ///
    /// # Errors
    /// See [`Self::decode`].
    pub fn drain(&mut self) -> Result<Vec<DecodedFrame>, EncodeError> {
        // SAFETY: the transform is streaming.
        unsafe {
            let _ = self
                .transform
                .ProcessMessage(MFT_MESSAGE_NOTIFY_END_OF_STREAM, 0);
        }

        let mut frames = Vec::new();
        // Bounded rather than looping until empty: a decoder that keeps answering forever is a
        // broken decoder, and a diagnostic that hangs helps nobody.
        for _ in 0..64 {
            match self.collect()? {
                Some(frame) => frames.push(frame),
                None => break,
            }
        }
        Ok(frames)
    }

    /// Pulls one decoded picture out, if one is ready.
    fn collect(&mut self) -> Result<Option<DecodedFrame>, EncodeError> {
        let mut buffers = [MFT_OUTPUT_DATA_BUFFER::default(); 1];
        let mut status = 0u32;

        // Unlike the encoders, Media Foundation's H.264 decoder does not allocate its own output
        // samples: it reports neither PROVIDES_SAMPLES nor CAN_PROVIDE_SAMPLES, and answers a null
        // `pSample` with "Null output sample in ProcessOutput call" rather than with a buffer. So
        // the caller supplies one, sized by the transform's own stream info.
        let provided = if self.provides_own_samples()? {
            None
        } else {
            let sample = self.empty_output_sample()?;
            buffers[0].pSample = std::mem::ManuallyDrop::new(Some(sample));
            Some(())
        };
        let _ = provided;

        // SAFETY: one correctly-initialised entry, and stream 0 exists on every decoder.
        match unsafe {
            self.transform
                .ProcessOutput(0, &mut buffers, &raw mut status)
        } {
            Ok(()) => {}
            Err(error) if error.code() == MF_E_TRANSFORM_NEED_MORE_INPUT => return Ok(None),
            Err(error) if error.code() == MF_E_TRANSFORM_STREAM_CHANGE => {
                // The decoder has worked out the real geometry and wants the output type agreed
                // again before it will hand anything over.
                let output = media_type(&MFVideoFormat_NV12, self.width, self.height)?;
                // SAFETY: the transform is live and stream 0 exists.
                unsafe { self.transform.SetOutputType(0, &output, 0) }.map_err(platform)?;
                return Ok(None);
            }
            Err(error) => return Err(platform(error)),
        }

        let Some(sample) = buffers[0].pSample.take() else {
            return Ok(None);
        };

        // SAFETY: the sample came from ProcessOutput and owns at least one buffer.
        let buffer = unsafe { sample.ConvertToContiguousBuffer() }.map_err(platform)?;
        let mut data: *mut u8 = std::ptr::null_mut();
        let mut length = 0u32;
        // SAFETY: both out-parameters are valid; the lock is released before returning.
        unsafe {
            buffer
                .Lock(&raw mut data, None, Some(&raw mut length))
                .map_err(platform)?;
        }
        // SAFETY: Lock reported `length` readable bytes at `data`.
        let nv12 = unsafe { std::slice::from_raw_parts(data, length as usize) }.to_vec();
        // SAFETY: balances the Lock above.
        unsafe { buffer.Unlock() }.map_err(platform)?;

        Ok(Some(DecodedFrame {
            width: self.width,
            height: self.height,
            nv12,
        }))
    }

    /// What the decoder itself says its output looks like, for diagnostics.
    ///
    /// A decoder renegotiates its output type once it has read a sequence header, and the geometry
    /// it settles on is not necessarily the one it was configured with. Reading a picture out of a
    /// buffer using the wrong geometry produces exactly the symptom this module was built to chase:
    /// mostly zeroes, which is a green field.
    ///
    /// # Errors
    /// [`EncodeError::Platform`] when the transform has no current output type.
    pub fn negotiated_output(&self) -> Result<(u32, u32, u32), EncodeError> {
        // SAFETY: stream 0 exists on every decoder this module will accept.
        let media_type = unsafe { self.transform.GetOutputCurrentType(0) }.map_err(platform)?;
        // SAFETY: the type is live; both keys are standard.
        let packed = unsafe { media_type.GetUINT64(&MF_MT_FRAME_SIZE) }.map_err(platform)?;
        // SAFETY: as above; a type with no stride reports an error, read as zero.
        let stride = unsafe { media_type.GetUINT32(&MF_MT_DEFAULT_STRIDE) }.unwrap_or(0);
        Ok(((packed >> 32) as u32, packed as u32, stride))
    }

    /// Whether the transform allocates its own output samples.
    fn provides_own_samples(&self) -> Result<bool, EncodeError> {
        // SAFETY: stream 0 exists on every decoder this module will accept.
        let info = unsafe { self.transform.GetOutputStreamInfo(0) }.map_err(platform)?;
        Ok(
            info.dwFlags & (OUTPUT_STREAM_PROVIDES_SAMPLES | OUTPUT_STREAM_CAN_PROVIDE_SAMPLES)
                != 0,
        )
    }

    /// An empty sample large enough for one decoded picture.
    fn empty_output_sample(&self) -> Result<IMFSample, EncodeError> {
        // SAFETY: stream 0 exists; the transform reports the size it needs.
        let info = unsafe { self.transform.GetOutputStreamInfo(0) }.map_err(platform)?;
        // A decoder that reports no size still needs a buffer, and NV12 is exactly one and a half
        // bytes a pixel, so the correct figure is computable rather than a guess.
        let needed = info.cbSize.max(self.width * self.height * 3 / 2);

        // SAFETY: a positive length; the buffer is owned by the sample below.
        let buffer = unsafe { MFCreateMemoryBuffer(needed) }.map_err(platform)?;
        // SAFETY: the out-parameter is valid.
        let sample = unsafe { MFCreateSample() }.map_err(platform)?;
        // SAFETY: both objects are live.
        unsafe { sample.AddBuffer(&buffer) }.map_err(platform)?;
        Ok(sample)
    }

    /// Wraps an access unit in a Media Foundation sample.
    fn sample_from(&self, access_unit: &[u8]) -> Result<IMFSample, EncodeError> {
        let length = u32::try_from(access_unit.len())
            .map_err(|_| EncodeError::Platform("access unit too large for a buffer".into()))?;
        // SAFETY: a positive length; the buffer is owned by the sample below.
        let buffer = unsafe { MFCreateMemoryBuffer(length) }.map_err(platform)?;

        let mut destination: *mut u8 = std::ptr::null_mut();
        // SAFETY: the buffer was just created with room for `length` bytes.
        unsafe {
            buffer
                .Lock(&raw mut destination, None, None)
                .map_err(platform)?;
            std::ptr::copy_nonoverlapping(access_unit.as_ptr(), destination, access_unit.len());
            buffer.SetCurrentLength(length).map_err(platform)?;
            buffer.Unlock().map_err(platform)?;
        }

        // SAFETY: the out-parameter is valid.
        let sample = unsafe { MFCreateSample() }.map_err(platform)?;
        // SAFETY: both objects are live.
        unsafe { sample.AddBuffer(&buffer) }.map_err(platform)?;
        Ok(sample)
    }
}

/// `MFT_OUTPUT_STREAM_PROVIDES_SAMPLES`: the transform always allocates its own output samples.
const OUTPUT_STREAM_PROVIDES_SAMPLES: u32 = 0x100;
/// `MFT_OUTPUT_STREAM_CAN_PROVIDE_SAMPLES`: the transform will allocate if the caller does not.
const OUTPUT_STREAM_CAN_PROVIDE_SAMPLES: u32 = 0x200;

/// Builds a video media type of a given subtype and size.
fn media_type(
    subtype: &windows::core::GUID,
    width: u32,
    height: u32,
) -> Result<IMFMediaType, EncodeError> {
    // SAFETY: the out-parameter is valid.
    let media_type = unsafe { MFCreateMediaType() }.map_err(platform)?;
    // SAFETY: the type is live and each key takes the kind of value given.
    unsafe {
        media_type
            .SetGUID(&MF_MT_MAJOR_TYPE, &MFMediaType_Video)
            .map_err(platform)?;
        media_type
            .SetGUID(&MF_MT_SUBTYPE, subtype)
            .map_err(platform)?;
        media_type
            .SetUINT64(&MF_MT_FRAME_SIZE, pack(width, height))
            .map_err(platform)?;
        media_type
            .SetUINT32(&MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive.0 as u32)
            .map_err(platform)?;
    }
    Ok(media_type)
}

impl Drop for H264Decoder {
    fn drop(&mut self) {
        // SAFETY: the transform is live; a failure here has nowhere useful to go.
        unsafe {
            let _ = self.transform.ProcessMessage(MFT_MESSAGE_COMMAND_FLUSH, 0);
        }
    }
}
