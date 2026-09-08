//! Encoding and decoding of the REX wire format.
//!
//! # Frame layout
//!
//! ```text
//! u32  body length          (big-endian, counts the envelope and payload)
//! u16  magic   = 0x5243     ("RC")
//! u16  protocol version
//! u16  message type
//! u16  flags
//! ..   payload
//! ```
//!
//! Every integer is big-endian. The decoder is deliberately strict — a trailing byte, a malformed
//! UTF-8 sequence, or a boolean that is not exactly 0 or 1 is an error rather than something to
//! shrug at — because a frame arrives from an unauthenticated peer on the local network and a
//! lenient parser is where such peers get their leverage.

use std::fmt;

#[path = "codec_browser.rs"]
mod browser_payload;
#[path = "codec_browser_phase2.rs"]
mod browser_phase2_payload;
#[path = "codec_browser_phase2_validate.rs"]
mod browser_phase2_validate;
#[path = "codec_browser_phase3.rs"]
mod browser_phase3_payload;
#[path = "codec_media.rs"]
mod media_payload;

use super::{
    browser, version, AuthMethod, ByeReason, CodecId, ControlEvent, Frame, KeyAction, Message,
    MessageType, PointerAction, TransportAction,
};

/// The envelope that precedes every payload: magic, version, type, flags.
pub const ENVELOPE_LEN: usize = 8;

/// The largest frame body the decoder will accept.
///
/// A peer can declare any length. Capping it is what stops a hostile or broken responder on the
/// local network making the engine allocate without bound.
pub const MAX_FRAME_LEN: usize = 16 * 1024 * 1024;

const MAGIC: u16 = 0x5243;

const MAX_DEVICE_NAME_BYTES: usize = 255;
const MAX_FINGERPRINT_BYTES: usize = 512;
const MAX_AUTH_BYTES: usize = 4 * 1024;
const MAX_CODEC_CONFIG_BYTES: usize = 1024 * 1024;
const MAX_MEDIA_PACKET_BYTES: usize = 15 * 1024 * 1024;
const MAX_TEXT_CONTROL_BYTES: usize = 16 * 1024;
const MAX_BYE_DETAIL_BYTES: usize = 1024;
const MAX_CODEC_CAPABILITIES: usize = 256;
const MAX_CODEC_CONFIG_BLOCKS: usize = 16;

/// Why a frame could not be read or written.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum WireError {
    /// The payload version is outside what this build implements.
    UnsupportedVersion(u16),
    /// The frame's magic did not match, so the peer is not speaking this protocol.
    BadMagic(u16),
    /// A length prefix was outside its permitted range.
    InvalidLength {
        /// What was being read.
        field: &'static str,
        /// The length that was declared.
        length: i64,
    },
    /// The buffer ended before the field did.
    Truncated(&'static str),
    /// Bytes remained after a complete message was read.
    TrailingBytes(usize),
    /// A tag had no meaning in this position.
    InvalidValue {
        /// What was being read.
        field: &'static str,
        /// The value that was rejected.
        value: i64,
    },
    /// A string field was not valid UTF-8.
    InvalidUtf8(&'static str),
    /// A field violated an invariant the protocol requires.
    Invariant(&'static str),
}

impl fmt::Display for WireError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::UnsupportedVersion(version) => {
                write!(formatter, "unsupported wire payload version: {version}")
            }
            Self::BadMagic(magic) => write!(formatter, "invalid frame magic: 0x{magic:04x}"),
            Self::InvalidLength { field, length } => {
                write!(formatter, "invalid {field} length: {length}")
            }
            Self::Truncated(field) => write!(formatter, "truncated {field}"),
            Self::TrailingBytes(count) => write!(formatter, "trailing payload bytes: {count}"),
            Self::InvalidValue { field, value } => {
                write!(formatter, "invalid {field}: {value}")
            }
            Self::InvalidUtf8(field) => write!(formatter, "invalid UTF-8 in {field}"),
            Self::Invariant(message) => write!(formatter, "{message}"),
        }
    }
}

impl std::error::Error for WireError {}

/// Encodes a frame, envelope and all.
///
/// # Errors
/// Returns an error when the payload version is unsupported, a field exceeds its cap, or the
/// finished frame would exceed [`MAX_FRAME_LEN`].
pub fn encode(frame: &Frame) -> Result<Vec<u8>, WireError> {
    let payload = encode_payload(frame.protocol_version, &frame.message)?;
    let body_len = ENVELOPE_LEN + payload.len();
    if body_len > MAX_FRAME_LEN {
        return Err(WireError::InvalidLength {
            field: "frame body",
            length: body_len as i64,
        });
    }

    let mut out = Vec::with_capacity(4 + body_len);
    out.extend_from_slice(&(body_len as u32).to_be_bytes());
    out.extend_from_slice(&MAGIC.to_be_bytes());
    out.extend_from_slice(&frame.protocol_version.to_be_bytes());
    out.extend_from_slice(&frame.message.type_id().to_be_bytes());
    out.extend_from_slice(&frame.flags.to_be_bytes());
    out.extend_from_slice(&payload);
    Ok(out)
}

/// Decodes exactly one frame, rejecting anything left over.
///
/// # Errors
/// Returns an error when the buffer is not exactly one well-formed frame.
pub fn decode(bytes: &[u8]) -> Result<Frame, WireError> {
    let (frame, consumed) = decode_prefix(bytes)?;
    if consumed != bytes.len() {
        return Err(WireError::TrailingBytes(bytes.len() - consumed));
    }
    Ok(frame)
}

/// Decodes the first frame in a buffer, reporting how many bytes it used.
///
/// This is the entry point for a stream reader: the caller keeps the remainder and calls again.
///
/// # Errors
/// Returns [`WireError::Truncated`] when the buffer holds only part of a frame, which a stream
/// reader treats as "read more" rather than as a failure.
pub fn decode_prefix(bytes: &[u8]) -> Result<(Frame, usize), WireError> {
    if bytes.len() < 4 {
        return Err(WireError::Truncated("frame length"));
    }

    let body_len = u32::from_be_bytes([bytes[0], bytes[1], bytes[2], bytes[3]]) as usize;
    if !(ENVELOPE_LEN..=MAX_FRAME_LEN).contains(&body_len) {
        return Err(WireError::InvalidLength {
            field: "frame body",
            length: body_len as i64,
        });
    }

    let total = 4 + body_len;
    if bytes.len() < total {
        return Err(WireError::Truncated("frame body"));
    }

    let body = &bytes[4..total];
    let magic = u16::from_be_bytes([body[0], body[1]]);
    if magic != MAGIC {
        return Err(WireError::BadMagic(magic));
    }

    let protocol_version = u16::from_be_bytes([body[2], body[3]]);
    if protocol_version == 0 {
        return Err(WireError::InvalidValue {
            field: "protocol version",
            value: 0,
        });
    }

    let type_id = u16::from_be_bytes([body[4], body[5]]);
    if type_id == 0 {
        return Err(WireError::InvalidValue {
            field: "message type",
            value: 0,
        });
    }

    let flags = u16::from_be_bytes([body[6], body[7]]);
    let message = decode_payload(protocol_version, type_id, &body[ENVELOPE_LEN..])?;

    Ok((
        Frame {
            protocol_version,
            message,
            flags,
        },
        total,
    ))
}

fn require_supported(protocol_version: u16) -> Result<(), WireError> {
    // Inclusive at both ends: the minimum is supported and so is the current version.
    if !(version::MIN_SUPPORTED..=version::CURRENT).contains(&protocol_version) {
        return Err(WireError::UnsupportedVersion(protocol_version));
    }
    Ok(())
}

fn encode_payload(protocol_version: u16, message: &Message) -> Result<Vec<u8>, WireError> {
    // An unknown message is relayed byte for byte, and is deliberately not version-checked: this
    // build cannot judge a payload it does not understand, and refusing to carry it would break
    // forward compatibility for no gain.
    if let Message::Unknown { payload, .. } = message {
        return Ok(payload.clone());
    }

    require_supported(protocol_version)?;
    if !browser::is_allowed_at_version(protocol_version, message) {
        return Err(WireError::Invariant(
            "browser protocol values require version 2",
        ));
    }
    if let Some(payload) = browser_phase3_payload::encode(message)? {
        return Ok(payload);
    }
    if let Some(payload) = browser_phase2_payload::encode(message)? {
        return Ok(payload);
    }
    if let Some(payload) = browser_payload::encode(message)? {
        return Ok(payload);
    }
    if let Some(payload) = media_payload::encode(message, protocol_version)? {
        return Ok(payload);
    }
    let mut writer = Writer::default();

    match message {
        Message::Hello {
            minimum_version,
            maximum_version,
            device_name,
            codec_capabilities,
            screen_width,
            screen_height,
            density_dpi,
        } => {
            if minimum_version < &1 || maximum_version < minimum_version {
                return Err(WireError::Invariant("hello version range is inverted"));
            }
            if device_name.is_empty() {
                return Err(WireError::Invariant("hello device name is empty"));
            }
            if *screen_width <= 0 || *screen_height <= 0 || *density_dpi <= 0 {
                return Err(WireError::Invariant(
                    "hello screen geometry must be positive",
                ));
            }
            if codec_capabilities.len() > MAX_CODEC_CAPABILITIES {
                return Err(WireError::InvalidLength {
                    field: "codec count",
                    length: codec_capabilities.len() as i64,
                });
            }

            writer.u16(*minimum_version);
            writer.u16(*maximum_version);
            writer.utf8_u16(device_name, MAX_DEVICE_NAME_BYTES, "device name")?;

            // Sorted and deduplicated, matching the Kotlin writer, so the same capability set
            // always produces the same bytes and the golden vectors stay stable.
            let mut codecs: Vec<u16> = codec_capabilities.iter().map(|codec| codec.0).collect();
            codecs.sort_unstable();
            codecs.dedup();
            writer.u16(codecs.len() as u16);
            for codec in codecs {
                writer.u16(codec);
            }

            writer.i32(*screen_width);
            writer.i32(*screen_height);
            writer.i32(*density_dpi);
        }

        Message::Auth {
            method,
            credential,
            public_key_fingerprint,
        } => {
            if credential.is_empty() {
                return Err(WireError::Invariant("auth credential is empty"));
            }
            writer.u8(*method as u8);
            writer.binary(credential, MAX_AUTH_BYTES, "credential")?;
            match public_key_fingerprint {
                Some(fingerprint) => {
                    if fingerprint.trim().is_empty() {
                        return Err(WireError::Invariant("auth fingerprint is blank"));
                    }
                    writer.u8(1);
                    writer.utf8_u16(fingerprint, MAX_FINGERPRINT_BYTES, "fingerprint")?;
                }
                None => writer.u8(0),
            }
        }

        Message::VideoConfig {
            codec,
            width,
            height,
            codec_specific_data,
        } => {
            if *width <= 0 || *height <= 0 {
                return Err(WireError::Invariant("video dimensions must be positive"));
            }
            if codec_specific_data.len() > MAX_CODEC_CONFIG_BLOCKS {
                return Err(WireError::InvalidLength {
                    field: "video config count",
                    length: codec_specific_data.len() as i64,
                });
            }
            writer.u16(codec.0);
            writer.i32(*width);
            writer.i32(*height);
            writer.u8(codec_specific_data.len() as u8);
            for block in codec_specific_data {
                writer.binary(block, MAX_CODEC_CONFIG_BYTES, "video config block")?;
            }
        }

        Message::Video {
            presentation_time_us,
            key_frame,
            data,
        } => {
            if *presentation_time_us < 0 {
                return Err(WireError::Invariant("presentation time is negative"));
            }
            if data.is_empty() {
                return Err(WireError::Invariant("video packet is empty"));
            }
            writer.i64(*presentation_time_us);
            writer.u8(u8::from(*key_frame));
            writer.binary(data, MAX_MEDIA_PACKET_BYTES, "video packet")?;
        }

        Message::AudioConfig {
            codec,
            sample_rate_hz,
            channel_count,
            codec_specific_data,
        } => {
            if !(1..=768_000).contains(sample_rate_hz) {
                return Err(WireError::Invariant("sample rate is out of range"));
            }
            if !(1..=32).contains(channel_count) {
                return Err(WireError::Invariant("channel count is out of range"));
            }
            writer.u16(codec.0);
            writer.i32(*sample_rate_hz);
            writer.u8(*channel_count);
            writer.binary(codec_specific_data, MAX_CODEC_CONFIG_BYTES, "audio config")?;
        }

        Message::Audio {
            presentation_time_us,
            data,
        } => {
            if *presentation_time_us < 0 {
                return Err(WireError::Invariant("presentation time is negative"));
            }
            if data.is_empty() {
                return Err(WireError::Invariant("audio packet is empty"));
            }
            writer.i64(*presentation_time_us);
            writer.binary(data, MAX_MEDIA_PACKET_BYTES, "audio packet")?;
        }

        Message::Control {
            sequence_number,
            event,
        } => {
            if *sequence_number < 0 {
                return Err(WireError::Invariant("control sequence is negative"));
            }
            writer.i64(*sequence_number);
            writer.u8(event.event_id());
            encode_control(&mut writer, event)?;
        }

        Message::Stats {
            receiver_queue_depth,
            decode_latency_us,
            round_trip_time_us,
            dropped_video_frames,
        } => {
            if *receiver_queue_depth < 0
                || *decode_latency_us < 0
                || *round_trip_time_us < 0
                || *dropped_video_frames < 0
            {
                return Err(WireError::Invariant("stats counters must not be negative"));
            }
            writer.i32(*receiver_queue_depth);
            writer.i64(*decode_latency_us);
            writer.i64(*round_trip_time_us);
            writer.i64(*dropped_video_frames);
        }

        Message::Bye { reason, detail } => {
            writer.u8(*reason as u8);
            writer.utf8_u16(detail, MAX_BYE_DETAIL_BYTES, "bye detail")?;
        }

        Message::MediaCommand { .. }
        | Message::MediaData { .. }
        | Message::Surface { .. }
        | Message::PlaybackState { .. }
        | Message::BrowserCapability { .. }
        | Message::BrowserCommand { .. }
        | Message::BrowserInput { .. }
        | Message::BrowserState { .. }
        | Message::BrowserPreview { .. }
        | Message::BrowserDialog { .. }
        | Message::BrowserDialogReply { .. }
        | Message::BrowserTabCommand { .. }
        | Message::BrowserTabState { .. }
        | Message::BrowserViewCommand { .. }
        | Message::BrowserViewState { .. }
        | Message::BrowserFavicon { .. }
        | Message::BrowserLibraryCommand { .. }
        | Message::BrowserLibraryState { .. }
        | Message::BrowserProfileCommand { .. }
        | Message::BrowserProfileState { .. }
        | Message::BrowserNetworkCommand { .. }
        | Message::BrowserNetworkState { .. }
        | Message::BrowserWorkspaceCommand { .. }
        | Message::BrowserWorkspaceState { .. }
        | Message::BrowserWorkspaceResize { .. }
        | Message::BrowserWorkspaceGeometry { .. }
        | Message::BrowserWorkspaceInput { .. }
        | Message::Unknown { .. } => unreachable!("handled before the legacy codec"),
    }

    Ok(writer.finish())
}

fn encode_control(writer: &mut Writer, event: &ControlEvent) -> Result<(), WireError> {
    match event {
        ControlEvent::Transport {
            action,
            position_ms,
        } => {
            let seeking = *action == TransportAction::SeekTo;
            if seeking && *position_ms < 0 {
                return Err(WireError::Invariant("seek requires a position"));
            }
            if !seeking && *position_ms != -1 {
                return Err(WireError::Invariant("only seek carries a position"));
            }
            writer.u8(*action as u8);
            writer.i64(*position_ms);
        }
        ControlEvent::Pointer {
            action,
            x,
            y,
            buttons,
        } => {
            if !x.is_finite() || !y.is_finite() {
                return Err(WireError::Invariant("pointer coordinates must be finite"));
            }
            writer.u8(*action as u8);
            writer.f32(*x);
            writer.f32(*y);
            writer.i32(*buttons);
        }
        ControlEvent::Key { action, key_code } => {
            if *key_code < 0 {
                return Err(WireError::Invariant("key code is negative"));
            }
            writer.u8(*action as u8);
            writer.i32(*key_code);
        }
        ControlEvent::Text(text) => {
            writer.utf8_i32(text, MAX_TEXT_CONTROL_BYTES, "text input")?;
        }
        ControlEvent::Volume(level) => {
            if !level.is_finite() || !(0.0..=1.0).contains(level) {
                return Err(WireError::Invariant("volume must be between 0 and 1"));
            }
            writer.f32(*level);
        }
    }
    Ok(())
}

fn decode_payload(
    protocol_version: u16,
    type_id: u16,
    payload: &[u8],
) -> Result<Message, WireError> {
    let Some(message_type) = MessageType::from_id(type_id) else {
        // Forward compatibility: carry what we cannot read.
        return Ok(Message::Unknown {
            type_id,
            payload: payload.to_vec(),
        });
    };

    require_supported(protocol_version)?;
    if browser::is_browser_type(type_id) {
        if protocol_version < 2 {
            return Err(WireError::Invariant(
                "browser message types require protocol version 2",
            ));
        }
        if type_id >= 35 && protocol_version < 4 { return Err(WireError::Invariant("Workspace resizing requires protocol version 4")); }
        return if type_id >= MessageType::BrowserWorkspaceCommand as u16 {
            browser_phase3_payload::decode(message_type, payload)
        } else if type_id >= MessageType::BrowserTabCommand as u16 {
            browser_phase2_payload::decode(message_type, payload)
        } else {
            browser_payload::decode(message_type, payload)
        };
    }
    if let Some(message) = media_payload::decode(message_type, protocol_version, payload)? {
        return Ok(message);
    }
    let mut reader = Reader::new(payload);

    let message = match message_type {
        MessageType::Hello => {
            let minimum_version = reader.u16("minimum version")?;
            let maximum_version = reader.u16("maximum version")?;
            let device_name = reader.utf8_u16(MAX_DEVICE_NAME_BYTES, "device name")?;
            let count = reader.u16("codec count")? as usize;
            if count > MAX_CODEC_CAPABILITIES {
                return Err(WireError::InvalidLength {
                    field: "codec count",
                    length: count as i64,
                });
            }
            let mut codec_capabilities = Vec::with_capacity(count);
            for _ in 0..count {
                let value = reader.u16("codec id")?;
                codec_capabilities.push(CodecId::new(value).ok_or(WireError::InvalidValue {
                    field: "codec id",
                    value: 0,
                })?);
            }
            Message::Hello {
                minimum_version,
                maximum_version,
                device_name,
                codec_capabilities,
                screen_width: reader.i32("screen width")?,
                screen_height: reader.i32("screen height")?,
                density_dpi: reader.i32("density dpi")?,
            }
        }

        MessageType::Auth => {
            let method_id = reader.u8("auth method")?;
            let method = AuthMethod::from_id(method_id).ok_or(WireError::InvalidValue {
                field: "auth method",
                value: i64::from(method_id),
            })?;
            let credential = reader.binary(MAX_AUTH_BYTES, "credential")?;
            let present = reader.boolean("fingerprint presence")?;
            let public_key_fingerprint = if present {
                Some(reader.utf8_u16(MAX_FINGERPRINT_BYTES, "fingerprint")?)
            } else {
                None
            };
            Message::Auth {
                method,
                credential,
                public_key_fingerprint,
            }
        }

        MessageType::VideoConfig => {
            let codec_value = reader.u16("video codec")?;
            let codec = CodecId::new(codec_value).ok_or(WireError::InvalidValue {
                field: "video codec",
                value: 0,
            })?;
            let width = reader.i32("video width")?;
            let height = reader.i32("video height")?;
            let count = reader.u8("video config count")? as usize;
            if count > MAX_CODEC_CONFIG_BLOCKS {
                return Err(WireError::InvalidLength {
                    field: "video config count",
                    length: count as i64,
                });
            }
            let mut codec_specific_data = Vec::with_capacity(count);
            for _ in 0..count {
                codec_specific_data
                    .push(reader.binary(MAX_CODEC_CONFIG_BYTES, "video config block")?);
            }
            Message::VideoConfig {
                codec,
                width,
                height,
                codec_specific_data,
            }
        }

        MessageType::Video => Message::Video {
            presentation_time_us: reader.i64("video presentation time")?,
            key_frame: reader.boolean("key-frame flag")?,
            data: reader.binary(MAX_MEDIA_PACKET_BYTES, "video packet")?,
        },

        MessageType::AudioConfig => {
            let codec_value = reader.u16("audio codec")?;
            let codec = CodecId::new(codec_value).ok_or(WireError::InvalidValue {
                field: "audio codec",
                value: 0,
            })?;
            Message::AudioConfig {
                codec,
                sample_rate_hz: reader.i32("sample rate")?,
                channel_count: reader.u8("channel count")?,
                codec_specific_data: reader.binary(MAX_CODEC_CONFIG_BYTES, "audio config")?,
            }
        }

        MessageType::Audio => Message::Audio {
            presentation_time_us: reader.i64("audio presentation time")?,
            data: reader.binary(MAX_MEDIA_PACKET_BYTES, "audio packet")?,
        },

        MessageType::Control => {
            let sequence_number = reader.i64("control sequence")?;
            let event_id = reader.u8("control event type")?;
            let event = decode_control(&mut reader, event_id)?;
            Message::Control {
                sequence_number,
                event,
            }
        }

        MessageType::Stats => Message::Stats {
            receiver_queue_depth: reader.i32("receiver queue depth")?,
            decode_latency_us: reader.i64("decode latency")?,
            round_trip_time_us: reader.i64("round-trip time")?,
            dropped_video_frames: reader.i64("dropped video frames")?,
        },

        MessageType::Bye => {
            let reason_id = reader.u8("bye reason")?;
            let reason = ByeReason::from_id(reason_id).ok_or(WireError::InvalidValue {
                field: "bye reason",
                value: i64::from(reason_id),
            })?;
            Message::Bye {
                reason,
                detail: reader.utf8_u16(MAX_BYE_DETAIL_BYTES, "bye detail")?,
            }
        }
        MessageType::MediaCommand
        | MessageType::Surface
        | MessageType::PlaybackState
        | MessageType::MediaData
        | MessageType::BrowserCapability
        | MessageType::BrowserCommand
        | MessageType::BrowserInput
        | MessageType::BrowserState
        | MessageType::BrowserPreview
        | MessageType::BrowserDialog
        | MessageType::BrowserDialogReply
        | MessageType::BrowserTabCommand
        | MessageType::BrowserTabState
        | MessageType::BrowserViewCommand
        | MessageType::BrowserViewState
        | MessageType::BrowserFavicon
        | MessageType::BrowserLibraryCommand
        | MessageType::BrowserLibraryState
        | MessageType::BrowserProfileCommand
        | MessageType::BrowserProfileState
        | MessageType::BrowserNetworkCommand
        | MessageType::BrowserNetworkState
        | MessageType::BrowserWorkspaceCommand
        | MessageType::BrowserWorkspaceState
        | MessageType::BrowserWorkspaceResize
        | MessageType::BrowserWorkspaceGeometry
        | MessageType::BrowserWorkspaceInput => unreachable!("handled by a family payload codec"),
    };

    reader.require_finished()?;
    Ok(message)
}

fn decode_control(reader: &mut Reader<'_>, event_id: u8) -> Result<ControlEvent, WireError> {
    match event_id {
        1 => {
            let action_id = reader.u8("transport action")?;
            let action = TransportAction::from_id(action_id).ok_or(WireError::InvalidValue {
                field: "transport action",
                value: i64::from(action_id),
            })?;
            Ok(ControlEvent::Transport {
                action,
                position_ms: reader.i64("transport position")?,
            })
        }
        2 => {
            let action_id = reader.u8("pointer action")?;
            let action = PointerAction::from_id(action_id).ok_or(WireError::InvalidValue {
                field: "pointer action",
                value: i64::from(action_id),
            })?;
            Ok(ControlEvent::Pointer {
                action,
                x: reader.f32("pointer x")?,
                y: reader.f32("pointer y")?,
                buttons: reader.i32("pointer buttons")?,
            })
        }
        3 => {
            let action_id = reader.u8("key action")?;
            let action = KeyAction::from_id(action_id).ok_or(WireError::InvalidValue {
                field: "key action",
                value: i64::from(action_id),
            })?;
            Ok(ControlEvent::Key {
                action,
                key_code: reader.i32("key code")?,
            })
        }
        4 => Ok(ControlEvent::Text(
            reader.utf8_i32(MAX_TEXT_CONTROL_BYTES, "text input")?,
        )),
        5 => Ok(ControlEvent::Volume(reader.f32("volume")?)),
        other => Err(WireError::InvalidValue {
            field: "control event type",
            value: i64::from(other),
        }),
    }
}

#[derive(Default)]
struct Writer {
    bytes: Vec<u8>,
}

impl Writer {
    fn u8(&mut self, value: u8) {
        self.bytes.push(value);
    }

    fn u16(&mut self, value: u16) {
        self.bytes.extend_from_slice(&value.to_be_bytes());
    }

    fn i32(&mut self, value: i32) {
        self.bytes.extend_from_slice(&value.to_be_bytes());
    }

    fn i64(&mut self, value: i64) {
        self.bytes.extend_from_slice(&value.to_be_bytes());
    }

    fn f32(&mut self, value: f32) {
        self.bytes.extend_from_slice(&value.to_bits().to_be_bytes());
    }

    fn utf8_u16(
        &mut self,
        value: &str,
        maximum: usize,
        field: &'static str,
    ) -> Result<(), WireError> {
        let encoded = value.as_bytes();
        if encoded.len() > maximum || encoded.len() > usize::from(u16::MAX) {
            return Err(WireError::InvalidLength {
                field,
                length: encoded.len() as i64,
            });
        }
        self.u16(encoded.len() as u16);
        self.bytes.extend_from_slice(encoded);
        Ok(())
    }

    fn utf8_i32(
        &mut self,
        value: &str,
        maximum: usize,
        field: &'static str,
    ) -> Result<(), WireError> {
        let encoded = value.as_bytes();
        if encoded.len() > maximum {
            return Err(WireError::InvalidLength {
                field,
                length: encoded.len() as i64,
            });
        }
        self.i32(encoded.len() as i32);
        self.bytes.extend_from_slice(encoded);
        Ok(())
    }

    fn binary(
        &mut self,
        value: &[u8],
        maximum: usize,
        field: &'static str,
    ) -> Result<(), WireError> {
        if value.len() > maximum {
            return Err(WireError::InvalidLength {
                field,
                length: value.len() as i64,
            });
        }
        self.i32(value.len() as i32);
        self.bytes.extend_from_slice(value);
        Ok(())
    }

    fn finish(self) -> Vec<u8> {
        self.bytes
    }
}

struct Reader<'a> {
    bytes: &'a [u8],
    offset: usize,
}

impl<'a> Reader<'a> {
    fn new(bytes: &'a [u8]) -> Self {
        Self { bytes, offset: 0 }
    }

    fn take(&mut self, count: usize, field: &'static str) -> Result<&'a [u8], WireError> {
        if self.offset + count > self.bytes.len() {
            return Err(WireError::Truncated(field));
        }
        let slice = &self.bytes[self.offset..self.offset + count];
        self.offset += count;
        Ok(slice)
    }

    fn u8(&mut self, field: &'static str) -> Result<u8, WireError> {
        Ok(self.take(1, field)?[0])
    }

    fn u16(&mut self, field: &'static str) -> Result<u16, WireError> {
        let slice = self.take(2, field)?;
        Ok(u16::from_be_bytes([slice[0], slice[1]]))
    }

    fn i32(&mut self, field: &'static str) -> Result<i32, WireError> {
        let slice = self.take(4, field)?;
        Ok(i32::from_be_bytes([slice[0], slice[1], slice[2], slice[3]]))
    }

    fn i64(&mut self, field: &'static str) -> Result<i64, WireError> {
        let slice = self.take(8, field)?;
        let mut buffer = [0u8; 8];
        buffer.copy_from_slice(slice);
        Ok(i64::from_be_bytes(buffer))
    }

    fn f32(&mut self, field: &'static str) -> Result<f32, WireError> {
        let slice = self.take(4, field)?;
        Ok(f32::from_bits(u32::from_be_bytes([
            slice[0], slice[1], slice[2], slice[3],
        ])))
    }

    fn boolean(&mut self, field: &'static str) -> Result<bool, WireError> {
        match self.u8(field)? {
            0 => Ok(false),
            1 => Ok(true),
            other => Err(WireError::InvalidValue {
                field,
                value: i64::from(other),
            }),
        }
    }

    fn binary(&mut self, maximum: usize, field: &'static str) -> Result<Vec<u8>, WireError> {
        let length = self.i32(field)?;
        if length < 0 || length as usize > maximum {
            return Err(WireError::InvalidLength {
                field,
                length: i64::from(length),
            });
        }
        Ok(self.take(length as usize, field)?.to_vec())
    }

    fn utf8_u16(&mut self, maximum: usize, field: &'static str) -> Result<String, WireError> {
        let length = self.u16(field)? as usize;
        self.utf8(length, maximum, field)
    }

    fn utf8_i32(&mut self, maximum: usize, field: &'static str) -> Result<String, WireError> {
        let length = self.i32(field)?;
        if length < 0 {
            return Err(WireError::InvalidLength {
                field,
                length: i64::from(length),
            });
        }
        self.utf8(length as usize, maximum, field)
    }

    fn utf8(
        &mut self,
        length: usize,
        maximum: usize,
        field: &'static str,
    ) -> Result<String, WireError> {
        if length > maximum {
            return Err(WireError::InvalidLength {
                field,
                length: length as i64,
            });
        }
        let slice = self.take(length, field)?;
        // Strict: a malformed sequence is an error, never a replacement character. Silently
        // substituting would let a peer smuggle a different string past a comparison later.
        std::str::from_utf8(slice)
            .map(str::to_owned)
            .map_err(|_| WireError::InvalidUtf8(field))
    }

    fn require_finished(&self) -> Result<(), WireError> {
        let remaining = self.bytes.len() - self.offset;
        if remaining != 0 {
            return Err(WireError::TrailingBytes(remaining));
        }
        Ok(())
    }
}
