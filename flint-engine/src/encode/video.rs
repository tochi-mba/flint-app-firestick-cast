//! The encoder contract every mirror session runs through.
//!
//! Capture hands frames in, encoded access units come out, and the session pushes those onto the
//! wire. Keeping that behind a trait is what lets the whole pipeline — session state, pacing,
//! key-frame requests, transport — be tested on a machine with no GPU and no encoder at all, which
//! is most CI machines and every developer who is not sitting at the Windows box.

use super::VideoCodec;

/// A frame handed to an encoder.
///
/// Desktop duplication produces a GPU texture and a hardware encoder consumes one directly, so the
/// real path never copies to system memory. The packed variant exists for the null encoder and for
/// tests, which have no device to make a texture on.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum FrameData {
    /// Tightly packed BGRA8 pixels, `stride` bytes per row.
    Bgra {
        /// Pixel bytes, at least `stride * height` long.
        pixels: Vec<u8>,
        /// Bytes per row, which may exceed `width * 4` when the source is padded.
        stride: u32,
    },
    /// A BGRA texture still on the GPU, where capture produced it.
    ///
    /// The fast path. A frame that stays on the GPU skips a readback, a colour conversion on the
    /// processor and an upload — about three milliseconds a frame at 1080p on this project's
    /// development machine, against a sixteen millisecond budget, and more than the encoder itself
    /// costs. An encoder that cannot take a texture should answer
    /// [`EncodeError::UnsupportedFrameData`] so the caller can fall back rather than guess.
    #[cfg(windows)]
    Texture(windows::Win32::Graphics::Direct3D11::ID3D11Texture2D),
}

/// One frame offered to an encoder.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SourceFrame {
    /// Frame width in pixels.
    pub width: u32,
    /// Frame height in pixels.
    pub height: u32,
    /// When this frame should be shown, relative to the start of the session.
    pub presentation_time_us: i64,
    /// The pixels themselves.
    pub data: FrameData,
}

/// One encoded access unit, ready to be framed onto the wire.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct EncodedFrame {
    /// The encoded bytes for this access unit.
    pub data: Vec<u8>,
    /// The presentation time carried through from the source frame.
    pub presentation_time_us: i64,
    /// Whether a decoder can start here without any earlier frame.
    pub key_frame: bool,
}

/// What a session asks an encoder to produce.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct EncoderConfig {
    /// Output width in pixels.
    pub width: u32,
    /// Output height in pixels.
    pub height: u32,
    /// Target frames per second.
    pub frame_rate: u32,
    /// Target bitrate. Adaptive control adjusts this from receiver statistics.
    pub bitrate_bits_per_second: u32,
    /// The codec both ends agreed on during the handshake.
    pub codec: VideoCodec,
}

impl EncoderConfig {
    /// Whether this configuration describes something an encoder could actually produce.
    ///
    /// Guards the session against asking for a zero-sized or zero-rate stream, which encoders
    /// report in wildly different ways — some fail at configure time, some accept it and then
    /// produce nothing at all, which looks identical to a hung pipeline from the outside.
    #[must_use]
    pub fn is_valid(&self) -> bool {
        self.width > 0
            && self.height > 0
            && self.width % 2 == 0
            && self.height % 2 == 0
            && self.frame_rate > 0
            && self.bitrate_bits_per_second > 0
    }
}

/// Why encoding could not start or continue.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum EncodeError {
    /// The configuration could not produce a stream; see [`EncoderConfig::is_valid`].
    InvalidConfig,
    /// A frame's dimensions did not match what the encoder was configured for.
    ///
    /// Recoverable by reconfiguring: a resolution change mid-session lands here rather than
    /// corrupting the stream, because an encoder fed a differently-sized frame emits access units
    /// no decoder downstream can make sense of.
    FrameSizeChanged {
        /// What the encoder was configured to accept.
        expected: (u32, u32),
        /// What the frame actually carried.
        actual: (u32, u32),
    },
    /// This encoder cannot take frames in the form it was offered.
    ///
    /// Not a failure of the frame or of the encoder, but of the pairing: the GPU texture path is
    /// available only on a hardware encoder sharing the capture device, and every other combination
    /// has to go through system memory. Reported so the caller can fall back deliberately rather
    /// than discovering it as a corrupt stream.
    UnsupportedFrameData,
    /// The platform refused, with its own message.
    Platform(String),
}

impl std::fmt::Display for EncodeError {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::UnsupportedFrameData => write!(
                formatter,
                "this encoder cannot accept frames in the form they were offered"
            ),
            Self::InvalidConfig => write!(
                formatter,
                "the encoder configuration cannot produce a stream"
            ),
            Self::FrameSizeChanged { expected, actual } => write!(
                formatter,
                "frame size changed from {}x{} to {}x{}",
                expected.0, expected.1, actual.0, actual.1
            ),
            Self::Platform(message) => write!(formatter, "{message}"),
        }
    }
}

impl std::error::Error for EncodeError {}

/// Turns captured frames into encoded access units.
pub trait VideoEncoder {
    /// Codec setup data the receiver needs before the first access unit.
    ///
    /// For H.264 this is SPS and PPS. It is available only once the encoder has produced its first
    /// key frame, because that is when the encoder itself decides the parameters.
    fn codec_specific_data(&self) -> &[Vec<u8>];

    /// The pixel size this encoder was configured to accept.
    fn output_size(&self) -> (u32, u32);

    /// The codec this encoder actually produces.
    ///
    /// Asked of the encoder rather than assumed from the handshake. A session that negotiated
    /// HEVC and then received H.264 would have the receiver build an HEVC decoder and fail on the
    /// first access unit — a black screen whose cause is nowhere near where it was introduced.
    fn codec(&self) -> VideoCodec;

    /// Offers one frame, returning an access unit when the encoder produces one.
    ///
    /// Returning `Ok(None)` is normal: an encoder may buffer a frame before emitting anything.
    ///
    /// # Errors
    /// See [`EncodeError`].
    fn submit(
        &mut self,
        frame: &SourceFrame,
        force_key_frame: bool,
    ) -> Result<Option<EncodedFrame>, EncodeError>;
}

/// An encoder that produces deterministic, decodable-by-nobody output.
///
/// It exists so the session, pacing, key-frame and transport logic can be exercised without a GPU.
/// It never claims to be a real encoder: [`super::HostEncoder::is_session_capable`] deliberately
/// has no software variant, so this is reachable only from tests and never offered to a user as a
/// way to mirror a screen.
#[derive(Debug)]
pub struct NullEncoder {
    config: EncoderConfig,
    codec_specific_data: Vec<Vec<u8>>,
    has_encoded_frame: bool,
}

impl NullEncoder {
    /// Builds a null encoder.
    ///
    /// # Errors
    /// [`EncodeError::InvalidConfig`] when the configuration could not produce a stream.
    pub fn new(config: EncoderConfig) -> Result<Self, EncodeError> {
        if !config.is_valid() {
            return Err(EncodeError::InvalidConfig);
        }

        Ok(Self {
            config,
            // Stand-ins for SPS and PPS: the shapes the wire format and the receiver expect, so
            // the transport path can be exercised without inventing them at each call site.
            codec_specific_data: vec![vec![0, 0, 0, 1, 0x67], vec![0, 0, 0, 1, 0x68]],
            has_encoded_frame: false,
        })
    }

    /// The configuration this encoder was built with.
    #[must_use]
    pub fn config(&self) -> EncoderConfig {
        self.config
    }
}

impl VideoEncoder for NullEncoder {
    fn codec_specific_data(&self) -> &[Vec<u8>] {
        &self.codec_specific_data
    }

    fn output_size(&self) -> (u32, u32) {
        (self.config.width, self.config.height)
    }

    fn codec(&self) -> VideoCodec {
        self.config.codec
    }

    fn submit(
        &mut self,
        frame: &SourceFrame,
        force_key_frame: bool,
    ) -> Result<Option<EncodedFrame>, EncodeError> {
        if frame.width != self.config.width || frame.height != self.config.height {
            return Err(EncodeError::FrameSizeChanged {
                expected: (self.config.width, self.config.height),
                actual: (frame.width, frame.height),
            });
        }

        // The real mirror uses an unbounded GOP: the opening frame is independently decodable,
        // and every later IDR is driven by receiver feedback rather than a timer.
        let key_frame = force_key_frame || !self.has_encoded_frame;
        self.has_encoded_frame = true;

        // The null encoder exists to exercise the session without a GPU, so it has nothing to do
        // with a texture and says so rather than inventing a length.
        let FrameData::Bgra { pixels, .. } = &frame.data else {
            return Err(EncodeError::UnsupportedFrameData);
        };
        Ok(Some(EncodedFrame {
            // Not a real bitstream, and deliberately so — a length that tracks the input is enough
            // for pacing and transport tests, and anything more would invite mistaking this for an
            // encoder that could actually feed a decoder.
            data: vec![if key_frame { 0x65 } else { 0x41 }; pixels.len().clamp(1, 1024)],
            presentation_time_us: frame.presentation_time_us,
            key_frame,
        }))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn config() -> EncoderConfig {
        EncoderConfig {
            width: 1920,
            height: 1080,
            frame_rate: 30,
            bitrate_bits_per_second: 12_000_000,
            codec: VideoCodec::H264,
        }
    }

    fn frame(width: u32, height: u32, presentation_time_us: i64) -> SourceFrame {
        SourceFrame {
            width,
            height,
            presentation_time_us,
            data: FrameData::Bgra {
                pixels: vec![0u8; (width * height * 4) as usize],
                stride: width * 4,
            },
        }
    }

    #[test]
    fn a_zero_sized_or_zero_rate_configuration_is_refused() {
        assert!(!EncoderConfig {
            width: 0,
            ..config()
        }
        .is_valid());
        assert!(!EncoderConfig {
            height: 0,
            ..config()
        }
        .is_valid());
        assert!(!EncoderConfig {
            frame_rate: 0,
            ..config()
        }
        .is_valid());
        assert!(!EncoderConfig {
            bitrate_bits_per_second: 0,
            ..config()
        }
        .is_valid());
    }

    #[test]
    fn odd_dimensions_are_refused_because_chroma_subsampling_needs_even_ones() {
        assert!(!EncoderConfig {
            width: 1921,
            ..config()
        }
        .is_valid());
        assert!(!EncoderConfig {
            height: 1081,
            ..config()
        }
        .is_valid());
    }

    #[test]
    fn building_an_encoder_from_an_invalid_configuration_fails_rather_than_producing_nothing() {
        // An encoder that accepts a bad config and then silently emits no frames is
        // indistinguishable from a hung pipeline, which is the harder bug to find.
        let result = NullEncoder::new(EncoderConfig {
            width: 0,
            ..config()
        });
        assert_eq!(result.unwrap_err(), EncodeError::InvalidConfig);
    }

    #[test]
    fn the_first_frame_is_always_a_key_frame() {
        // A receiver joining a stream cannot render anything until one arrives.
        let mut encoder = NullEncoder::new(config()).unwrap();
        let encoded = encoder
            .submit(&frame(1920, 1080, 0), false)
            .unwrap()
            .unwrap();
        assert!(encoded.key_frame);
    }

    #[test]
    fn later_frames_are_not_key_frames_unless_asked_for() {
        let mut encoder = NullEncoder::new(config()).unwrap();
        encoder.submit(&frame(1920, 1080, 0), false).unwrap();

        let second = encoder
            .submit(&frame(1920, 1080, 33_333), false)
            .unwrap()
            .unwrap();
        assert!(!second.key_frame);

        let forced = encoder
            .submit(&frame(1920, 1080, 66_666), true)
            .unwrap()
            .unwrap();
        assert!(forced.key_frame);
    }

    #[test]
    fn presentation_time_passes_through_untouched() {
        let mut encoder = NullEncoder::new(config()).unwrap();
        let encoded = encoder
            .submit(&frame(1920, 1080, 1_234_567), false)
            .unwrap()
            .unwrap();
        assert_eq!(encoded.presentation_time_us, 1_234_567);
    }

    #[test]
    fn a_resolution_change_is_reported_rather_than_silently_corrupting_the_stream() {
        let mut encoder = NullEncoder::new(config()).unwrap();
        encoder.submit(&frame(1920, 1080, 0), false).unwrap();

        let error = encoder
            .submit(&frame(1280, 720, 33_333), false)
            .unwrap_err();

        assert_eq!(
            error,
            EncodeError::FrameSizeChanged {
                expected: (1920, 1080),
                actual: (1280, 720),
            }
        );
        assert!(error.to_string().contains("1920x1080"));
        assert!(error.to_string().contains("1280x720"));
    }

    #[test]
    fn codec_specific_data_is_available_for_the_video_config_message() {
        // The receiver cannot configure its decoder without these, so a session must be able to
        // read them before it sends the first access unit.
        let encoder = NullEncoder::new(config()).unwrap();
        assert_eq!(encoder.codec_specific_data().len(), 2);
        assert!(encoder
            .codec_specific_data()
            .iter()
            .all(|block| !block.is_empty()));
    }

    #[test]
    fn key_frames_are_not_inserted_periodically() {
        // Healthy links use an unbounded GOP. A receiver that loses its reference chain requests
        // the next IDR explicitly instead of paying a recurring bandwidth and latency cost.
        let mut encoder = NullEncoder::new(config()).unwrap();
        let source = frame(1920, 1080, 0);
        let mut key_frames = 0;
        for _ in 0..300 {
            let encoded = encoder.submit(&source, false).unwrap().unwrap();
            if encoded.key_frame {
                key_frames += 1;
            }
        }

        assert_eq!(key_frames, 1, "saw a periodic key frame");

        let requested = encoder.submit(&source, true).unwrap().unwrap();
        assert!(requested.key_frame);

        let after_request = encoder.submit(&source, false).unwrap().unwrap();
        assert!(!after_request.key_frame);
    }
}
