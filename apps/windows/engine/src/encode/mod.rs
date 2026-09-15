//! Encoder model and discovery.

pub mod caps;
pub mod video;

#[cfg(windows)]
pub mod d3d_manager;
#[cfg(windows)]
pub mod h264;
#[cfg(windows)]
pub mod h264_hardware;
#[cfg(windows)]
pub mod h264_hardware_transform;
#[cfg(windows)]
pub mod mediafoundation;
#[cfg(windows)]
pub mod nv12_texture;
#[cfg(windows)]
pub mod selected;
pub mod sps;

/// A video codec.
///
/// The discriminants match the REX wire protocol's `CodecId`, so a number means the same thing in
/// the engine, in the C# shell and on the receiver.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash)]
#[repr(u16)]
pub enum VideoCodec {
    /// H.264 / AVC. The widest Fire TV decoder support; the default.
    H264 = 1,
    /// H.265 / HEVC. Better compression, less uniform decoder support.
    H265 = 2,
    /// AV1. Best compression; decoder support on Fire TV hardware is rare.
    Av1 = 5,
}

/// The hardware family behind an encoder.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash)]
#[repr(u8)]
pub enum EncoderVendor {
    /// NVIDIA NVENC.
    Nvenc = 0,
    /// AMD Advanced Media Framework.
    Amf = 1,
    /// Intel Quick Sync Video.
    QuickSync = 2,
    /// A hardware encoder whose registered name Flint does not recognise.
    ///
    /// Still usable: the platform proved it exists. Only the vendor label is missing.
    Unknown = 3,
}

/// A hardware encoder this host actually has.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct HostEncoder {
    /// The encoder family.
    pub vendor: EncoderVendor,
    /// The adapter this encoder belongs to, matching the DXGI adapter LUID.
    pub adapter_luid: i64,
    /// Codecs the probe confirmed. Sorted and deduplicated on construction.
    pub codecs: Vec<VideoCodec>,
}

impl HostEncoder {
    /// Builds an encoder record, normalising its codec list.
    #[must_use]
    pub fn new(vendor: EncoderVendor, adapter_luid: i64, mut codecs: Vec<VideoCodec>) -> Self {
        codecs.sort_unstable();
        codecs.dedup();
        Self {
            vendor,
            adapter_luid,
            codecs,
        }
    }

    /// Whether this encoder is fit for a live session.
    ///
    /// Every variant here is hardware, so the only disqualifier is having no confirmed codec.
    /// Software encoding is not represented at all: it cannot meet the latency budget, so it is
    /// never offered rather than being offered and rejected later.
    #[must_use]
    pub fn is_session_capable(&self) -> bool {
        !self.codecs.is_empty()
    }

    /// Whether the probe confirmed a specific codec.
    #[must_use]
    pub fn supports(&self, codec: VideoCodec) -> bool {
        self.codecs.contains(&codec)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn codec_discriminants_match_the_rex_wire_protocol() {
        // Host and receiver must never disagree about what a codec number means.
        assert_eq!(VideoCodec::H264 as u16, 1);
        assert_eq!(VideoCodec::H265 as u16, 2);
        assert_eq!(VideoCodec::Av1 as u16, 5);
    }

    #[test]
    fn new_sorts_and_deduplicates_codecs() {
        let encoder = HostEncoder::new(
            EncoderVendor::Nvenc,
            1,
            vec![VideoCodec::H265, VideoCodec::H264, VideoCodec::H265],
        );

        assert_eq!(encoder.codecs, vec![VideoCodec::H264, VideoCodec::H265]);
    }

    #[test]
    fn an_encoder_with_no_codecs_is_not_session_capable() {
        let encoder = HostEncoder::new(EncoderVendor::Nvenc, 1, vec![]);
        assert!(!encoder.is_session_capable());
    }

    #[test]
    fn supports_reports_only_confirmed_codecs() {
        let encoder = HostEncoder::new(EncoderVendor::Amf, 7, vec![VideoCodec::H264]);
        assert!(encoder.supports(VideoCodec::H264));
        assert!(!encoder.supports(VideoCodec::Av1));
    }
}
