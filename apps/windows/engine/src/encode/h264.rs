//! A real H.264 encoder, built on a Media Foundation transform.
//!
//! Media Foundation exposes encoders as MFTs, and they come in two flavours. Hardware MFTs are
//! asynchronous: they only accept input when they raise `METransformNeedInput`, and drive an event
//! loop the caller has to service. Software MFTs are synchronous — feed a sample, drain a sample —
//! and that is what this uses, because a mirror that works is worth more than one that is fast and
//! doesn't exist. The trait boundary in [`super::video`] is what keeps that a swap rather than a
//! rewrite: the session never learns which kind it got.
//!
//! Two ordering rules here are not stylistic and will silently produce a dead encoder if broken:
//! the output type must be set before the input type, and the encoder only reveals its sequence
//! header (the SPS and PPS a decoder cannot start without) after the output type is committed.

use windows::core::{Interface, VARIANT};
use windows::Win32::Media::MediaFoundation::{
    CODECAPI_AVEncCommonMeanBitRate, CODECAPI_AVEncCommonRateControlMode,
    CODECAPI_AVEncCommonRealTime, CODECAPI_AVEncMPVDefaultBPictureCount, CODECAPI_AVEncMPVGOPSize,
    CODECAPI_AVEncVideoForceKeyFrame, CODECAPI_AVLowLatencyMode, ICodecAPI, IMFMediaType,
    IMFSample, IMFTransform, MFCreateMediaType, MFCreateMemoryBuffer, MFCreateSample,
    MFMediaType_Video, MFTEnumEx, MFVideoFormat_H264, MFVideoFormat_NV12,
    MFVideoInterlace_Progressive, MFT_CATEGORY_VIDEO_ENCODER, MFT_ENUM_FLAG,
    MFT_ENUM_FLAG_SORTANDFILTER, MFT_ENUM_FLAG_SYNCMFT, MFT_MESSAGE_COMMAND_FLUSH,
    MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, MFT_MESSAGE_NOTIFY_END_OF_STREAM,
    MFT_MESSAGE_NOTIFY_END_STREAMING, MFT_MESSAGE_NOTIFY_START_OF_STREAM, MFT_OUTPUT_DATA_BUFFER,
    MFT_REGISTER_TYPE_INFO, MF_E_TRANSFORM_NEED_MORE_INPUT, MF_MT_AVG_BITRATE,
    MF_MT_DEFAULT_STRIDE, MF_MT_FRAME_RATE, MF_MT_FRAME_SIZE, MF_MT_INTERLACE_MODE,
    MF_MT_MAJOR_TYPE, MF_MT_MPEG2_PROFILE, MF_MT_MPEG_SEQUENCE_HEADER, MF_MT_PIXEL_ASPECT_RATIO,
    MF_MT_SUBTYPE,
};

use super::mediafoundation::MediaFoundationPlatform;
use super::video::{
    EncodeError, EncodedFrame, EncoderConfig, FrameData, SourceFrame, VideoEncoder,
};
use super::VideoCodec;
use crate::convert::nv12::{bgra_to_nv12, nv12_len};

/// H.264 Main profile, as `eAVEncH264VProfile_Main` defines it.
///
/// Main rather than High: every Fire TV decoder handles it, and the extra tools High adds buy
/// little on desktop content while narrowing the set of receivers that can play the stream.
const H264_PROFILE_MAIN: u32 = 77;

/// Largest GOP size the codec API can represent: effectively unbounded for a live session.
///
/// IDRs are requested from receiver feedback. Scheduling them by frame count wastes bandwidth on
/// a healthy link and conflicts with the mirror's explicit recovery policy.
const UNBOUNDED_GOP_SIZE: u32 = u32::MAX;

/// A hundred nanoseconds, the unit Media Foundation measures time in.
const HNS_PER_SECOND: i64 = 10_000_000;

/// An H.264 encoder backed by a Media Foundation transform.
pub struct H264Encoder {
    transform: IMFTransform,
    config: EncoderConfig,
    codec_specific_data: Vec<Vec<u8>>,
    nv12: Vec<u8>,
    streaming: bool,
    // Declared last on purpose. Rust drops fields in declaration order, so the transform is
    // released while Media Foundation is still running — releasing a transform after MFShutdown is
    // undefined behaviour, and the crash it produces lands nowhere near the cause.
    _platform: MediaFoundationPlatform,
}

impl std::fmt::Debug for H264Encoder {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        formatter
            .debug_struct("H264Encoder")
            .field("config", &self.config)
            .field(
                "codec_specific_data_blocks",
                &self.codec_specific_data.len(),
            )
            .field("streaming", &self.streaming)
            .finish_non_exhaustive()
    }
}

impl H264Encoder {
    /// Creates and configures an encoder for `config`.
    ///
    /// # Errors
    /// [`EncodeError::InvalidConfig`] when the configuration could not produce a stream, and
    /// [`EncodeError::Platform`] when Windows has no usable H.264 encoder or refuses the settings.
    pub fn new(config: EncoderConfig) -> Result<Self, EncodeError> {
        if !config.is_valid() {
            return Err(EncodeError::InvalidConfig);
        }
        if config.codec != VideoCodec::H264 {
            return Err(EncodeError::Platform(format!(
                "this encoder only produces H.264, not {:?}",
                config.codec
            )));
        }

        // Started before the transform is created and held for the encoder's whole life: Media
        // Foundation refuses to hand out transforms before startup, and tears them down at
        // shutdown. Startup is reference counted, so a session that also probes is unaffected.
        let platform = MediaFoundationPlatform::start().map_err(platform)?;
        let transform = find_encoder()?;
        Self::from_transform(config, transform, platform)
    }

    /// Configures an already-created transform.
    ///
    /// Split out from [`Self::new`] so a diagnostic can drive a specific encoder rather than only
    /// whichever one Windows ranks first — the difference that matters when one of the encoders on
    /// a machine produces a flat picture and another does not.
    fn from_transform(
        config: EncoderConfig,
        transform: IMFTransform,
        platform: MediaFoundationPlatform,
    ) -> Result<Self, EncodeError> {
        let mut encoder = Self {
            transform,
            config,
            codec_specific_data: Vec::new(),
            nv12: Vec::new(),
            streaming: false,
            _platform: platform,
        };
        encoder.configure()?;
        Ok(encoder)
    }

    /// The configuration this encoder was built with.
    #[must_use]
    pub fn config(&self) -> EncoderConfig {
        self.config
    }

    /// Applies the output type, then the input type, then reads the sequence header.
    fn configure(&mut self) -> Result<(), EncodeError> {
        // Before the media types, not after. The encoder reports AVLowLatencyMode as supported but
        // no longer modifiable once a type is committed, so setting it later is accepted with an
        // OK that changes nothing — measured on this project as a first access unit arriving only
        // after eighteen frames, six hundred milliseconds of latency that no amount of network
        // tuning downstream could recover.
        self.apply_low_latency_settings();

        let output = self.output_media_type()?;
        // Output before input: an encoder cannot describe the pixels it accepts until it knows
        // what it is producing, and setting them the other way round fails with a type error that
        // reads like the input format is unsupported.
        // SAFETY: the transform lives as long as this encoder, and the type outlives the call.
        unsafe { self.transform.SetOutputType(0, &output, 0) }.map_err(platform)?;

        let input = self.input_media_type()?;
        // SAFETY: as for the output type.
        unsafe { self.transform.SetInputType(0, &input, 0) }.map_err(platform)?;

        // After the types, not before: see the note on the method.
        self.apply_rate_control_settings();

        // The receiver configures MediaCodec before the first frame is sent and the current ABI
        // deliberately has no mid-stream codec-data refresh. Refuse an encoder that cannot publish
        // a complete SPS/PPS pair now instead of starting a stream that can only render black.
        self.codec_specific_data = read_sequence_header(&self.transform)?;

        // SAFETY: both media types are committed, which is what these notifications require.
        unsafe {
            self.transform
                .ProcessMessage(MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, 0)
                .map_err(platform)?;
            self.transform
                .ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM, 0)
                .map_err(platform)?;
        }
        self.streaming = true;
        Ok(())
    }

    fn output_media_type(&self) -> Result<IMFMediaType, EncodeError> {
        // SAFETY: takes no arguments, and returns an owned interface or an error.
        let media_type = unsafe { MFCreateMediaType() }.map_err(platform)?;
        // SAFETY: the type was just created, and each key below matches its documented value type.
        unsafe {
            media_type
                .SetGUID(&MF_MT_MAJOR_TYPE, &MFMediaType_Video)
                .map_err(platform)?;
            media_type
                .SetGUID(&MF_MT_SUBTYPE, &MFVideoFormat_H264)
                .map_err(platform)?;
            media_type
                .SetUINT32(&MF_MT_AVG_BITRATE, self.config.bitrate_bits_per_second)
                .map_err(platform)?;
            media_type
                .SetUINT32(&MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive.0 as u32)
                .map_err(platform)?;
            media_type
                .SetUINT32(&MF_MT_MPEG2_PROFILE, H264_PROFILE_MAIN)
                .map_err(platform)?;
            media_type
                .SetUINT64(
                    &MF_MT_FRAME_SIZE,
                    pack(self.config.width, self.config.height),
                )
                .map_err(platform)?;
            media_type
                .SetUINT64(&MF_MT_FRAME_RATE, pack(self.config.frame_rate, 1))
                .map_err(platform)?;
            media_type
                .SetUINT64(&MF_MT_PIXEL_ASPECT_RATIO, pack(1, 1))
                .map_err(platform)?;
        }
        Ok(media_type)
    }

    fn input_media_type(&self) -> Result<IMFMediaType, EncodeError> {
        // SAFETY: takes no arguments, and returns an owned interface or an error.
        let media_type = unsafe { MFCreateMediaType() }.map_err(platform)?;
        // SAFETY: the type was just created, and each key below matches its documented value type.
        unsafe {
            media_type
                .SetGUID(&MF_MT_MAJOR_TYPE, &MFMediaType_Video)
                .map_err(platform)?;
            media_type
                .SetGUID(&MF_MT_SUBTYPE, &MFVideoFormat_NV12)
                .map_err(platform)?;
            media_type
                .SetUINT32(&MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive.0 as u32)
                .map_err(platform)?;
            media_type
                .SetUINT64(
                    &MF_MT_FRAME_SIZE,
                    pack(self.config.width, self.config.height),
                )
                .map_err(platform)?;
            media_type
                .SetUINT64(&MF_MT_FRAME_RATE, pack(self.config.frame_rate, 1))
                .map_err(platform)?;
            media_type
                .SetUINT64(&MF_MT_PIXEL_ASPECT_RATIO, pack(1, 1))
                .map_err(platform)?;
            // Without this the transform has to infer the row pitch of the buffers it is handed,
            // and rejects them with a bare "the parameter is incorrect" when its guess disagrees.
            // NV12 luma is one byte per pixel, so the stride is the width.
            media_type
                .SetUINT32(&MF_MT_DEFAULT_STRIDE, self.config.width)
                .map_err(platform)?;
        }
        Ok(media_type)
    }

    /// Wraps the current NV12 buffer in a Media Foundation sample.
    fn sample_from_nv12(&self, presentation_time_us: i64) -> Result<IMFSample, EncodeError> {
        let length = u32::try_from(self.nv12.len()).map_err(|_| {
            EncodeError::Platform("frame is larger than a Media Foundation buffer".into())
        })?;
        // SAFETY: takes only a size, and returns an owned interface or an error.
        let buffer = unsafe { MFCreateMemoryBuffer(length) }.map_err(platform)?;

        // SAFETY: the buffer was just created with room for `length` bytes, the out-parameters live
        // for the call, and the copy below is bounded by the capacity Lock reports.
        unsafe {
            let mut destination: *mut u8 = std::ptr::null_mut();
            // The buffer's capacity, which is its *maximum* length. Its current length is zero
            // until we set it below, so asking for that instead would bound the copy to nothing
            // and hand the encoder a blank frame that still encodes into a plausible-looking
            // stream — the reason this reads the second out-parameter and not the third.
            let mut capacity = 0u32;
            buffer
                .Lock(&raw mut destination, Some(&raw mut capacity), None)
                .map_err(platform)?;
            // SAFETY: Lock handed back a buffer of at least `capacity` bytes, and the copy is
            // bounded by the smaller of it and the source.
            std::ptr::copy_nonoverlapping(
                self.nv12.as_ptr(),
                destination,
                self.nv12.len().min(capacity as usize),
            );
            buffer.Unlock().map_err(platform)?;
            buffer.SetCurrentLength(length).map_err(platform)?;
        }

        // SAFETY: takes no arguments, and returns an owned interface or an error.
        let sample = unsafe { MFCreateSample() }.map_err(platform)?;
        // SAFETY: the sample and its buffer were both created above and are live.
        unsafe {
            sample.AddBuffer(&buffer).map_err(platform)?;
            sample
                .SetSampleTime(us_to_hns(presentation_time_us))
                .map_err(platform)?;
            sample
                .SetSampleDuration(HNS_PER_SECOND / i64::from(self.config.frame_rate))
                .map_err(platform)?;
        }
        Ok(sample)
    }

    /// Drains one encoded access unit, if the encoder has produced one.
    fn drain(&mut self) -> Result<Option<EncodedFrame>, EncodeError> {
        let mut buffers = [MFT_OUTPUT_DATA_BUFFER::default(); 1];
        let mut status = 0u32;

        // Most software encoders do not allocate their own output samples, and handing one a null
        // sample is refused with a bare "the parameter is incorrect" that names nothing. Ask the
        // transform which kind it is and allocate on its behalf when it expects that.
        let provides_samples = self.provides_own_samples()?;
        if !provides_samples {
            buffers[0].pSample = std::mem::ManuallyDrop::new(Some(self.allocate_output_sample()?));
        }

        // SAFETY: `buffers` holds exactly one correctly-initialised entry, carrying a sample when
        // the transform expects the caller to supply one and null when it allocates its own.
        let result = unsafe {
            self.transform
                .ProcessOutput(0, &mut buffers, &raw mut status)
        };
        match result {
            Ok(()) => {}
            Err(error) if error.code() == MF_E_TRANSFORM_NEED_MORE_INPUT => return Ok(None),
            Err(error) => return Err(platform(error)),
        }

        let sample = buffers[0].pSample.take().ok_or_else(|| {
            EncodeError::Platform("the encoder reported output but produced none".into())
        })?;

        let encoded = read_sample(&sample)?;
        Ok(Some(encoded))
    }

    /// Tunes the encoder for a live mirror rather than for a file.
    ///
    /// Left to its defaults, the Media Foundation H.264 encoder buffers deeply — measured on this
    /// project at roughly seventeen frames before the first access unit appears, which is over half
    /// a second of latency before a single pixel reaches the television, and it never catches up.
    /// These four settings are what turn it from a file encoder into a streaming one:
    ///
    /// * low-latency mode caps the internal lookahead,
    /// * real-time mode tells it to favour deadline over quality,
    /// * B-pictures are removed because they cannot be emitted until a later frame is encoded,
    ///   which is latency by construction,
    /// * constant bitrate keeps the transport's pacing predictable.
    ///
    /// Each is optional. An encoder that rejects one still produces a correct stream.
    fn apply_low_latency_settings(&self) {
        // SAFETY: the transform is live. ICodecAPI is optional, and an encoder that does not
        // implement it is handled by the early return rather than by failing the session.
        let Ok(codec_api) = self.transform.cast::<ICodecAPI>() else {
            return;
        };

        set_codec_bool(&codec_api, &CODECAPI_AVLowLatencyMode, true);
        set_codec_bool(&codec_api, &CODECAPI_AVEncCommonRealTime, true);
        set_codec_u32(&codec_api, &CODECAPI_AVEncMPVDefaultBPictureCount, 0);
    }

    /// Applies rate control, once the media types are committed.
    ///
    /// The mirror image of [`Self::apply_low_latency_settings`], and the ordering is the whole
    /// point. Latency properties are only modifiable *before* a type is committed; rate control
    /// properties are only modifiable *after*. Set on the wrong side of `SetOutputType`, each is
    /// accepted with an OK that changes nothing, so both orderings compile and appear to work.
    fn apply_rate_control_settings(&self) {
        // SAFETY: the transform is live. ICodecAPI is optional, and an encoder that does not
        // implement it is handled by the early return rather than by failing the session.
        let Ok(codec_api) = self.transform.cast::<ICodecAPI>() else {
            return;
        };

        set_codec_u32(
            &codec_api,
            &CODECAPI_AVEncCommonRateControlMode,
            RATE_CONTROL_CBR,
        );

        // The bitrate has to be stated here as well as on the output media type. Selecting a rate
        // control mode without a target leaves this encoder running at a floor of a few hundred
        // bytes a frame: it still produces a valid, decodable stream, so every counter on both
        // sides looks healthy while the picture is quantised into flat blocks of colour.
        set_codec_u32(
            &codec_api,
            &CODECAPI_AVEncCommonMeanBitRate,
            self.config.bitrate_bits_per_second,
        );

        // Use the largest representable GOP rather than scheduling periodic IDRs. Receiver loss
        // feedback drives request_key_frame_now, so recovery remains immediate without repeatedly
        // paying the size and latency cost of an intra frame on a healthy link.
        set_codec_u32(&codec_api, &CODECAPI_AVEncMPVGOPSize, UNBOUNDED_GOP_SIZE);
    }

    /// Asks the encoder to make the next access unit a key frame.
    ///
    /// Separate from [`Self::apply_low_latency_settings`] because this one is set per frame, while
    /// those are set once before the media types are committed.
    fn request_key_frame_now(&self) {
        // SAFETY: the transform is live. ICodecAPI is optional; an encoder without it still
        // produces a correct stream, just without on-demand key frames.
        let Ok(codec_api) = self.transform.cast::<ICodecAPI>() else {
            return;
        };

        set_codec_u32(&codec_api, &CODECAPI_AVEncVideoForceKeyFrame, 1);
    }

    /// Whether the transform allocates its own output samples.
    fn provides_own_samples(&self) -> Result<bool, EncodeError> {
        // SAFETY: stream 0 exists on every encoder this module will accept.
        let info = unsafe { self.transform.GetOutputStreamInfo(0) }.map_err(platform)?;
        Ok(provides_own_samples(info.dwFlags))
    }

    /// Allocates an output sample large enough for whatever the transform says it needs.
    fn allocate_output_sample(&self) -> Result<IMFSample, EncodeError> {
        // SAFETY: stream 0 exists; the size it reports is what the transform will write into.
        let info = unsafe { self.transform.GetOutputStreamInfo(0) }.map_err(platform)?;
        // A transform that reports no size still has to put an access unit somewhere, so fall back
        // to a frame-sized buffer rather than allocating nothing and failing on the next call.
        let size = if info.cbSize > 0 {
            info.cbSize
        } else {
            nv12_len(self.config.width, self.config.height) as u32
        };

        // SAFETY: takes only a size, and returns an owned interface or an error.
        let buffer = unsafe { MFCreateMemoryBuffer(size) }.map_err(platform)?;
        // SAFETY: takes no arguments, and returns an owned interface or an error.
        let sample = unsafe { MFCreateSample() }.map_err(platform)?;
        // SAFETY: both objects were just created and the buffer is empty.
        unsafe { sample.AddBuffer(&buffer) }.map_err(platform)?;
        Ok(sample)
    }
}

impl VideoEncoder for H264Encoder {
    fn codec_specific_data(&self) -> &[Vec<u8>] {
        &self.codec_specific_data
    }

    fn output_size(&self) -> (u32, u32) {
        (self.config.width, self.config.height)
    }

    fn codec(&self) -> VideoCodec {
        // Always H.264: the constructor refuses any other codec rather than silently producing
        // this one under another name.
        VideoCodec::H264
    }

    fn submit(
        &mut self,
        frame: &SourceFrame,
        force_key_frame: bool,
    ) -> Result<Option<EncodedFrame>, EncodeError> {
        if force_key_frame {
            self.request_key_frame_now();
        }

        if frame.width != self.config.width || frame.height != self.config.height {
            return Err(EncodeError::FrameSizeChanged {
                expected: (self.config.width, self.config.height),
                actual: (frame.width, frame.height),
            });
        }

        // Software encoding is a processor activity: a frame that is still on the GPU would have to
        // be read back first, and the caller is better placed to decide whether that is worth doing
        // than this encoder is.
        let FrameData::Bgra { pixels, stride } = &frame.data else {
            return Err(EncodeError::UnsupportedFrameData);
        };
        bgra_to_nv12(pixels, frame.width, frame.height, *stride, &mut self.nv12)
            .map_err(|error| EncodeError::Platform(error.to_string()))?;
        debug_assert_eq!(self.nv12.len(), nv12_len(frame.width, frame.height));

        let sample = self.sample_from_nv12(frame.presentation_time_us)?;
        // SAFETY: the transform is streaming, and the sample holds one frame of the configured size.
        unsafe { self.transform.ProcessInput(0, &sample, 0) }.map_err(platform)?;
        self.drain()
    }
}

impl Drop for H264Encoder {
    fn drop(&mut self) {
        if !self.streaming {
            return;
        }
        // SAFETY: the transform is live and streaming; these are the documented teardown messages
        // and a failure here has nowhere useful to go.
        unsafe {
            let _ = self
                .transform
                .ProcessMessage(MFT_MESSAGE_NOTIFY_END_OF_STREAM, 0);
            let _ = self.transform.ProcessMessage(MFT_MESSAGE_COMMAND_FLUSH, 0);
            let _ = self
                .transform
                .ProcessMessage(MFT_MESSAGE_NOTIFY_END_STREAMING, 0);
        }
    }
}

/// Finds a synchronous H.264 encoder transform.
fn find_encoder() -> Result<IMFTransform, EncodeError> {
    let output_info = MFT_REGISTER_TYPE_INFO {
        guidMajorType: MFMediaType_Video,
        guidSubtype: MFVideoFormat_H264,
    };

    let mut activates = std::ptr::null_mut();
    let mut count = 0u32;

    // SAFETY: both out-parameters are valid, and the returned array is released below.
    unsafe {
        MFTEnumEx(
            MFT_CATEGORY_VIDEO_ENCODER,
            MFT_ENUM_FLAG(MFT_ENUM_FLAG_SYNCMFT.0 | MFT_ENUM_FLAG_SORTANDFILTER.0),
            None,
            Some(&raw const output_info),
            &raw mut activates,
            &raw mut count,
        )
        .map_err(platform)?;
    }

    if count == 0 || activates.is_null() {
        return Err(EncodeError::Platform(
            "Windows reports no H.264 encoder on this machine".into(),
        ));
    }

    // SAFETY: MFTEnumEx populated `count` activation objects; the first is the best match because
    // SORTANDFILTER asked the platform to rank them.
    let activate = unsafe { (*activates).clone() }
        .ok_or_else(|| EncodeError::Platform("the H.264 encoder activation was empty".into()))?;

    // SAFETY: every activation object in the array is addressed exactly once and released here,
    // then the array itself is freed, matching what MFTEnumEx documents.
    unsafe {
        for index in 1..count as usize {
            let _ = (*activates.add(index)).take();
        }
        windows::Win32::System::Com::CoTaskMemFree(Some(activates.cast()));
    }

    // SAFETY: the activation object is live and ActivateObject returns the transform it describes.
    unsafe { activate.ActivateObject::<IMFTransform>() }.map_err(platform)
}

/// Reads the SPS and PPS the receiver needs before the first access unit.
pub(crate) fn read_sequence_header(transform: &IMFTransform) -> Result<Vec<Vec<u8>>, EncodeError> {
    // SAFETY: the output type was set above, so the transform has one to report.
    let output_type = unsafe { transform.GetOutputCurrentType(0) }.map_err(platform)?;

    // SAFETY: the type is live; an encoder without the attribute returns an error, handled below.
    let length = match unsafe { output_type.GetBlobSize(&MF_MT_MPEG_SEQUENCE_HEADER) } {
        Ok(length) if length > 0 => length,
        _ => {
            return Err(EncodeError::Platform(
                "the H.264 encoder did not publish SPS/PPS before streaming".into(),
            ))
        }
    };

    let mut header = vec![0u8; length as usize];
    // SAFETY: the blob is exactly `length` bytes, which is what the buffer was sized to.
    unsafe {
        output_type
            .GetBlob(&MF_MT_MPEG_SEQUENCE_HEADER, &mut header, None)
            .map_err(platform)?;
    }

    normalise_parameter_sets(&header)
}

/// Normalises an Annex B or AVC configuration record into separate SPS and PPS Annex B units.
///
/// Media Foundation implementations publish either representation. Android receives one canonical
/// representation regardless: `csd-0` is SPS, `csd-1` is PPS, and both start with `00 00 00 01`.
pub(crate) fn normalise_parameter_sets(header: &[u8]) -> Result<Vec<Vec<u8>>, EncodeError> {
    let nals = if header.first() == Some(&1) {
        avcc_parameter_sets(header)?
    } else {
        annex_b_parameter_sets(header)?
    };

    let sps = nals
        .iter()
        .find(|nal| nal.first().is_some_and(|byte| byte & 0x1f == 7));
    let pps = nals
        .iter()
        .find(|nal| nal.first().is_some_and(|byte| byte & 0x1f == 8));
    let (Some(sps), Some(pps)) = (sps, pps) else {
        return Err(EncodeError::Platform(
            "the H.264 encoder sequence header did not contain both SPS and PPS".into(),
        ));
    };

    Ok(vec![annex_b_unit(sps), annex_b_unit(pps)])
}

/// Borrows every NAL payload from an Annex B byte stream.
fn annex_b_parameter_sets(header: &[u8]) -> Result<Vec<&[u8]>, EncodeError> {
    let Some((first, mut code_len)) = find_start_code(header, 0) else {
        return Err(invalid_sequence_header());
    };
    if header[..first].iter().any(|&byte| byte != 0) {
        return Err(invalid_sequence_header());
    }

    let mut at = first;
    let mut nals = Vec::new();
    loop {
        let payload_start = at + code_len;
        let next = find_start_code(header, payload_start);
        let mut payload_end = next.map_or(header.len(), |(index, _)| index);
        while payload_end > payload_start && header[payload_end - 1] == 0 {
            payload_end -= 1;
        }
        if payload_end > payload_start {
            nals.push(&header[payload_start..payload_end]);
        }

        let Some((next_at, next_len)) = next else {
            break;
        };
        at = next_at;
        code_len = next_len;
    }
    Ok(nals)
}

/// Borrows SPS/PPS NAL payloads from an `AVCDecoderConfigurationRecord` (`avcC`).
fn avcc_parameter_sets(header: &[u8]) -> Result<Vec<&[u8]>, EncodeError> {
    if header.len() < 7 || header[0] != 1 {
        return Err(invalid_sequence_header());
    }

    let mut at = 5usize;
    let sps_count = usize::from(header[at] & 0x1f);
    at += 1;
    let mut nals = Vec::with_capacity(sps_count.saturating_add(1));
    for _ in 0..sps_count {
        nals.push(take_avcc_nal(header, &mut at)?);
    }

    let Some(&pps_count) = header.get(at) else {
        return Err(invalid_sequence_header());
    };
    at += 1;
    for _ in 0..usize::from(pps_count) {
        nals.push(take_avcc_nal(header, &mut at)?);
    }
    Ok(nals)
}

fn take_avcc_nal<'a>(header: &'a [u8], at: &mut usize) -> Result<&'a [u8], EncodeError> {
    let length_bytes = header
        .get(*at..(*at).saturating_add(2))
        .ok_or_else(invalid_sequence_header)?;
    let length = usize::from(u16::from_be_bytes([length_bytes[0], length_bytes[1]]));
    *at += 2;
    if length == 0 {
        return Err(invalid_sequence_header());
    }
    let end = (*at)
        .checked_add(length)
        .ok_or_else(invalid_sequence_header)?;
    let nal = header.get(*at..end).ok_or_else(invalid_sequence_header)?;
    *at = end;
    Ok(nal)
}

fn find_start_code(bytes: &[u8], from: usize) -> Option<(usize, usize)> {
    (from..bytes.len().saturating_sub(2)).find_map(|index| {
        if bytes.get(index..index + 4) == Some(&[0, 0, 0, 1]) {
            Some((index, 4))
        } else if bytes.get(index..index + 3) == Some(&[0, 0, 1]) {
            Some((index, 3))
        } else {
            None
        }
    })
}

fn annex_b_unit(nal: &[u8]) -> Vec<u8> {
    let mut unit = Vec::with_capacity(4 + nal.len());
    unit.extend_from_slice(&[0, 0, 0, 1]);
    unit.extend_from_slice(nal);
    unit
}

fn invalid_sequence_header() -> EncodeError {
    EncodeError::Platform("the H.264 encoder published malformed sequence data".into())
}

/// Copies an encoded sample out of Media Foundation's memory.
pub(crate) fn read_sample(sample: &IMFSample) -> Result<EncodedFrame, EncodeError> {
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
    let bytes = unsafe { std::slice::from_raw_parts(data, length as usize) }.to_vec();
    // SAFETY: balances the Lock above.
    unsafe { buffer.Unlock() }.map_err(platform)?;

    // SAFETY: the sample carries its own timing.
    let presentation_time_us = unsafe { sample.GetSampleTime() }
        .map(hns_to_us)
        .unwrap_or_default();

    Ok(EncodedFrame {
        key_frame: is_key_frame(&bytes),
        data: bytes,
        presentation_time_us,
    })
}

/// Whether an access unit contains an IDR picture.
///
/// Read from the bitstream rather than from the sample's `CleanPoint` attribute: encoders are
/// inconsistent about setting it, and a receiver that never sees a frame marked as a key frame
/// waits forever for one it can start decoding from.
pub(crate) fn is_key_frame(access_unit: &[u8]) -> bool {
    let mut index = 0usize;
    while index + 4 <= access_unit.len() {
        let (start_code_len, found) = if access_unit[index..index + 4] == [0, 0, 0, 1] {
            (4, true)
        } else if access_unit[index..index + 3] == [0, 0, 1] {
            (3, true)
        } else {
            (0, false)
        };

        if !found {
            index += 1;
            continue;
        }

        if let Some(&header) = access_unit.get(index + start_code_len) {
            // The low five bits of the NAL header carry the unit type; 5 is an IDR slice.
            if header & 0x1f == 5 {
                return true;
            }
        }
        index += start_code_len;
    }
    false
}

/// `eAVEncCommonRateControlMode_CBR`: a constant bitrate, which keeps pacing predictable.
const RATE_CONTROL_CBR: u32 = 0;

/// Sets an optional boolean codec property, ignoring an encoder that does not offer it.
pub(crate) fn set_codec_bool(codec_api: &ICodecAPI, property: &windows::core::GUID, value: bool) {
    let variant = VARIANT::from(value);
    // SAFETY: the variant outlives the call, and a rejected property is a normal outcome here.
    let _ = unsafe { codec_api.SetValue(property, &raw const variant) };
}

/// Sets an optional numeric codec property, ignoring an encoder that does not offer it.
pub(crate) fn set_codec_u32(codec_api: &ICodecAPI, property: &windows::core::GUID, value: u32) {
    let variant = VARIANT::from(value);
    // SAFETY: as above.
    let _ = unsafe { codec_api.SetValue(property, &raw const variant) };
}

/// `MFT_OUTPUT_STREAM_PROVIDES_SAMPLES`: the transform always allocates its own output samples.
const OUTPUT_STREAM_PROVIDES_SAMPLES: u32 = 0x100;
/// `MFT_OUTPUT_STREAM_CAN_PROVIDE_SAMPLES`: the transform will allocate if the caller does not.
const OUTPUT_STREAM_CAN_PROVIDE_SAMPLES: u32 = 0x200;

/// Whether a transform reporting `flags` allocates its own output samples.
///
/// When it does not, `ProcessOutput` must be handed a sample to write into; passing null instead is
/// refused with a bare "the parameter is incorrect" that names neither the stream nor the reason.
fn provides_own_samples(flags: u32) -> bool {
    flags & (OUTPUT_STREAM_PROVIDES_SAMPLES | OUTPUT_STREAM_CAN_PROVIDE_SAMPLES) != 0
}

/// Packs two 32-bit values into the 64-bit form Media Foundation uses for sizes and ratios.
pub(crate) fn pack(high: u32, low: u32) -> u64 {
    (u64::from(high) << 32) | u64::from(low)
}

pub(crate) fn us_to_hns(microseconds: i64) -> i64 {
    microseconds * 10
}

pub(crate) fn hns_to_us(hns: i64) -> i64 {
    hns / 10
}

pub(crate) fn platform(error: windows::core::Error) -> EncodeError {
    EncodeError::Platform(error.message())
}

/// The GUID of the H.264 encoder's sequence-header attribute, for tests that need to name it.
#[cfg(test)]
const SEQUENCE_HEADER_GUID: windows::core::GUID = MF_MT_MPEG_SEQUENCE_HEADER;

#[cfg(test)]
#[path = "h264_tests.rs"]
mod tests;

#[cfg(test)]
#[path = "h264_live_report.rs"]
mod live_report;

#[cfg(test)]
#[path = "h264_resolution_probe.rs"]
mod resolution_probe;

#[cfg(test)]
#[path = "h264_codec_api_probe.rs"]
mod codec_api_probe;
