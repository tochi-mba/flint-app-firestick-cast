//! A hardware H.264 encoder, presented through the same contract as the software one.
//!
//! The asynchronous event pump and the business of finding and unlocking the transform live in
//! [`super::h264_hardware_transform`]. What is left here is the encoder itself: how a frame becomes
//! a sample the transform will read, how the pump's events are turned into access units, and the
//! configuration a low-latency mirror needs.
//!
//! The [`VideoEncoder`] contract on the outside is identical to the software encoder's, which is
//! what lets the session stay ignorant of which kind it got.

use windows::core::Interface;
use windows::Win32::Media::MediaFoundation::{
    CODECAPI_AVEncCommonMeanBitRate, CODECAPI_AVEncCommonRateControlMode,
    CODECAPI_AVEncCommonRealTime, CODECAPI_AVEncMPVDefaultBPictureCount, CODECAPI_AVEncMPVGOPSize,
    CODECAPI_AVEncVideoForceKeyFrame, CODECAPI_AVLowLatencyMode, ICodecAPI, IMFMediaType,
    IMFSample, MFCreateMediaType, MFMediaType_Video, MFVideoFormat_H264, MFVideoFormat_NV12,
    MFVideoInterlace_Progressive, MFT_MESSAGE_COMMAND_FLUSH, MFT_MESSAGE_NOTIFY_BEGIN_STREAMING,
    MFT_MESSAGE_NOTIFY_END_OF_STREAM, MFT_MESSAGE_NOTIFY_END_STREAMING,
    MFT_MESSAGE_NOTIFY_START_OF_STREAM, MFT_OUTPUT_DATA_BUFFER, MF_E_TRANSFORM_NEED_MORE_INPUT,
    MF_E_TRANSFORM_STREAM_CHANGE, MF_MT_AVG_BITRATE, MF_MT_DEFAULT_STRIDE, MF_MT_FRAME_RATE,
    MF_MT_FRAME_SIZE, MF_MT_INTERLACE_MODE, MF_MT_MAJOR_TYPE, MF_MT_PIXEL_ASPECT_RATIO,
    MF_MT_SUBTYPE,
};

use super::h264_hardware_transform::{HardwareTransform, TransformEvent};
use super::nv12_texture::Nv12TexturePool;
use super::video::{
    EncodeError, EncodedFrame, EncoderConfig, FrameData, SourceFrame, VideoEncoder,
};
use super::VideoCodec;
use crate::convert::gpu_nv12::GpuNv12Converter;
use crate::convert::nv12::bgra_to_nv12;
use crate::encode::h264::{platform, set_codec_bool, set_codec_u32};

/// A hardware H.264 encoder presented through the same contract as the software one.
pub struct HardwareH264Encoder {
    hardware: HardwareTransform,
    config: EncoderConfig,
    codec_specific_data: Vec<Vec<u8>>,
    nv12: Vec<u8>,
    /// Access units the pump collected while looking for something else.
    ready: std::collections::VecDeque<EncodedFrame>,
    /// Converts captured BGRA textures to NV12 on the GPU, when frames arrive as textures.
    converter: Option<GpuNv12Converter>,
    /// NV12 textures on the encoder's own device, which is where it reads frames from.
    ///
    /// Built lazily on the first frame: the pool needs the frame size, and a configuration that
    /// cannot allocate NV12 textures should fail as a frame rather than as a construction, so the
    /// caller can fall back.
    textures: Option<Nv12TexturePool>,
    /// Samples handed to the encoder that it may not have finished reading.
    ///
    /// An asynchronous transform does not copy the frame during `ProcessInput`; it queues the
    /// sample and reads the pixels later, on its own thread. Dropping the caller's reference the
    /// moment `ProcessInput` returns is therefore a use-after-free whenever the transform does not
    /// take a reference of its own — and the symptom is not a crash. The encoder reads whatever now
    /// occupies that memory, compresses it perfectly happily, and emits a valid H.264 stream of
    /// noise: correct parameter sets, correct dimensions, plausible bitrate, and a picture that
    /// decodes to nothing. On a television that is a flat green field.
    ///
    /// The tell was a solid black frame encoding to 481KB while a solid white one took 32KB. Both
    /// are uniform; neither should cost more than a few hundred bytes.
    in_flight: std::collections::VecDeque<IMFSample>,
    /// Whether the next frame fed must come out as an IDR.
    ///
    /// Held as intent rather than acted on immediately, because `ICodecAPI` applies the request to
    /// the *next frame the encoder takes*. Setting it at a moment when no frame is pending — right
    /// after a flush, say — leaves nothing for it to attach to, and the request evaporates.
    force_next_key_frame: bool,
    /// How many frames the encoder has asked for and not yet been given.
    ///
    /// A count rather than a flag. A hardware encoder pipelines: it asks for several frames before
    /// it returns the first access unit, and each `METransformNeedInput` is delivered exactly once.
    /// Collapsing two requests into one boolean loses a frame's worth of permission, and the pump
    /// then waits for a request that has already been and gone.
    input_requests: usize,
    streaming: bool,
}

impl std::fmt::Debug for HardwareH264Encoder {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        formatter
            .debug_struct("HardwareH264Encoder")
            .field("config", &self.config)
            .field(
                "codec_specific_data_blocks",
                &self.codec_specific_data.len(),
            )
            .field("ready", &self.ready.len())
            .finish()
    }
}

impl HardwareH264Encoder {
    /// How many events to consume in one pump before giving up on this frame.
    ///
    /// A hardware transform can raise several events per frame; this bounds the loop so a
    /// misbehaving driver stalls one frame rather than wedging the whole session.
    const MAX_EVENTS_PER_TICK: usize = 16;

    /// How long to wait for the encoder to ask for a frame before giving up on it.
    ///
    /// Generous against a frame budget of about sixteen milliseconds, because this is a safety net
    /// rather than a pacing control: a healthy encoder answers in well under a millisecond, and one
    /// that has not answered in a quarter of a second is not going to.
    const INPUT_TIMEOUT: std::time::Duration = std::time::Duration::from_millis(250);

    /// How many submitted samples to keep alive while the encoder works through them.
    ///
    /// Generous against any real encoder's pipeline depth. This bounds memory rather than
    /// correctness: samples are normally released as soon as the encoder asks for the next frame.
    const MAX_IN_FLIGHT: usize = 16;

    /// How many synthetic frames to offer before giving up on getting parameter sets out.
    ///
    /// Comfortably more than any encoder's pipeline depth. A hardware encoder typically returns its
    /// first access unit within a handful of frames; one that has taken sixty is not going to.
    const PRIMING_FRAMES: usize = 60;

    /// Creates a hardware encoder on `adapter_luid`, configured for `config`.
    ///
    /// # Errors
    /// [`EncodeError::InvalidConfig`] for a configuration that cannot produce a stream, and
    /// [`EncodeError::Platform`] when the adapter has no usable hardware encoder — an ordinary
    /// answer, which the caller handles by falling back to software.
    pub fn new(config: EncoderConfig, adapter_luid: i64) -> Result<Self, EncodeError> {
        Self::build(config, adapter_luid, None)
    }

    /// Creates a hardware encoder that runs on a device the caller already owns.
    ///
    /// # Errors
    /// See [`Self::new`].
    pub fn open_on_device(
        config: EncoderConfig,
        adapter_luid: i64,
        device: windows::Win32::Graphics::Direct3D11::ID3D11Device,
        context: windows::Win32::Graphics::Direct3D11::ID3D11DeviceContext,
    ) -> Result<Self, EncodeError> {
        Self::build(config, adapter_luid, Some((device, context)))
    }

    fn build(
        config: EncoderConfig,
        adapter_luid: i64,
        existing: Option<(
            windows::Win32::Graphics::Direct3D11::ID3D11Device,
            windows::Win32::Graphics::Direct3D11::ID3D11DeviceContext,
        )>,
    ) -> Result<Self, EncodeError> {
        if !config.is_valid() {
            return Err(EncodeError::InvalidConfig);
        }
        if config.codec != VideoCodec::H264 {
            return Err(EncodeError::Platform(format!(
                "this encoder only produces H.264, not {:?}",
                config.codec
            )));
        }

        let hardware = match existing {
            Some((device, context)) => {
                HardwareTransform::open_on_device(adapter_luid, device, context)?
            }
            None => HardwareTransform::open(adapter_luid)?,
        };
        let mut encoder = Self {
            hardware,
            config,
            codec_specific_data: Vec::new(),
            nv12: Vec::new(),
            ready: std::collections::VecDeque::new(),
            converter: None,
            textures: None,
            in_flight: std::collections::VecDeque::new(),
            force_next_key_frame: false,
            input_requests: 0,
            streaming: false,
        };
        encoder.configure()?;
        Ok(encoder)
    }

    /// What the transform settled on for its input, and what it needs a buffer to look like.
    ///
    /// Read back rather than assumed. `SetInputType` succeeding means the transform accepted a type
    /// it can work with, not that it accepted the one that was offered: a hardware encoder is free
    /// to negotiate a different stride or a padded frame size, and a caller that keeps filling
    /// buffers to its own idea of the layout then hands over pixels the encoder reads as noise.
    ///
    /// # Errors
    /// [`EncodeError::Platform`] when the transform has no current input type.
    pub fn negotiated_input(&self) -> Result<(u32, u32, i32, u32), EncodeError> {
        let transform = self.hardware.transform();
        // SAFETY: stream 0 exists on every encoder.
        let media_type = unsafe { transform.GetInputCurrentType(0) }.map_err(platform)?;
        // SAFETY: the type is live; all three keys are standard.
        let size = unsafe { media_type.GetUINT64(&MF_MT_FRAME_SIZE) }.unwrap_or(0);
        let stride = unsafe { media_type.GetUINT32(&MF_MT_DEFAULT_STRIDE) }.unwrap_or(0) as i32;

        let mut info = windows::Win32::Media::MediaFoundation::MFT_INPUT_STREAM_INFO::default();
        // SAFETY: stream 0 exists and the out-parameter is valid.
        unsafe { transform.GetInputStreamInfo(0, &mut info) }.map_err(platform)?;

        Ok(((size >> 32) as u32, size as u32, stride, info.cbSize))
    }

    /// The configuration this encoder was built with.
    #[must_use]
    pub fn config(&self) -> EncoderConfig {
        self.config
    }

    fn configure(&mut self) -> Result<(), EncodeError> {
        let transform = self.hardware.transform().clone();

        // Before the media types, because latency properties stop being modifiable the moment a
        // type is committed — and are then accepted with an OK that changes nothing. On the
        // software path this exact ordering mistake cost eighteen frames of startup delay.
        self.apply_low_latency_settings();

        let output = build_media_type(&MFVideoFormat_H264, &self.config, true)?;
        // Output before input, exactly as on the software path: an encoder cannot describe the
        // pixels it accepts until it knows what it is producing.
        // SAFETY: the transform is live and stream 0 exists on every encoder.
        unsafe { transform.SetOutputType(0, &output, 0) }.map_err(platform)?;

        let input = build_media_type(&MFVideoFormat_NV12, &self.config, false)?;
        // SAFETY: as above.
        unsafe { transform.SetInputType(0, &input, 0) }.map_err(platform)?;

        // A hardware encoder usually has no sequence header to publish yet: unlike the software
        // MFT, it decides SPS and PPS when it encodes the first key frame and emits them in-band at
        // the head of that access unit. Treating the absence as fatal — which is correct for the
        // software path, where a missing header means the receiver could never configure a decoder
        // — would reject every hardware encoder on the machine. So it is taken if offered, and
        // otherwise lifted out of the first key frame by `capture_parameter_sets`.
        self.codec_specific_data =
            crate::encode::h264::read_sequence_header(&transform).unwrap_or_default();

        // After the media types, for the same reason in reverse: rate control is only modifiable
        // once a type is committed.
        self.apply_rate_control_settings();

        // SAFETY: the transform is configured, which is what these messages require.
        unsafe {
            transform
                .ProcessMessage(MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, 0)
                .map_err(platform)?;
            transform
                .ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM, 0)
                .map_err(platform)?;
        }
        self.streaming = true;

        // Prove the transform actually drives its event queue before reporting success. A hardware
        // MFT that is configured but never raises `METransformNeedInput` — typically because the
        // driver wants a D3D device manager this path does not supply — would otherwise be
        // discovered one frame at a time, at 250ms each, for the life of the session. Failing here
        // instead lets the caller fall back to software immediately.
        match self.hardware.next_event_within(Self::INPUT_TIMEOUT)? {
            Some(TransformEvent::NeedInput) => {
                // Recorded rather than discarded: this is the encoder's standing request for the
                // first frame, and forgetting it would make `submit` wait for a second one.
                self.input_requests += 1;
            }
            _ => {
                return Err(EncodeError::Platform(
                    "the hardware encoder never asked for input; falling back to software".into(),
                ))
            }
        }

        self.prime()
    }

    /// Runs synthetic frames through the encoder until it publishes SPS and PPS.
    ///
    /// The software encoder can be asked for its sequence header the moment it is configured. A
    /// hardware encoder cannot: it decides the parameter sets when it encodes its first key frame,
    /// and publishes them in-band. That difference would otherwise leak all the way out to the
    /// wire, because Flint sends `VIDEO_CONFIG` *before* the first access unit — a receiver with no
    /// SPS and PPS cannot construct a decoder at all, and shows a black screen rather than an
    /// error. So the encoder is made to answer the question here, while there is still somewhere
    /// useful to put the answer.
    ///
    /// The priming frames themselves are then flushed. Keeping them would be worse than wasteful:
    /// the receiver never sees them, so any later frame predicting from one would decode into
    /// garbage. A flush also restarts the encoder's group of pictures, which is what makes the
    /// first real frame an IDR without having to ask for one.
    ///
    /// # Errors
    /// [`EncodeError::Platform`] when no parameter sets appear. That is a refusal rather than a
    /// warning: an encoder whose output this project's receiver cannot configure a decoder for is
    /// not usable, and reporting it here is what lets selection fall back to software.
    fn prime(&mut self) -> Result<(), EncodeError> {
        if std::env::var("FLINT_HW_NO_PRIME").is_ok() {
            // Diagnostic path: skip priming entirely, so the parameter sets come from the first
            // real key frame instead. Isolates "does priming corrupt the stream?" from every other
            // possibility, which is otherwise a rebuild-per-guess question.
            return Ok(());
        }

        let stride = self.config.width * 4;
        let frame = SourceFrame {
            width: self.config.width,
            height: self.config.height,
            // Black, because the content is irrelevant and a flat frame is the cheapest thing an
            // encoder can be asked to compress.
            data: FrameData::Bgra {
                pixels: vec![0u8; (stride * self.config.height) as usize],
                stride,
            },
            presentation_time_us: 0,
        };

        for _ in 0..Self::PRIMING_FRAMES {
            let _ = self.submit(&frame, true)?;
            if !self.codec_specific_data.is_empty() {
                break;
            }
        }

        if self.codec_specific_data.is_empty() {
            return Err(EncodeError::Platform(
                "the hardware encoder published no SPS or PPS; falling back to software".into(),
            ));
        }

        self.flush_priming()
    }

    /// Applies the properties that must be set before any media type is committed.
    ///
    /// Same set as the software encoder, and for the same reasons: low-latency mode stops the
    /// encoder buffering frames before it emits anything, real-time mode makes it favour the
    /// deadline over quality, and B-pictures are removed because one cannot be emitted until a
    /// later frame has been encoded — latency by construction.
    ///
    /// Every property is optional. The hardware path went without all of them until a key frame
    /// failed to appear at 720p and exposed the omission.
    fn apply_low_latency_settings(&self) {
        // SAFETY: the transform is live. ICodecAPI is optional, and an encoder without it still
        // produces a correct stream.
        let Ok(codec_api) = self.hardware.transform().cast::<ICodecAPI>() else {
            return;
        };
        set_codec_bool(&codec_api, &CODECAPI_AVLowLatencyMode, true);
        set_codec_bool(&codec_api, &CODECAPI_AVEncCommonRealTime, true);
        set_codec_u32(&codec_api, &CODECAPI_AVEncMPVDefaultBPictureCount, 0);
    }

    /// Applies rate control and the key-frame interval, once the media types are committed.
    ///
    /// This differs from the software path in one deliberate way: the group of pictures is bounded
    /// rather than effectively infinite. The software encoder honours an on-demand key-frame
    /// request reliably, so it can afford to emit an intra frame only when one is asked for. This
    /// machine's hardware encoder does not always honour that request, and an unbounded GOP would
    /// then mean a receiver that joins late — or loses a packet — never gets a frame it can start
    /// decoding from, and stares at a frozen picture indefinitely. A bounded interval caps that
    /// worst case at a known number of seconds, and costs one intra frame per interval on a link
    /// that is behaving.
    fn apply_rate_control_settings(&self) {
        // SAFETY: as above.
        let Ok(codec_api) = self.hardware.transform().cast::<ICodecAPI>() else {
            return;
        };
        set_codec_u32(
            &codec_api,
            &CODECAPI_AVEncCommonRateControlMode,
            RATE_CONTROL_CBR,
        );
        // Stated here as well as on the output media type: selecting a rate control mode without a
        // target leaves the encoder running at a floor of a few hundred bytes a frame, which is a
        // valid decodable stream that looks healthy on every counter while the picture is quantised
        // into flat blocks of colour.
        set_codec_u32(
            &codec_api,
            &CODECAPI_AVEncCommonMeanBitRate,
            self.config.bitrate_bits_per_second,
        );
        set_codec_u32(
            &codec_api,
            &CODECAPI_AVEncMPVGOPSize,
            gop_size(self.config.frame_rate),
        );
    }

    /// Hands one sample to the encoder, honouring any standing key-frame request first.
    ///
    /// The request has to be made here rather than wherever it was decided: `ICodecAPI` attaches it
    /// to the next frame the encoder takes, so a request made while the encoder holds nothing is
    /// simply lost. Every route to a key frame — the caller's, and the one priming leaves behind —
    /// therefore records intent and lets this apply it at the only moment that works.
    fn feed(&mut self, sample: &IMFSample) -> Result<(), EncodeError> {
        if self.force_next_key_frame {
            self.request_key_frame_now();
        }
        self.hardware.submit(sample)?;
        self.force_next_key_frame = false;

        // Held until the encoder asks for another frame, which is the only signal it gives that it
        // is finished with this one. See the field's own note: releasing here instead produces a
        // valid stream of noise rather than any kind of error.
        self.in_flight.push_back(sample.clone());
        // A ceiling, so a transform that stops asking cannot grow this without bound. Deep enough
        // to cover any realistic encoder pipeline.
        while self.in_flight.len() > Self::MAX_IN_FLIGHT {
            self.in_flight.pop_front();
        }
        Ok(())
    }

    /// Releases the oldest sample the encoder is holding, if any.
    ///
    /// Called when the transform asks for another frame: a request for input means the previous
    /// one has been consumed.
    fn release_oldest_in_flight(&mut self) {
        self.in_flight.pop_front();
    }

    /// Asks the encoder to make the next access unit a key frame.
    ///
    /// Set per frame, unlike the low-latency properties, and best-effort: an encoder that does not
    /// expose `ICodecAPI` still produces a correct stream, just without key frames on demand.
    fn request_key_frame_now(&self) {
        // SAFETY: the transform is live, and a rejected property is a normal outcome here.
        let Ok(codec_api) = self.hardware.transform().cast::<ICodecAPI>() else {
            return;
        };
        set_codec_u32(&codec_api, &CODECAPI_AVEncVideoForceKeyFrame, 1);
    }

    /// Discards everything priming produced and puts the encoder back at the start of a stream.
    fn flush_priming(&mut self) -> Result<(), EncodeError> {
        self.ready.clear();
        let transform = self.hardware.transform().clone();
        // SAFETY: the transform is live and streaming, which is what these messages require.
        unsafe {
            transform
                .ProcessMessage(MFT_MESSAGE_COMMAND_FLUSH, 0)
                .map_err(platform)?;
        }

        // Any request banked before the flush refers to a stream that no longer exists. Honouring
        // one afterwards would feed a frame the encoder never asked for.
        self.input_requests = 0;
        while self.hardware.poll_event()?.is_some() {}

        // SAFETY: as above. A flushed transform needs telling that a new stream is beginning before
        // it will ask for anything again.
        unsafe {
            transform
                .ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM, 0)
                .map_err(platform)?;
        }

        if let Some(TransformEvent::NeedInput) =
            self.hardware.next_event_within(Self::INPUT_TIMEOUT)?
        {
            self.input_requests += 1;
        }

        // A flush empties the encoder but does not start a new group of pictures, so without this
        // the first real frame is predicted from priming frames the receiver was never sent, and
        // decodes into garbage. Recorded rather than requested outright: there is no frame in the
        // encoder for the request to attach to yet.
        self.force_next_key_frame = true;
        Ok(())
    }

    /// Converts a captured BGRA texture straight into an NV12 texture the encoder reads.
    ///
    /// The whole point of the GPU path: no readback, no colour conversion on the processor, no
    /// upload. The video processor does the conversion and the downscale in one pass on the same
    /// device the encoder runs on, so the frame never crosses the bus at all.
    fn sample_from_texture(
        &mut self,
        texture: &windows::Win32::Graphics::Direct3D11::ID3D11Texture2D,
        source_size: (u32, u32),
        presentation_time_us: i64,
    ) -> Result<IMFSample, EncodeError> {
        self.ensure_textures()?;

        // Rebuilt when the desktop resolution changes, which is a real event mid-session: a
        // converter still scaling from the old size would silently letterbox or crop.
        let stale = match self.converter.as_ref() {
            Some(converter) => converter.source() != source_size,
            None => true,
        };
        if stale {
            let devices = self.hardware.devices();
            self.converter = Some(GpuNv12Converter::new(
                devices.device(),
                devices.context(),
                source_size,
                (self.config.width, self.config.height),
            )?);
        }

        let destination = self.textures.as_mut().expect("built above").next_texture();
        self.converter
            .as_mut()
            .expect("built above")
            .convert(texture, &destination)?;

        let duration = HNS_PER_SECOND / i64::from(self.config.frame_rate.max(1));
        self.textures.as_ref().expect("built above").wrap(
            &destination,
            crate::encode::h264::us_to_hns(presentation_time_us),
            duration,
        )
    }

    /// Builds the texture pool if it does not exist yet.
    fn ensure_textures(&mut self) -> Result<(), EncodeError> {
        if self.textures.is_none() {
            let devices = self.hardware.devices();
            self.textures = Some(Nv12TexturePool::new(
                devices.device().clone(),
                devices.context().clone(),
                self.config.width,
                self.config.height,
            )?);
        }
        Ok(())
    }

    /// Uploads the current NV12 buffer to a texture the encoder can read.
    ///
    /// This replaced a system-memory buffer, which this transform accepted and did not read. See
    /// [`Nv12TexturePool`] for the whole story; the short version is that a hardware encoder is a
    /// GPU object and will encode whatever is in GPU memory whether or not the frame was put there.
    fn sample_from_nv12_texture(
        &mut self,
        presentation_time_us: i64,
    ) -> Result<IMFSample, EncodeError> {
        self.ensure_textures()?;

        let duration = HNS_PER_SECOND / i64::from(self.config.frame_rate.max(1));
        let presentation = crate::encode::h264::us_to_hns(presentation_time_us);
        let nv12 = std::mem::take(&mut self.nv12);
        let result =
            self.textures
                .as_mut()
                .expect("just built")
                .upload(&nv12, presentation, duration);
        // Returned to its field either way, so the next frame reuses the same allocation rather
        // than growing a new one on the frame path.
        self.nv12 = nv12;
        result
    }

    /// Lifts SPS and PPS out of a key frame, the first time one carries them.
    ///
    /// A hardware encoder publishes no sequence header before streaming; the parameter sets arrive
    /// at the head of the first key frame instead. The receiver needs them as separate `csd-0` and
    /// `csd-1` blocks to configure `MediaCodec`, so they are extracted here rather than leaving the
    /// receiver to parse the bitstream. Only the first success is kept: a later key frame carries
    /// the same sets, and re-parsing every one would be wasted work on the frame path.
    fn capture_parameter_sets(&mut self, encoded: &EncodedFrame) {
        if !self.codec_specific_data.is_empty() || !encoded.key_frame {
            return;
        }
        if let Ok(sets) = crate::encode::h264::normalise_parameter_sets(&encoded.data) {
            self.codec_specific_data = sets;
        }
    }

    /// Renegotiates the output type after the transform reports a stream change.
    ///
    /// Hardware encoders routinely refuse to hand over the first access unit until the output type
    /// has been agreed a second time. The type set during configuration is a *request*; once the
    /// encoder has seen real frames it publishes the type it will actually produce — filling in
    /// details it could not know in advance, such as the exact profile and level the content needs
    /// — and withholds all output until the caller accepts it. Treating that as a failure is what
    /// makes a perfectly good encoder look broken.
    fn renegotiate_output(&mut self) -> Result<(), EncodeError> {
        let transform = self.hardware.transform().clone();
        // SAFETY: the transform is live and stream 0 exists on every encoder. Index 0 is the type
        // the encoder itself now prefers, which is the whole point of the renegotiation.
        let agreed = unsafe { transform.GetOutputAvailableType(0, 0) }.map_err(platform)?;
        // SAFETY: as above, with a type the transform just offered.
        unsafe { transform.SetOutputType(0, &agreed, 0) }.map_err(platform)?;

        // The renegotiated type is the first one that can carry a sequence header, because it is
        // the first the encoder has committed to. Taking it here saves parsing it out of the
        // bitstream later, and costs nothing when it is absent.
        if self.codec_specific_data.is_empty() {
            self.codec_specific_data =
                crate::encode::h264::read_sequence_header(&transform).unwrap_or_default();
        }
        Ok(())
    }

    /// Collects one finished access unit from the transform.
    fn collect_output(&mut self) -> Result<Option<EncodedFrame>, EncodeError> {
        let transform = self.hardware.transform().clone();
        let mut buffers = [MFT_OUTPUT_DATA_BUFFER::default(); 1];
        let mut status = 0u32;

        // Hardware transforms allocate their own output samples, so `pSample` stays null going in.
        // SAFETY: one correctly-initialised entry, and stream 0 exists on every encoder.
        match unsafe { transform.ProcessOutput(0, &mut buffers, &mut status) } {
            Ok(()) => {}
            Err(error) if error.code() == MF_E_TRANSFORM_NEED_MORE_INPUT => return Ok(None),
            Err(error) if error.code() == MF_E_TRANSFORM_STREAM_CHANGE => {
                // Not an error: the encoder is telling us it is ready, on terms it can only state
                // now. Accept them and let the next pump iteration collect the frame.
                self.renegotiate_output()?;
                return Ok(None);
            }
            Err(error) => return Err(platform(error)),
        }

        let Some(sample) = buffers[0].pSample.take() else {
            return Ok(None);
        };
        crate::encode::h264::read_sample(&sample).map(Some)
    }
}

impl VideoEncoder for HardwareH264Encoder {
    fn codec_specific_data(&self) -> &[Vec<u8>] {
        &self.codec_specific_data
    }

    fn output_size(&self) -> (u32, u32) {
        (self.config.width, self.config.height)
    }

    fn codec(&self) -> VideoCodec {
        // Fixed rather than read back from `config`: this module configures an H.264 output type
        // and nothing else, and `new` refuses any other codec outright.
        VideoCodec::H264
    }

    fn submit(
        &mut self,
        frame: &SourceFrame,
        force_key_frame: bool,
    ) -> Result<Option<EncodedFrame>, EncodeError> {
        if force_key_frame {
            // The receiver asks for one after packet loss, and this is the only thing that ends a
            // stream it can no longer decode. Ignoring the request — which this path did until the
            // priming work exposed it — leaves the picture broken until the session restarts.
            self.force_next_key_frame = true;
        }

        // A texture may arrive at the capture size: the video processor scales it to the encoder's
        // size in the same pass that converts its colour, so the session does not have to resize it
        // first. System-memory frames have no such stage and must already match.
        let is_texture = matches!(frame.data, FrameData::Texture(_));
        if !is_texture && (frame.width != self.config.width || frame.height != self.config.height) {
            return Err(EncodeError::FrameSizeChanged {
                expected: (self.config.width, self.config.height),
                actual: (frame.width, frame.height),
            });
        }

        let sample = match &frame.data {
            FrameData::Texture(texture) => self.sample_from_texture(
                texture,
                (frame.width, frame.height),
                frame.presentation_time_us,
            )?,
            FrameData::Bgra { pixels, stride } => {
                bgra_to_nv12(pixels, frame.width, frame.height, *stride, &mut self.nv12)
                    .map_err(|error| EncodeError::Platform(error.to_string()))?;
                self.sample_from_nv12_texture(frame.presentation_time_us)?
            }
        };

        // The encoder may already have asked for a frame, either during configuration or while a
        // previous tick was pumping. Waiting for another request in that case would deadlock: it
        // has no reason to ask twice for one frame.
        let mut fed = if self.input_requests > 0 {
            self.feed(&sample)?;
            self.input_requests -= 1;
            true
        } else {
            false
        };

        // The pump. A hardware transform will not take a frame until it says it wants one, and it
        // announces finished work the same way; driving it with direct calls instead produces
        // errors that read like an unsupported format.
        for _ in 0..Self::MAX_EVENTS_PER_TICK {
            // Block only while the frame still needs delivering. Once it is in, polling keeps this
            // call bounded rather than waiting on output the encoder may not have produced yet.
            let event = if fed {
                self.hardware.poll_event()?
            } else {
                self.hardware.next_event_within(Self::INPUT_TIMEOUT)?
            };

            match event {
                Some(TransformEvent::NeedInput) => {
                    // A request for input means the encoder has finished with an earlier frame.
                    self.release_oldest_in_flight();
                    if fed {
                        // The encoder is already asking for the *next* frame, which there is no
                        // frame in hand to satisfy. Bank the request. Dropping it here was this
                        // module's longest-lived bug: every later frame then waited the full input
                        // timeout for a request that had already been delivered and discarded, so
                        // the encoder looked like a driver that accepted frames and produced
                        // nothing, at a flat quarter-second each.
                        self.input_requests += 1;
                    } else {
                        self.feed(&sample)?;
                        fed = true;
                    }
                }
                Some(TransformEvent::HaveOutput) => {
                    if let Some(encoded) = self.collect_output()? {
                        self.capture_parameter_sets(&encoded);
                        self.ready.push_back(encoded);
                    }
                }
                // Nothing pending and the frame is already in. Normal while the encoder fills.
                None => break,
                Some(_) => {}
            }
        }

        // Whatever the pump gathered, oldest first, so nothing the encoder produced is dropped.
        Ok(self.ready.pop_front())
    }
}

impl Drop for HardwareH264Encoder {
    fn drop(&mut self) {
        if !self.streaming {
            return;
        }
        let transform = self.hardware.transform();
        // SAFETY: the transform is live and streaming; these are the documented teardown messages
        // and a failure here has nowhere useful to go.
        unsafe {
            let _ = transform.ProcessMessage(MFT_MESSAGE_NOTIFY_END_OF_STREAM, 0);
            let _ = transform.ProcessMessage(MFT_MESSAGE_COMMAND_FLUSH, 0);
            let _ = transform.ProcessMessage(MFT_MESSAGE_NOTIFY_END_STREAMING, 0);
        }
    }
}

/// Builds a media type for one end of the encoder.
fn build_media_type(
    subtype: &windows::core::GUID,
    config: &EncoderConfig,
    is_output: bool,
) -> Result<IMFMediaType, EncodeError> {
    let media_type = unsafe { MFCreateMediaType() }.map_err(platform)?;
    // SAFETY: the media type was just created and every key below matches its documented value
    // type.
    unsafe {
        media_type
            .SetGUID(&MF_MT_MAJOR_TYPE, &MFMediaType_Video)
            .map_err(platform)?;
        media_type
            .SetGUID(&MF_MT_SUBTYPE, subtype)
            .map_err(platform)?;
        media_type
            .SetUINT32(&MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive.0 as u32)
            .map_err(platform)?;
        media_type
            .SetUINT64(
                &MF_MT_FRAME_SIZE,
                crate::encode::h264::pack(config.width, config.height),
            )
            .map_err(platform)?;
        media_type
            .SetUINT64(
                &MF_MT_FRAME_RATE,
                crate::encode::h264::pack(config.frame_rate, 1),
            )
            .map_err(platform)?;
        media_type
            .SetUINT64(&MF_MT_PIXEL_ASPECT_RATIO, crate::encode::h264::pack(1, 1))
            .map_err(platform)?;

        if is_output {
            media_type
                .SetUINT32(&MF_MT_AVG_BITRATE, config.bitrate_bits_per_second)
                .map_err(platform)?;
        } else {
            // NV12 luma is one byte per pixel, so the stride is the width. Without this the
            // transform has to guess the row pitch and rejects frames when it guesses wrong.
            media_type
                .SetUINT32(&MF_MT_DEFAULT_STRIDE, config.width)
                .map_err(platform)?;
        }
    }
    Ok(media_type)
}

/// A hundred nanoseconds, the unit Media Foundation measures time in.
const HNS_PER_SECOND: i64 = 10_000_000;

/// `eAVEncCommonRateControlMode_CBR`: a constant bitrate, which keeps pacing predictable.
const RATE_CONTROL_CBR: u32 = 0;

/// How often the hardware path emits a key frame regardless of whether one was requested.
///
/// Two seconds is the compromise: short enough that a receiver recovering from loss is never frozen
/// for long, long enough that the bitrate cost of intra frames stays small on a link that is
/// behaving.
const KEY_FRAME_INTERVAL_SECONDS: u32 = 2;

/// The shortest key-frame interval, in frames.
const MIN_GOP_SIZE: u32 = 30;

/// The longest key-frame interval, in frames.
const MAX_GOP_SIZE: u32 = 600;

/// The key-frame interval in frames, for a given frame rate.
///
/// Clamped at both ends. A frame rate of zero would otherwise produce a GOP of zero, which encoders
/// disagree about the meaning of — some read it as "every frame is an IDR", others as "never" — and
/// a very high frame rate would push recovery after packet loss out to several seconds.
#[must_use]
fn gop_size(frame_rate: u32) -> u32 {
    frame_rate
        .saturating_mul(KEY_FRAME_INTERVAL_SECONDS)
        .clamp(MIN_GOP_SIZE, MAX_GOP_SIZE)
}

#[cfg(test)]
#[path = "h264_hardware_tests.rs"]
mod tests;

#[cfg(test)]
#[path = "h264_hardware_live_report.rs"]
mod live_report;

#[cfg(test)]
#[path = "h264_hardware_live_encoding.rs"]
mod live_encoding;
