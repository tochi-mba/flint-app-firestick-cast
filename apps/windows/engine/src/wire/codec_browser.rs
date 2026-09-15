//! Strict v2 browser payload encoding.
//!
//! This module deliberately knows nothing about sockets, certificates, or WebView. The ordinary
//! cast connection rejects these values; only the authenticated browser TLS channel may dispatch
//! them. Keeping the bounded codec here is what lets all implementations test the contract before
//! the transport is involved.

use super::WireError;
use crate::wire::browser::limits;
use crate::wire::{
    BrowserCapabilityStatus, BrowserCommandAction, BrowserDialogType, BrowserInputEvent,
    BrowserLoadState, BrowserPointerAction, BrowserPreviewState, BrowserSemanticKey, Message,
    MessageType,
};

/// Encodes a browser payload, returning `None` for a non-browser message.
pub(super) fn encode(message: &Message) -> Result<Option<Vec<u8>>, WireError> {
    if !matches!(message.type_id(), 14..=20) {
        return Ok(None);
    }
    validate(message)?;

    let mut writer = Writer::default();
    match message {
        Message::BrowserCapability {
            status,
            secure_endpoint_port,
            api_level,
            web_view_version,
            preview_supported,
            preview_max_width,
            preview_max_height,
            interactive_preview_frames_per_second,
            idle_preview_frames_per_second,
            preview_max_bytes,
            detail,
        } => {
            writer.u8(*status as u8);
            writer.u16(*secure_endpoint_port);
            writer.u16(*api_level);
            writer.utf8_u16(web_view_version, limits::MAX_TITLE_BYTES, "WebView version")?;
            writer.boolean(*preview_supported);
            writer.u16(*preview_max_width);
            writer.u16(*preview_max_height);
            writer.u8(*interactive_preview_frames_per_second);
            writer.u8(*idle_preview_frames_per_second);
            writer.i32(*preview_max_bytes);
            writer.utf8_u16(
                detail,
                limits::MAX_DETAIL_BYTES,
                "browser capability detail",
            )?;
        }
        Message::BrowserCommand {
            epoch,
            command_id,
            action,
            url,
            preview_enabled,
        } => {
            writer.i64(*epoch);
            writer.i64(*command_id);
            writer.u8(*action as u8);
            match *action {
                BrowserCommandAction::Open | BrowserCommandAction::Navigate => writer.utf8_u16(
                    url.as_deref().expect("validated browser command URL"),
                    limits::MAX_URL_BYTES,
                    "browser command URL",
                )?,
                BrowserCommandAction::SetPreviewEnabled => {
                    writer.boolean((*preview_enabled).expect("validated browser preview setting"));
                }
                _ => {}
            }
        }
        Message::BrowserInput {
            epoch,
            sequence,
            event,
        } => {
            writer.i64(*epoch);
            writer.i64(*sequence);
            writer.u8(event.event_id());
            encode_input(&mut writer, event)?;
        }
        Message::BrowserState {
            epoch,
            revision,
            navigation_id,
            last_accepted_command_id,
            last_accepted_input_sequence,
            load_state,
            url,
            title,
            progress,
            can_go_back,
            can_go_forward,
            viewport_width,
            viewport_height,
            preview_state,
            error_detail,
        } => {
            writer.i64(*epoch);
            writer.i64(*revision);
            writer.i64(*navigation_id);
            writer.i64(*last_accepted_command_id);
            writer.i64(*last_accepted_input_sequence);
            writer.u8(*load_state as u8);
            writer.utf8_u16(url, limits::MAX_URL_BYTES, "browser state URL")?;
            writer.utf8_u16(title, limits::MAX_TITLE_BYTES, "browser state title")?;
            writer.u8(*progress);
            writer.boolean(*can_go_back);
            writer.boolean(*can_go_forward);
            writer.i32(*viewport_width);
            writer.i32(*viewport_height);
            writer.u8(*preview_state as u8);
            writer.utf8_u16(
                error_detail,
                limits::MAX_DETAIL_BYTES,
                "browser state error detail",
            )?;
        }
        Message::BrowserPreview {
            epoch,
            navigation_id,
            frame_id,
            width,
            height,
            jpeg,
        } => {
            writer.i64(*epoch);
            writer.i64(*navigation_id);
            writer.i64(*frame_id);
            writer.u16(*width);
            writer.u16(*height);
            writer.binary(jpeg, limits::MAX_PREVIEW_BYTES, "browser preview JPEG")?;
        }
        Message::BrowserDialog {
            epoch,
            dialog_id,
            dialog_type,
            origin,
            message,
            default_value,
            timeout_milliseconds,
        } => {
            writer.i64(*epoch);
            writer.i64(*dialog_id);
            writer.u8(*dialog_type as u8);
            writer.utf8_u16(origin, limits::MAX_ORIGIN_BYTES, "browser dialog origin")?;
            writer.utf8_u16(message, limits::MAX_DIALOG_BYTES, "browser dialog message")?;
            writer.utf8_u16(
                default_value,
                limits::MAX_DIALOG_BYTES,
                "browser dialog default value",
            )?;
            writer.i32(*timeout_milliseconds);
        }
        Message::BrowserDialogReply {
            epoch,
            dialog_id,
            accepted,
            prompt_text,
        } => {
            writer.i64(*epoch);
            writer.i64(*dialog_id);
            writer.boolean(*accepted);
            writer.nullable_utf8_u16(
                prompt_text.as_deref(),
                limits::MAX_TEXT_BYTES,
                "browser dialog prompt text",
            )?;
        }
        _ => unreachable!("browser predicate and message match diverged"),
    }

    Ok(Some(writer.finish()))
}

/// Decodes one known browser payload.
pub(super) fn decode(message_type: MessageType, payload: &[u8]) -> Result<Message, WireError> {
    let mut reader = Reader::new(payload);
    let message = match message_type {
        MessageType::BrowserCapability => Message::BrowserCapability {
            status: browser_capability_status(&mut reader)?,
            secure_endpoint_port: reader.u16("browser secure endpoint port")?,
            api_level: reader.u16("browser API level")?,
            web_view_version: reader.utf8_u16(limits::MAX_TITLE_BYTES, "WebView version")?,
            preview_supported: reader.boolean("browser preview supported")?,
            preview_max_width: reader.u16("browser preview maximum width")?,
            preview_max_height: reader.u16("browser preview maximum height")?,
            interactive_preview_frames_per_second: reader.u8("browser interactive preview rate")?,
            idle_preview_frames_per_second: reader.u8("browser idle preview rate")?,
            preview_max_bytes: reader.i32("browser preview maximum bytes")?,
            detail: reader.utf8_u16(limits::MAX_DETAIL_BYTES, "browser capability detail")?,
        },
        MessageType::BrowserCommand => {
            let epoch = reader.i64("browser command epoch")?;
            let command_id = reader.i64("browser command ID")?;
            let action = browser_command_action(&mut reader)?;
            let (url, preview_enabled) = match action {
                BrowserCommandAction::Open | BrowserCommandAction::Navigate => (
                    Some(reader.utf8_u16(limits::MAX_URL_BYTES, "browser command URL")?),
                    None,
                ),
                BrowserCommandAction::SetPreviewEnabled => {
                    (None, Some(reader.boolean("browser preview enabled")?))
                }
                _ => (None, None),
            };
            Message::BrowserCommand {
                epoch,
                command_id,
                action,
                url,
                preview_enabled,
            }
        }
        MessageType::BrowserInput => Message::BrowserInput {
            epoch: reader.i64("browser input epoch")?,
            sequence: reader.i64("browser input sequence")?,
            event: decode_input(&mut reader)?,
        },
        MessageType::BrowserState => Message::BrowserState {
            epoch: reader.i64("browser state epoch")?,
            revision: reader.i64("browser state revision")?,
            navigation_id: reader.i64("browser state navigation ID")?,
            last_accepted_command_id: reader.i64("browser state last accepted command ID")?,
            last_accepted_input_sequence: reader
                .i64("browser state last accepted input sequence")?,
            load_state: browser_load_state(&mut reader)?,
            url: reader.utf8_u16(limits::MAX_URL_BYTES, "browser state URL")?,
            title: reader.utf8_u16(limits::MAX_TITLE_BYTES, "browser state title")?,
            progress: reader.u8("browser state progress")?,
            can_go_back: reader.boolean("browser state can go back")?,
            can_go_forward: reader.boolean("browser state can go forward")?,
            viewport_width: reader.i32("browser viewport width")?,
            viewport_height: reader.i32("browser viewport height")?,
            preview_state: browser_preview_state(&mut reader)?,
            error_detail: reader
                .utf8_u16(limits::MAX_DETAIL_BYTES, "browser state error detail")?,
        },
        MessageType::BrowserPreview => Message::BrowserPreview {
            epoch: reader.i64("browser preview epoch")?,
            navigation_id: reader.i64("browser preview navigation ID")?,
            frame_id: reader.i64("browser preview frame ID")?,
            width: reader.u16("browser preview width")?,
            height: reader.u16("browser preview height")?,
            jpeg: reader.binary(limits::MAX_PREVIEW_BYTES, "browser preview JPEG")?,
        },
        MessageType::BrowserDialog => Message::BrowserDialog {
            epoch: reader.i64("browser dialog epoch")?,
            dialog_id: reader.i64("browser dialog ID")?,
            dialog_type: browser_dialog_type(&mut reader)?,
            origin: reader.utf8_u16(limits::MAX_ORIGIN_BYTES, "browser dialog origin")?,
            message: reader.utf8_u16(limits::MAX_DIALOG_BYTES, "browser dialog message")?,
            default_value: reader
                .utf8_u16(limits::MAX_DIALOG_BYTES, "browser dialog default value")?,
            timeout_milliseconds: reader.i32("browser dialog timeout")?,
        },
        MessageType::BrowserDialogReply => Message::BrowserDialogReply {
            epoch: reader.i64("browser dialog reply epoch")?,
            dialog_id: reader.i64("browser dialog reply ID")?,
            accepted: reader.boolean("browser dialog accepted")?,
            prompt_text: reader.nullable_utf8_u16(
                limits::MAX_TEXT_BYTES,
                "browser dialog prompt text",
                "browser dialog prompt text presence",
            )?,
        },
        _ => return Err(WireError::Invariant("unhandled browser message type")),
    };
    validate(&message)?;
    reader.require_finished()?;
    Ok(message)
}

fn encode_input(writer: &mut Writer, event: &BrowserInputEvent) -> Result<(), WireError> {
    match event {
        BrowserInputEvent::Pointer {
            action,
            navigation_id,
            frame_id,
            x,
            y,
            buttons,
        } => {
            writer.u8(*action as u8);
            write_preview_reference(writer, *navigation_id, *frame_id, *x, *y);
            writer.i32(*buttons);
        }
        BrowserInputEvent::Scroll {
            navigation_id,
            frame_id,
            x,
            y,
            delta_x,
            delta_y,
        } => {
            write_preview_reference(writer, *navigation_id, *frame_id, *x, *y);
            writer.i32(*delta_x);
            writer.i32(*delta_y);
        }
        BrowserInputEvent::SemanticKey(key) => writer.u8(*key as u8),
        BrowserInputEvent::Text(text) => {
            writer.utf8_u16(text, limits::MAX_TEXT_BYTES, "browser text input")?;
        }
    }
    Ok(())
}

fn decode_input(reader: &mut Reader<'_>) -> Result<BrowserInputEvent, WireError> {
    match reader.u8("browser input type")? {
        1 => {
            let action = browser_pointer_action(reader)?;
            let (navigation_id, frame_id, x, y) = read_preview_reference(reader)?;
            Ok(BrowserInputEvent::Pointer {
                action,
                navigation_id,
                frame_id,
                x,
                y,
                buttons: reader.i32("browser pointer buttons")?,
            })
        }
        2 => {
            let (navigation_id, frame_id, x, y) = read_preview_reference(reader)?;
            Ok(BrowserInputEvent::Scroll {
                navigation_id,
                frame_id,
                x,
                y,
                delta_x: reader.i32("browser scroll delta X")?,
                delta_y: reader.i32("browser scroll delta Y")?,
            })
        }
        3 => Ok(BrowserInputEvent::SemanticKey(browser_semantic_key(
            reader,
        )?)),
        4 => Ok(BrowserInputEvent::Text(
            reader.utf8_u16(limits::MAX_TEXT_BYTES, "browser text input")?,
        )),
        value => Err(WireError::InvalidValue {
            field: "browser input type",
            value: i64::from(value),
        }),
    }
}

fn write_preview_reference(writer: &mut Writer, navigation_id: i64, frame_id: i64, x: u16, y: u16) {
    writer.i64(navigation_id);
    writer.i64(frame_id);
    writer.u16(x);
    writer.u16(y);
}

fn read_preview_reference(reader: &mut Reader<'_>) -> Result<(i64, i64, u16, u16), WireError> {
    Ok((
        reader.i64("browser input navigation ID")?,
        reader.i64("browser input frame ID")?,
        reader.u16("browser input X")?,
        reader.u16("browser input Y")?,
    ))
}

fn browser_capability_status(
    reader: &mut Reader<'_>,
) -> Result<BrowserCapabilityStatus, WireError> {
    let value = reader.u8("browser capability status")?;
    BrowserCapabilityStatus::from_id(value).ok_or(WireError::InvalidValue {
        field: "browser capability status",
        value: i64::from(value),
    })
}

fn browser_command_action(reader: &mut Reader<'_>) -> Result<BrowserCommandAction, WireError> {
    let value = reader.u8("browser command action")?;
    BrowserCommandAction::from_id(value).ok_or(WireError::InvalidValue {
        field: "browser command action",
        value: i64::from(value),
    })
}

fn browser_pointer_action(reader: &mut Reader<'_>) -> Result<BrowserPointerAction, WireError> {
    let value = reader.u8("browser pointer action")?;
    BrowserPointerAction::from_id(value).ok_or(WireError::InvalidValue {
        field: "browser pointer action",
        value: i64::from(value),
    })
}

fn browser_semantic_key(reader: &mut Reader<'_>) -> Result<BrowserSemanticKey, WireError> {
    let value = reader.u8("browser semantic key")?;
    BrowserSemanticKey::from_id(value).ok_or(WireError::InvalidValue {
        field: "browser semantic key",
        value: i64::from(value),
    })
}

fn browser_load_state(reader: &mut Reader<'_>) -> Result<BrowserLoadState, WireError> {
    let value = reader.u8("browser load state")?;
    BrowserLoadState::from_id(value).ok_or(WireError::InvalidValue {
        field: "browser load state",
        value: i64::from(value),
    })
}

fn browser_preview_state(reader: &mut Reader<'_>) -> Result<BrowserPreviewState, WireError> {
    let value = reader.u8("browser preview state")?;
    BrowserPreviewState::from_id(value).ok_or(WireError::InvalidValue {
        field: "browser preview state",
        value: i64::from(value),
    })
}

fn browser_dialog_type(reader: &mut Reader<'_>) -> Result<BrowserDialogType, WireError> {
    let value = reader.u8("browser dialog type")?;
    BrowserDialogType::from_id(value).ok_or(WireError::InvalidValue {
        field: "browser dialog type",
        value: i64::from(value),
    })
}

fn validate(message: &Message) -> Result<(), WireError> {
    match message {
        Message::BrowserCapability {
            status,
            secure_endpoint_port,
            api_level,
            web_view_version,
            preview_supported,
            preview_max_width,
            preview_max_height,
            interactive_preview_frames_per_second,
            idle_preview_frames_per_second,
            preview_max_bytes,
            detail,
        } => {
            require(*api_level > 0, "browser API level is out of range")?;
            require_nonblank(web_view_version, "browser WebView version is blank")?;
            require_text(web_view_version, limits::MAX_TITLE_BYTES, "WebView version")?;
            require_text(
                detail,
                limits::MAX_DETAIL_BYTES,
                "browser capability detail",
            )?;
            if *status == BrowserCapabilityStatus::Available {
                require(
                    *secure_endpoint_port > 0,
                    "available browser capability requires a secure endpoint",
                )?;
            }
            if *preview_supported {
                require(
                    (1..=limits::MAX_PREVIEW_WIDTH).contains(preview_max_width),
                    "browser preview width is out of range",
                )?;
                require(
                    (1..=limits::MAX_PREVIEW_HEIGHT).contains(preview_max_height),
                    "browser preview height is out of range",
                )?;
                require(
                    (1..=limits::MAX_INTERACTIVE_PREVIEW_FPS)
                        .contains(interactive_preview_frames_per_second),
                    "browser interactive preview rate is out of range",
                )?;
                require(
                    (1..=limits::MAX_IDLE_PREVIEW_FPS).contains(idle_preview_frames_per_second),
                    "browser idle preview rate is out of range",
                )?;
                require(
                    *preview_max_bytes > 0
                        && *preview_max_bytes <= limits::MAX_PREVIEW_BYTES as i32,
                    "browser preview byte limit is out of range",
                )?;
            } else {
                require(
                    *preview_max_width == 0
                        && *preview_max_height == 0
                        && *interactive_preview_frames_per_second == 0
                        && *idle_preview_frames_per_second == 0
                        && *preview_max_bytes == 0,
                    "unavailable preview must advertise zero limits",
                )?;
            }
        }
        Message::BrowserCommand {
            epoch,
            command_id,
            action,
            url,
            preview_enabled,
        } => {
            require_positive(*epoch, "browser command epoch")?;
            require_positive(*command_id, "browser command ID")?;
            let requires_url = matches!(
                *action,
                BrowserCommandAction::Open | BrowserCommandAction::Navigate
            );
            let requires_preview = *action == BrowserCommandAction::SetPreviewEnabled;
            require(
                requires_url == url.is_some(),
                "browser command URL has an invalid action combination",
            )?;
            require(
                requires_preview == preview_enabled.is_some(),
                "browser preview setting has an invalid action combination",
            )?;
            if let Some(url) = url {
                require_nonblank(url, "browser command URL is blank")?;
                require_text(url, limits::MAX_URL_BYTES, "browser command URL")?;
            }
        }
        Message::BrowserInput {
            epoch,
            sequence,
            event,
        } => {
            require_positive(*epoch, "browser input epoch")?;
            require_positive(*sequence, "browser input sequence")?;
            validate_input(event)?;
        }
        Message::BrowserState {
            epoch,
            revision,
            navigation_id,
            last_accepted_command_id,
            last_accepted_input_sequence,
            load_state,
            url,
            title,
            progress,
            viewport_width,
            viewport_height,
            error_detail,
            ..
        } => {
            require_positive(*epoch, "browser state epoch")?;
            require_positive(*revision, "browser state revision")?;
            require_nonnegative(*navigation_id, "browser state navigation ID")?;
            require_nonnegative(
                *last_accepted_command_id,
                "browser state command acknowledgement",
            )?;
            require_nonnegative(
                *last_accepted_input_sequence,
                "browser state input acknowledgement",
            )?;
            require_text(url, limits::MAX_URL_BYTES, "browser state URL")?;
            require_text(title, limits::MAX_TITLE_BYTES, "browser state title")?;
            require_text(
                error_detail,
                limits::MAX_DETAIL_BYTES,
                "browser state error detail",
            )?;
            require(*progress <= 100, "browser state progress is out of range")?;
            require(
                (0..=limits::MAX_VIEWPORT_DIMENSION).contains(viewport_width),
                "browser viewport width is out of range",
            )?;
            require(
                (0..=limits::MAX_VIEWPORT_DIMENSION).contains(viewport_height),
                "browser viewport height is out of range",
            )?;
            if *load_state == BrowserLoadState::Failed {
                require_nonblank(
                    error_detail,
                    "failed browser state requires an error detail",
                )?;
            } else {
                require(
                    error_detail.is_empty(),
                    "only failed browser state carries an error detail",
                )?;
            }
            if matches!(
                *load_state,
                BrowserLoadState::Loading | BrowserLoadState::Loaded | BrowserLoadState::Failed
            ) {
                require_positive(*navigation_id, "active browser state navigation ID")?;
                require_nonblank(url, "active browser state URL is blank")?;
            }
        }
        Message::BrowserPreview {
            epoch,
            navigation_id,
            frame_id,
            width,
            height,
            jpeg,
        } => {
            require_positive(*epoch, "browser preview epoch")?;
            require_positive(*navigation_id, "browser preview navigation ID")?;
            require_positive(*frame_id, "browser preview frame ID")?;
            require(
                (1..=limits::MAX_PREVIEW_WIDTH).contains(width),
                "browser preview width is out of range",
            )?;
            require(
                (1..=limits::MAX_PREVIEW_HEIGHT).contains(height),
                "browser preview height is out of range",
            )?;
            require(
                !jpeg.is_empty() && jpeg.len() <= limits::MAX_PREVIEW_BYTES,
                "browser preview JPEG length is out of range",
            )?;
        }
        Message::BrowserDialog {
            epoch,
            dialog_id,
            dialog_type,
            origin,
            message,
            default_value,
            timeout_milliseconds,
        } => {
            require_positive(*epoch, "browser dialog epoch")?;
            require_positive(*dialog_id, "browser dialog ID")?;
            require_nonblank(origin, "browser dialog origin is blank")?;
            require_text(origin, limits::MAX_ORIGIN_BYTES, "browser dialog origin")?;
            require_text(message, limits::MAX_DIALOG_BYTES, "browser dialog message")?;
            require_text(
                default_value,
                limits::MAX_DIALOG_BYTES,
                "browser dialog default value",
            )?;
            require(
                (1..=limits::MAX_DIALOG_TIMEOUT_MILLISECONDS).contains(timeout_milliseconds),
                "browser dialog timeout is out of range",
            )?;
            if *dialog_type != BrowserDialogType::Prompt {
                require(
                    default_value.is_empty(),
                    "only browser prompts carry a default value",
                )?;
            }
        }
        Message::BrowserDialogReply {
            epoch,
            dialog_id,
            accepted,
            prompt_text,
        } => {
            require_positive(*epoch, "browser dialog reply epoch")?;
            require_positive(*dialog_id, "browser dialog reply ID")?;
            if !*accepted {
                require(
                    prompt_text.is_none(),
                    "cancelled browser dialog reply carries prompt text",
                )?;
            }
            if let Some(prompt_text) = prompt_text {
                require_text(
                    prompt_text,
                    limits::MAX_TEXT_BYTES,
                    "browser dialog prompt text",
                )?;
            }
        }
        _ => return Err(WireError::Invariant("unhandled browser message type")),
    }
    Ok(())
}

fn validate_input(event: &BrowserInputEvent) -> Result<(), WireError> {
    match event {
        BrowserInputEvent::Pointer {
            action,
            navigation_id,
            frame_id,
            buttons,
            ..
        } => {
            validate_preview_reference(*navigation_id, *frame_id)?;
            require(
                *buttons == 0 || *buttons == 1,
                "browser pointer buttons must be the primary bit or zero",
            )?;
            let valid_transition = match *action {
                BrowserPointerAction::Down => *buttons == 1,
                BrowserPointerAction::Up | BrowserPointerAction::Cancel => *buttons == 0,
                BrowserPointerAction::Move => true,
            };
            require(
                valid_transition,
                "browser pointer action has an invalid button transition",
            )?;
        }
        BrowserInputEvent::Scroll {
            navigation_id,
            frame_id,
            delta_x,
            delta_y,
            ..
        } => {
            validate_preview_reference(*navigation_id, *frame_id)?;
            require(
                *delta_x != 0 || *delta_y != 0,
                "browser scroll must have a delta",
            )?;
        }
        BrowserInputEvent::SemanticKey(_) => {}
        BrowserInputEvent::Text(text) => {
            require_text(text, limits::MAX_TEXT_BYTES, "browser text input")?;
        }
    }
    Ok(())
}

fn validate_preview_reference(navigation_id: i64, frame_id: i64) -> Result<(), WireError> {
    require_positive(navigation_id, "browser input navigation ID")?;
    require_positive(frame_id, "browser input frame ID")
}

fn require_positive(value: i64, field: &'static str) -> Result<(), WireError> {
    require(value > 0, field)
}

fn require_nonnegative(value: i64, field: &'static str) -> Result<(), WireError> {
    require(value >= 0, field)
}

fn require_nonblank(value: &str, field: &'static str) -> Result<(), WireError> {
    require(!value.trim().is_empty(), field)
}

fn require_text(value: &str, maximum: usize, field: &'static str) -> Result<(), WireError> {
    if value.len() > maximum {
        return Err(WireError::InvalidLength {
            field,
            length: value.len() as i64,
        });
    }
    Ok(())
}

fn require(condition: bool, message: &'static str) -> Result<(), WireError> {
    if condition {
        Ok(())
    } else {
        Err(WireError::Invariant(message))
    }
}

#[derive(Default)]
pub(super) struct Writer {
    bytes: Vec<u8>,
}

impl Writer {
    pub(super) fn u8(&mut self, value: u8) {
        self.bytes.push(value);
    }

    pub(super) fn u16(&mut self, value: u16) {
        self.bytes.extend_from_slice(&value.to_be_bytes());
    }

    pub(super) fn i32(&mut self, value: i32) {
        self.bytes.extend_from_slice(&value.to_be_bytes());
    }

    pub(super) fn i64(&mut self, value: i64) {
        self.bytes.extend_from_slice(&value.to_be_bytes());
    }

    pub(super) fn boolean(&mut self, value: bool) {
        self.u8(u8::from(value));
    }

    pub(super) fn utf8_u16(
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
        self.u16(bytes.len() as u16);
        self.bytes.extend_from_slice(bytes);
        Ok(())
    }

    pub(super) fn utf8_u32(
        &mut self,
        value: &str,
        maximum: usize,
        field: &'static str,
    ) -> Result<(), WireError> {
        let bytes = value.as_bytes();
        if bytes.len() > maximum {
            return Err(WireError::InvalidLength {
                field,
                length: bytes.len() as i64,
            });
        }
        self.u32(bytes.len() as u32);
        self.bytes.extend_from_slice(bytes);
        Ok(())
    }

    pub(super) fn u32(&mut self, value: u32) {
        self.bytes.extend_from_slice(&value.to_be_bytes());
    }

    pub(super) fn nullable_utf8_u16(
        &mut self,
        value: Option<&str>,
        maximum: usize,
        field: &'static str,
    ) -> Result<(), WireError> {
        if let Some(value) = value {
            self.boolean(true);
            self.utf8_u16(value, maximum, field)
        } else {
            self.boolean(false);
            Ok(())
        }
    }

    pub(super) fn binary(
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

    pub(super) fn finish(self) -> Vec<u8> {
        self.bytes
    }
}

pub(super) struct Reader<'a> {
    bytes: &'a [u8],
    offset: usize,
}

impl<'a> Reader<'a> {
    pub(super) fn new(bytes: &'a [u8]) -> Self {
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

    pub(super) fn u8(&mut self, field: &'static str) -> Result<u8, WireError> {
        Ok(self.take(1, field)?[0])
    }

    pub(super) fn u16(&mut self, field: &'static str) -> Result<u16, WireError> {
        let bytes = self.take(2, field)?;
        Ok(u16::from_be_bytes([bytes[0], bytes[1]]))
    }

    pub(super) fn i32(&mut self, field: &'static str) -> Result<i32, WireError> {
        let bytes = self.take(4, field)?;
        Ok(i32::from_be_bytes([bytes[0], bytes[1], bytes[2], bytes[3]]))
    }

    pub(super) fn i64(&mut self, field: &'static str) -> Result<i64, WireError> {
        let bytes = self.take(8, field)?;
        let mut value = [0; 8];
        value.copy_from_slice(bytes);
        Ok(i64::from_be_bytes(value))
    }

    pub(super) fn boolean(&mut self, field: &'static str) -> Result<bool, WireError> {
        match self.u8(field)? {
            0 => Ok(false),
            1 => Ok(true),
            value => Err(WireError::InvalidValue {
                field,
                value: i64::from(value),
            }),
        }
    }

    pub(super) fn binary(
        &mut self,
        maximum: usize,
        field: &'static str,
    ) -> Result<Vec<u8>, WireError> {
        let length = self.i32(field)?;
        if length < 0 || length as usize > maximum {
            return Err(WireError::InvalidLength {
                field,
                length: i64::from(length),
            });
        }
        Ok(self.take(length as usize, field)?.to_vec())
    }

    pub(super) fn utf8_u16(
        &mut self,
        maximum: usize,
        field: &'static str,
    ) -> Result<String, WireError> {
        let length = self.u16(field)? as usize;
        self.utf8(length, maximum, field)
    }

    pub(super) fn utf8_u32(
        &mut self,
        maximum: usize,
        field: &'static str,
    ) -> Result<String, WireError> {
        let length = self.u32(field)? as usize;
        self.utf8(length, maximum, field)
    }

    pub(super) fn u32(&mut self, field: &'static str) -> Result<u32, WireError> {
        let bytes = self.take(4, field)?;
        Ok(u32::from_be_bytes([bytes[0], bytes[1], bytes[2], bytes[3]]))
    }

    pub(super) fn nullable_utf8_u16(
        &mut self,
        maximum: usize,
        field: &'static str,
        presence_field: &'static str,
    ) -> Result<Option<String>, WireError> {
        if self.boolean(presence_field)? {
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

    pub(super) fn require_finished(&self) -> Result<(), WireError> {
        let remaining = self.bytes.len() - self.offset;
        if remaining == 0 {
            Ok(())
        } else {
            Err(WireError::TrailingBytes(remaining))
        }
    }
}
