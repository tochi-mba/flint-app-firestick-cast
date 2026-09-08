//! Hardware video encoder discovery.
//!
//! Encoders are proven, never inferred. A present GPU does not imply a present encoder: support
//! varies by silicon generation, by codec, and by driver version, and a virtualised or remoted
//! session may expose none at all.
//!
//! Media Foundation's transform registry is used rather than each vendor's own SDK. It reports
//! NVENC, Quick Sync and AMF through one interface, needs no vendor toolkit installed on the build
//! machine, and answers the question the capability report actually asks: what will this machine
//! let Flint encode with right now.

use crate::encode::{EncoderVendor, HostEncoder, VideoCodec};

/// Vendor-neutral view of the encoders this host has.
#[derive(Debug, Default, Clone, PartialEq, Eq)]
pub struct EncoderInventory {
    /// Every hardware encoder the probe confirmed.
    pub encoders: Vec<HostEncoder>,
}

impl EncoderInventory {
    /// Whether any encoder can serve a live session.
    #[must_use]
    pub fn has_session_capable(&self) -> bool {
        self.encoders.iter().any(HostEncoder::is_session_capable)
    }

    /// The encoders able to produce `codec`.
    #[must_use]
    pub fn supporting(&self, codec: VideoCodec) -> Vec<&HostEncoder> {
        self.encoders
            .iter()
            .filter(|encoder| encoder.supports(codec))
            .collect()
    }
}

/// Classifies an encoder by the name its driver registers.
///
/// Vendor detection from a transform's friendly name is a heuristic, and it is only ever used to
/// label an encoder that Media Foundation has already confirmed exists. It never creates an entry,
/// so a name this does not recognise costs a label, not a capability.
#[must_use]
pub fn vendor_from_name(name: &str) -> EncoderVendor {
    let lowered = name.to_ascii_lowercase();

    if lowered.contains("nvidia") || lowered.contains("nvenc") {
        EncoderVendor::Nvenc
    } else if lowered.contains("intel") || lowered.contains("quick sync") || lowered.contains("qsv")
    {
        EncoderVendor::QuickSync
    } else if lowered.contains("amd") || lowered.contains("radeon") || lowered.contains("amf") {
        EncoderVendor::Amf
    } else {
        EncoderVendor::Unknown
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn vendor_is_read_from_the_registered_transform_name() {
        assert_eq!(
            vendor_from_name("NVIDIA H.264 Encoder MFT"),
            EncoderVendor::Nvenc
        );
        assert_eq!(
            vendor_from_name("Intel(R) Quick Sync Video H.264 Encoder MFT"),
            EncoderVendor::QuickSync
        );
        assert_eq!(
            vendor_from_name("AMD H.264 Hardware MFT Encoder"),
            EncoderVendor::Amf
        );
    }

    #[test]
    fn vendor_detection_is_case_insensitive() {
        assert_eq!(
            vendor_from_name("nvidia hevc encoder"),
            EncoderVendor::Nvenc
        );
        assert_eq!(
            vendor_from_name("INTEL QSV ENCODER"),
            EncoderVendor::QuickSync
        );
    }

    #[test]
    fn an_unrecognised_name_is_unknown_rather_than_guessed() {
        // A wrong vendor label would send the capture strategy to the wrong adapter.
        assert_eq!(
            vendor_from_name("Some Other Encoder"),
            EncoderVendor::Unknown
        );
        assert_eq!(vendor_from_name(""), EncoderVendor::Unknown);
    }

    #[test]
    fn an_empty_inventory_reports_no_capability() {
        let inventory = EncoderInventory::default();
        assert!(!inventory.has_session_capable());
        assert!(inventory.supporting(VideoCodec::H264).is_empty());
    }

    #[test]
    fn supporting_filters_by_codec() {
        let inventory = EncoderInventory {
            encoders: vec![
                HostEncoder::new(
                    EncoderVendor::Nvenc,
                    1,
                    vec![VideoCodec::H264, VideoCodec::H265],
                ),
                HostEncoder::new(EncoderVendor::QuickSync, 2, vec![VideoCodec::H264]),
            ],
        };

        assert_eq!(inventory.supporting(VideoCodec::H264).len(), 2);
        assert_eq!(inventory.supporting(VideoCodec::H265).len(), 1);
        assert!(inventory.supporting(VideoCodec::Av1).is_empty());
    }

    #[test]
    fn an_unknown_vendor_is_still_session_capable_when_it_has_a_codec() {
        // Media Foundation proved the encoder exists. Not recognising its name is Flint's gap,
        // not the hardware's, so the encoder is kept and merely labelled unknown.
        let inventory = EncoderInventory {
            encoders: vec![HostEncoder::new(
                EncoderVendor::Unknown,
                1,
                vec![VideoCodec::H264],
            )],
        };

        assert!(inventory.has_session_capable());
    }
}
