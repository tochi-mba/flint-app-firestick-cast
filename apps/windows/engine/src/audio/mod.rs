//! Sound with screen sharing: capturing what this PC plays and encoding it for the TV.

pub mod aac;
mod capture;
pub mod endpoint;
#[cfg(test)]
pub(crate) mod fake_outputs;
mod follow;
pub mod ring;
pub mod session;

/// Why sound could not be captured or encoded.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum AudioError {
    /// This edition of Windows has no AAC encoder, as the N editions without the Media Feature
    /// Pack do not.
    NoEncoder,
    /// The data rate is not one Windows' encoder offers.
    UnsupportedBitrate(u32),
    /// This PC has no sound output at all.
    NoOutput,
    /// The named sound output is not connected.
    DeviceMissing,
    /// The sound output in use went away.
    DeviceLost,
    /// Windows refused, with its own message.
    Platform(String),
}

impl std::fmt::Display for AudioError {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::NoEncoder => write!(
                formatter,
                "this edition of Windows has no AAC encoder; installing the Media Feature Pack adds one"
            ),
            Self::UnsupportedBitrate(kbps) => write!(formatter, "{kbps} kbps is not an AAC rate Windows offers"),
            Self::NoOutput => write!(formatter, "this PC has no sound output"),
            Self::DeviceMissing => write!(formatter, "the chosen sound output is not connected"),
            Self::DeviceLost => write!(formatter, "the sound output was disconnected"),
            Self::Platform(message) => write!(formatter, "{message}"),
        }
    }
}

impl std::error::Error for AudioError {}

#[cfg(test)]
mod spike_tests;

#[cfg(test)]
mod tests {
    use super::AudioError;

    #[test]
    fn every_reason_reads_as_words() {
        for (error, words) in [
            (AudioError::NoEncoder, "Media Feature Pack"),
            (AudioError::UnsupportedBitrate(100), "100 kbps"),
            (AudioError::NoOutput, "no sound output"),
            (AudioError::DeviceMissing, "not connected"),
            (AudioError::DeviceLost, "disconnected"),
            (
                AudioError::Platform("the driver said no".into()),
                "the driver said no",
            ),
        ] {
            assert!(error.to_string().contains(words), "{error:?}");
        }
    }
}
