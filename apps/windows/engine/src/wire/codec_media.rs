//! Payload codecs for the v1-compatible local-media message family.

use super::WireError;
use crate::wire::{MediaAction, Message, MessageType, PlaybackState, SurfaceMode};

const MAX_URL_BYTES: usize = 4 * 1024;
const MAX_TITLE_BYTES: usize = 512;
const MAX_DETAIL_BYTES: usize = 1024;
const MAX_MEDIA_PACKET_BYTES: usize = 15 * 1024 * 1024;

/// Encodes a media payload, returning `None` for a non-media message.
pub(super) fn encode(
    message: &Message,
    protocol_version: u16,
) -> Result<Option<Vec<u8>>, WireError> {
    let mut writer = Writer::default();
    match message {
        Message::MediaCommand {
            action,
            url,
            title,
            mime_type,
            duration_ms,
            start_position_ms,
            subtitle_url,
        } => {
            if *duration_ms < -1 {
                return Err(WireError::Invariant("media duration must be -1 or greater"));
            }
            if *start_position_ms < 0 {
                return Err(WireError::Invariant("media start position is negative"));
            }
            if *action == MediaAction::Load && mime_type.trim().is_empty() {
                return Err(WireError::Invariant("media load requires a MIME type"));
            }
            if subtitle_url
                .as_ref()
                .is_some_and(|url| url.trim().is_empty())
            {
                return Err(WireError::Invariant("media subtitle URL is blank"));
            }
            writer.u8(*action as u8);
            writer.utf8_u16(url, MAX_URL_BYTES, "media URL")?;
            writer.utf8_u16(title, MAX_TITLE_BYTES, "media title")?;
            writer.utf8_u16(mime_type, MAX_TITLE_BYTES, "media MIME type")?;
            writer.i64(*duration_ms);
            writer.i64(*start_position_ms);
            writer.nullable_utf8_u16(
                subtitle_url.as_deref(),
                MAX_URL_BYTES,
                "media subtitle URL",
            )?;
        }
        Message::MediaData { data, is_final } => {
            writer.u8(u8::from(*is_final));
            writer.binary(data, MAX_MEDIA_PACKET_BYTES, "media data chunk")?;
        }
        Message::Surface { mode, caption } => {
            if *mode == SurfaceMode::Browser && protocol_version < 2 {
                return Err(WireError::Invariant(
                    "browser surface requires protocol version 2",
                ));
            }
            writer.u8(*mode as u8);
            writer.utf8_u16(caption, MAX_TITLE_BYTES, "surface caption")?;
        }
        Message::PlaybackState {
            state,
            position_ms,
            duration_ms,
            detail,
        } => {
            if *position_ms < 0 {
                return Err(WireError::Invariant("playback position is negative"));
            }
            if *duration_ms < -1 {
                return Err(WireError::Invariant("media duration must be -1 or greater"));
            }
            writer.u8(*state as u8);
            writer.i64(*position_ms);
            writer.i64(*duration_ms);
            writer.utf8_u16(detail, MAX_DETAIL_BYTES, "playback detail")?;
        }
        _ => return Ok(None),
    }
    Ok(Some(writer.finish()))
}

/// Decodes a media payload, returning `None` for a non-media type.
pub(super) fn decode(
    message_type: MessageType,
    protocol_version: u16,
    payload: &[u8],
) -> Result<Option<Message>, WireError> {
    let mut reader = Reader::new(payload);
    let message = match message_type {
        MessageType::MediaCommand => {
            let id = reader.u8("media action")?;
            let action = MediaAction::from_id(id).ok_or(WireError::InvalidValue {
                field: "media action",
                value: i64::from(id),
            })?;
            Message::MediaCommand {
                action,
                url: reader.utf8_u16(MAX_URL_BYTES, "media URL")?,
                title: reader.utf8_u16(MAX_TITLE_BYTES, "media title")?,
                mime_type: reader.utf8_u16(MAX_TITLE_BYTES, "media MIME type")?,
                duration_ms: reader.i64("media duration")?,
                start_position_ms: reader.i64("media start position")?,
                subtitle_url: reader.nullable_utf8_u16(MAX_URL_BYTES, "media subtitle URL")?,
            }
        }
        MessageType::MediaData => Message::MediaData {
            is_final: reader.boolean("media data final flag")?,
            data: reader.binary(MAX_MEDIA_PACKET_BYTES, "media data chunk")?,
        },
        MessageType::Surface => {
            let id = reader.u8("surface mode")?;
            let mode = SurfaceMode::from_id(id).ok_or(WireError::InvalidValue {
                field: "surface mode",
                value: i64::from(id),
            })?;
            if mode == SurfaceMode::Browser && protocol_version < 2 {
                return Err(WireError::Invariant(
                    "browser surface requires protocol version 2",
                ));
            }
            Message::Surface {
                mode,
                caption: reader.utf8_u16(MAX_TITLE_BYTES, "surface caption")?,
            }
        }
        MessageType::PlaybackState => {
            let id = reader.u8("playback state")?;
            let state = PlaybackState::from_id(id).ok_or(WireError::InvalidValue {
                field: "playback state",
                value: i64::from(id),
            })?;
            let position_ms = reader.i64("playback position")?;
            let duration_ms = reader.i64("playback duration")?;
            if position_ms < 0 || duration_ms < -1 {
                return Err(WireError::Invariant("playback progress is out of range"));
            }
            Message::PlaybackState {
                state,
                position_ms,
                duration_ms,
                detail: reader.utf8_u16(MAX_DETAIL_BYTES, "playback detail")?,
            }
        }
        _ => return Ok(None),
    };
    reader.require_finished()?;
    Ok(Some(message))
}

#[derive(Default)]
struct Writer {
    bytes: Vec<u8>,
}

impl Writer {
    fn u8(&mut self, value: u8) {
        self.bytes.push(value);
    }

    fn i64(&mut self, value: i64) {
        self.bytes.extend_from_slice(&value.to_be_bytes());
    }

    fn utf8_u16(
        &mut self,
        value: &str,
        maximum: usize,
        field: &'static str,
    ) -> Result<(), WireError> {
        let bytes = value.as_bytes();
        if bytes.len() > maximum || bytes.len() > usize::from(u16::MAX) {
            return Err(WireError::InvalidLength {
                field,
                length: bytes.len() as i64,
            });
        }
        self.bytes
            .extend_from_slice(&(bytes.len() as u16).to_be_bytes());
        self.bytes.extend_from_slice(bytes);
        Ok(())
    }

    fn nullable_utf8_u16(
        &mut self,
        value: Option<&str>,
        maximum: usize,
        field: &'static str,
    ) -> Result<(), WireError> {
        if let Some(value) = value {
            self.u8(1);
            self.utf8_u16(value, maximum, field)
        } else {
            self.u8(0);
            Ok(())
        }
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
        self.bytes
            .extend_from_slice(&(value.len() as i32).to_be_bytes());
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

    fn i32(&mut self, field: &'static str) -> Result<i32, WireError> {
        let bytes = self.take(4, field)?;
        Ok(i32::from_be_bytes([bytes[0], bytes[1], bytes[2], bytes[3]]))
    }

    fn i64(&mut self, field: &'static str) -> Result<i64, WireError> {
        let bytes = self.take(8, field)?;
        let mut value = [0; 8];
        value.copy_from_slice(bytes);
        Ok(i64::from_be_bytes(value))
    }

    fn boolean(&mut self, field: &'static str) -> Result<bool, WireError> {
        match self.u8(field)? {
            0 => Ok(false),
            1 => Ok(true),
            value => Err(WireError::InvalidValue {
                field,
                value: i64::from(value),
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
        let bytes = self.take(2, field)?;
        let length = u16::from_be_bytes([bytes[0], bytes[1]]) as usize;
        self.utf8(length, maximum, field)
    }

    fn nullable_utf8_u16(
        &mut self,
        maximum: usize,
        field: &'static str,
    ) -> Result<Option<String>, WireError> {
        if self.boolean("media subtitle URL presence")? {
            self.utf8_u16(maximum, field).map(Some)
        } else {
            Ok(None)
        }
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
        let bytes = self.take(length, field)?;
        std::str::from_utf8(bytes)
            .map(str::to_owned)
            .map_err(|_| WireError::InvalidUtf8(field))
    }

    fn require_finished(&self) -> Result<(), WireError> {
        let remaining = self.bytes.len() - self.offset;
        if remaining == 0 {
            Ok(())
        } else {
            Err(WireError::TrailingBytes(remaining))
        }
    }
}
