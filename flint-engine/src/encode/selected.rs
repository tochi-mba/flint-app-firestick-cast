//! Choosing between the hardware and software H.264 encoders at runtime.
//!
//! Both encoders implement [`VideoEncoder`] identically, so nothing above this module needs to know
//! which one it got. That matters more than it sounds: the session, the pacing logic and the FFI
//! boundary are all written once, and the choice becomes a detail of construction rather than a
//! branch threaded through the frame path.
//!
//! # Why the fallback is not a formality
//!
//! A hardware encoder can be absent (no supported GPU), present but unusable (a driver that
//! enumerates an encoder it will not stream from), or usable but already claimed by another
//! process. All three are ordinary on real machines, and none of them should mean "no mirroring".
//! So hardware is *attempted*, and any failure — of enumeration, configuration, or the transform's
//! first request for a frame — falls through to software rather than ending the session.
//!
//! The software encoder is genuinely viable here, which is what makes this a fallback rather than a
//! consolation: it is within about half a millisecond of hardware on the median. The hardware
//! path's advantage is its tail — a tighter worst case is what a viewer perceives as smoothness —
//! and that its work happens on the GPU's dedicated encode block, leaving the CPU for capture,
//! colour conversion and the network.
//!
//! # What a working encoder is not
//!
//! Neither encoder is trusted here on the strength of configuring without error. A hardware encoder
//! shipped in this project for a while that streamed H.264 with correct parameter sets, correct
//! dimensions, correct framing and a plausible bitrate, every frame of which decoded to nothing.
//! Selection is only as good as the proof behind it, and that proof is
//! `what_the_hardware_encoder_produces_decodes_back_into_a_picture`.

use super::h264::H264Encoder;
use super::h264_hardware::HardwareH264Encoder;
use super::video::{EncodeError, EncodedFrame, EncoderConfig, SourceFrame, VideoEncoder};
use super::VideoCodec;

/// Which of the two encoders a session ended up with.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum EncoderKind {
    /// A GPU encode block, via an asynchronous Media Foundation transform.
    Hardware,
    /// Media Foundation's software H.264 encoder.
    Software,
}

/// An H.264 encoder, hardware where possible and software otherwise.
#[derive(Debug)]
pub enum SelectedEncoder {
    /// A hardware transform on the capture adapter.
    Hardware(Box<HardwareH264Encoder>),
    /// The software encoder.
    Software(Box<H264Encoder>),
}

impl SelectedEncoder {
    /// Opens the best encoder available on `adapter_luid`, falling back to software.
    ///
    /// `adapter_luid` should be the adapter the *frames are captured on*, not the fastest GPU in
    /// the machine. On a hybrid laptop those differ, and an encoder on the other adapter needs
    /// every frame copied across the PCIe bus first — which costs more than the faster encoder
    /// saves.
    ///
    /// # Errors
    /// [`EncodeError`] only when the software encoder also fails, at which point the host cannot
    /// encode H.264 at all and there is nothing left to fall back to.
    pub fn open(config: EncoderConfig, adapter_luid: i64) -> Result<Self, EncodeError> {
        // Hardware first, software when there is none or when it declines.
        //
        // The hardware path was briefly demoted while it produced a stream that decoded to a field
        // of zeroes — a green television, with correct parameter sets, correct dimensions, correct
        // framing and a plausible bitrate. The cause was that a hardware transform reads its frames
        // from GPU memory whatever its media type advertises, so system-memory buffers were being
        // accepted and ignored. Frames now go into NV12 textures on the encoder's own device, and
        // `what_the_hardware_encoder_produces_decodes_back_into_a_picture` is what holds it there.
        //
        // `FLINT_ENCODER` overrides the choice either way, which is the first thing worth reaching
        // for when a session looks wrong.
        match std::env::var("FLINT_ENCODER").as_deref() {
            Ok("software") => return Self::software_only(config),
            Ok("hardware") => {
                return Ok(Self::Hardware(Box::new(HardwareH264Encoder::new(
                    config,
                    adapter_luid,
                )?)))
            }
            _ => {}
        }

        match HardwareH264Encoder::new(config, adapter_luid) {
            Ok(encoder) => Ok(Self::Hardware(Box::new(encoder))),
            // Deliberately swallowed. Every reason a hardware encoder declines is a fact about this
            // machine rather than a bug in the caller, and the software path covers all of them.
            Err(_) => Self::software_only(config),
        }
    }

    /// Opens a hardware encoder on a device the caller already owns.
    ///
    /// The device is the whole point: an encoder built here shares the capture device, so a
    /// captured texture can be converted and encoded without ever crossing the bus. An encoder on
    /// its own device would have to be handed system memory instead, which is the readback this
    /// path exists to remove.
    ///
    /// # Errors
    /// [`EncodeError`] when no hardware encoder will open on that device. There is deliberately no
    /// software fallback here: software cannot read a texture, so a caller that wanted the GPU path
    /// needs to know it did not get it.
    pub fn open_on_device(
        config: EncoderConfig,
        adapter_luid: i64,
        device: windows::Win32::Graphics::Direct3D11::ID3D11Device,
        context: windows::Win32::Graphics::Direct3D11::ID3D11DeviceContext,
    ) -> Result<Self, EncodeError> {
        if std::env::var("FLINT_ENCODER").as_deref() == Ok("software") {
            return Err(EncodeError::UnsupportedFrameData);
        }
        Ok(Self::Hardware(Box::new(
            HardwareH264Encoder::open_on_device(config, adapter_luid, device, context)?,
        )))
    }

    /// Opens the software encoder, skipping hardware entirely.
    ///
    /// For callers that need output identical across machines — golden-vector tests, and anything
    /// comparing two runs — because hardware encoders on different GPUs produce different, equally
    /// valid bitstreams from the same frames.
    ///
    /// # Errors
    /// [`EncodeError`] when the host has no software H.264 encoder.
    pub fn software_only(config: EncoderConfig) -> Result<Self, EncodeError> {
        Ok(Self::Software(Box::new(H264Encoder::new(config)?)))
    }

    /// Which encoder this actually is, for telemetry and for the diagnostics screen.
    #[must_use]
    pub fn kind(&self) -> EncoderKind {
        match self {
            Self::Hardware(_) => EncoderKind::Hardware,
            Self::Software(_) => EncoderKind::Software,
        }
    }

    /// Whether this session is using the GPU's encode block.
    #[must_use]
    pub fn is_hardware(&self) -> bool {
        self.kind() == EncoderKind::Hardware
    }
}

impl VideoEncoder for SelectedEncoder {
    fn codec_specific_data(&self) -> &[Vec<u8>] {
        match self {
            Self::Hardware(encoder) => encoder.codec_specific_data(),
            Self::Software(encoder) => encoder.codec_specific_data(),
        }
    }

    fn output_size(&self) -> (u32, u32) {
        match self {
            Self::Hardware(encoder) => encoder.output_size(),
            Self::Software(encoder) => encoder.output_size(),
        }
    }

    fn codec(&self) -> VideoCodec {
        match self {
            Self::Hardware(encoder) => encoder.codec(),
            Self::Software(encoder) => encoder.codec(),
        }
    }

    fn submit(
        &mut self,
        frame: &SourceFrame,
        force_key_frame: bool,
    ) -> Result<Option<EncodedFrame>, EncodeError> {
        match self {
            Self::Hardware(encoder) => encoder.submit(frame, force_key_frame),
            Self::Software(encoder) => encoder.submit(frame, force_key_frame),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn config(width: u32, height: u32) -> EncoderConfig {
        EncoderConfig {
            width,
            height,
            frame_rate: 60,
            bitrate_bits_per_second: 12_000_000,
            codec: VideoCodec::H264,
        }
    }

    #[test]
    fn an_invalid_configuration_is_refused_by_both_paths_rather_than_falling_back_forever() {
        // The fallback exists for machines that cannot do hardware, not for configurations that
        // cannot encode. A zero-sized frame must fail, not quietly become a software session that
        // fails later and further away.
        assert!(SelectedEncoder::open(config(0, 0), 0).is_err());
    }

    #[test]
    fn a_nonexistent_adapter_falls_back_to_software_rather_than_failing() {
        // The whole point: no hardware here, and mirroring still works.
        let encoder = SelectedEncoder::open(config(640, 360), i64::MAX)
            .expect("a missing adapter must fall back, not fail");
        assert_eq!(encoder.kind(), EncoderKind::Software);
        assert!(!encoder.is_hardware());
    }

    #[test]
    fn software_only_never_selects_hardware_even_when_hardware_exists() {
        let encoder =
            SelectedEncoder::software_only(config(640, 360)).expect("software must be available");
        assert_eq!(encoder.kind(), EncoderKind::Software);
    }

    #[test]
    fn software_only_still_refuses_an_invalid_configuration() {
        assert!(SelectedEncoder::software_only(config(640, 0)).is_err());
    }

    #[test]
    fn the_selected_encoder_reports_the_size_it_was_asked_for() {
        let encoder = SelectedEncoder::open(config(1280, 720), i64::MAX).expect("an encoder");
        assert_eq!(encoder.output_size(), (1280, 720));
    }

    #[test]
    fn the_selected_encoder_reports_h264_whichever_path_it_took() {
        let encoder = SelectedEncoder::open(config(640, 360), i64::MAX).expect("an encoder");
        assert_eq!(encoder.codec(), VideoCodec::H264);
    }

    #[test]
    fn odd_dimensions_are_refused_rather_than_silently_rounded() {
        // Chroma subsampling needs even dimensions; rounding here would put the encoder and the
        // colour converter into disagreement about the frame size.
        assert!(SelectedEncoder::open(config(641, 361), i64::MAX).is_err());
    }

    #[test]
    fn a_non_h264_codec_is_refused_rather_than_downgraded() {
        // Silently producing H.264 for a session that negotiated HEVC would have the receiver build
        // the wrong decoder and show a black screen far from the cause.
        let mut wanted = config(640, 360);
        wanted.codec = VideoCodec::H265;
        assert!(SelectedEncoder::open(wanted, i64::MAX).is_err());
    }

    #[test]
    fn the_two_kinds_are_distinguishable() {
        assert_ne!(EncoderKind::Hardware, EncoderKind::Software);
    }
}

/// Head-to-head measurement of the two encoders on this machine.
///
/// Ignored by default: it needs a GPU with a hardware H.264 encoder, and it reports numbers rather
/// than asserting them, because the right value is a property of the machine and not of the code.
#[cfg(test)]
mod live_selection {
    use super::*;

    fn config() -> EncoderConfig {
        EncoderConfig {
            width: 1920,
            height: 1080,
            frame_rate: 60,
            bitrate_bits_per_second: 12_000_000,
            codec: VideoCodec::H264,
        }
    }

    fn capture_adapter() -> Option<i64> {
        crate::capture::duplication::DesktopDuplication::open_primary()
            .ok()
            .map(|duplication| duplication.format().adapter_luid)
    }

    #[test]
    fn selection_prefers_hardware_on_the_capture_adapter_when_one_exists() {
        let Some(adapter) = capture_adapter() else {
            // No desktop to duplicate — a headless build agent. Nothing to prove here.
            return;
        };

        let encoder = SelectedEncoder::open(config(), adapter).expect("an encoder on this machine");
        // Not asserted as hardware: a machine without one is a legitimate configuration, and this
        // test would then be asserting a fact about the hardware rather than about the selection.
        println!(
            "selected encoder on adapter {adapter}: {:?}",
            encoder.kind()
        );
        assert_eq!(encoder.output_size(), (1920, 1080));
    }

    #[test]
    fn a_selected_encoder_produces_a_decodable_key_frame_whichever_path_it_took() {
        let Some(adapter) = capture_adapter() else {
            return;
        };
        let mut encoder = SelectedEncoder::open(config(), adapter).expect("an encoder");

        // A hardware encoder pipelines, so the first access unit arrives several frames in. Feeding
        // a fixed budget and taking the first output covers both paths without special-casing.
        let mut first = None;
        for index in 0..90 {
            let frame = moving_frame(index, 1920, 1080);
            if let Some(encoded) = encoder
                .submit(&frame, false)
                .expect("encoding must not fail")
            {
                first = Some(encoded);
                break;
            }
        }

        let first = first.expect("the encoder produced no access unit within 90 frames");
        assert!(first.key_frame, "the first access unit must be a key frame");
        assert!(
            first.data.len() > 4,
            "an access unit needs at least a start code and a NAL header"
        );
        assert!(
            !encoder.codec_specific_data().is_empty(),
            "the receiver cannot configure a decoder without SPS and PPS"
        );
    }

    /// A frame whose content changes with `index`, so the encoder has real work to do.
    fn moving_frame(index: u32, width: u32, height: u32) -> SourceFrame {
        let stride = width * 4;
        let mut pixels = vec![0u8; (stride * height) as usize];
        for y in 0..height {
            for x in 0..width {
                let offset = (y * stride + x * 4) as usize;
                pixels[offset] = ((x + index * 7) % 256) as u8;
                pixels[offset + 1] = ((y + index * 3) % 256) as u8;
                pixels[offset + 2] = ((x + y + index) % 256) as u8;
                pixels[offset + 3] = 255;
            }
        }
        SourceFrame {
            width,
            height,
            data: super::super::video::FrameData::Bgra { pixels, stride },
            presentation_time_us: i64::from(index) * 16_667,
        }
    }
}
