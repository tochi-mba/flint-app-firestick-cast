//! Strict payload codec for additive browser workspace messages 32 through 34.
//!
//! These messages deliberately live beside, rather than extend, earlier browser IDs: every browser
//! decoder rejects trailing bytes, so adding fields to an existing payload would break older peers.
//! All values in this file remain TLS-browser-session-only through `browser::is_browser_*`.

use std::collections::HashSet;

use super::browser_payload::{Reader, Writer};
use super::WireError;
use crate::wire::browser::limits;
use crate::wire::{
    BrowserSemanticKey, BrowserWorkspaceCommandAction, BrowserWorkspaceInputKind,
    BrowserWorkspacePaneStateEntry, BrowserWorkspaceWireInteractionMode,
    BrowserWorkspaceWireLayout, BrowserWorkspaceWireMuteApplication,
    BrowserWorkspaceWireObservedPlayback, BrowserWorkspaceWirePaneResidency, Message, MessageType,
};

/// Encodes an additive browser workspace payload, returning `None` for every other message.
pub(super) fn encode(message: &Message) -> Result<Option<Vec<u8>>, WireError> {
    if !matches!(message.type_id(), 32..=36) {
        return Ok(None);
    }
    validate(message)?;

    let mut writer = Writer::default();
    match message {
        Message::BrowserWorkspaceResize { epoch, command_id, expected_revision, column, row, mode } => {
            writer.i64(*epoch); writer.i64(*command_id); writer.i64(*expected_revision);
            writer.u16(*column); writer.u16(*row); writer.u8(*mode);
        }
        Message::BrowserWorkspaceGeometry { epoch, revision, column, row, mode } => {
            writer.i64(*epoch); writer.i64(*revision); writer.u16(*column); writer.u16(*row); writer.u8(*mode);
        }
        Message::BrowserWorkspaceCommand {
            epoch,
            command_id,
            expected_revision,
            action,
            pane_id,
            value,
            url,
        } => {
            writer.i64(*epoch);
            writer.i64(*command_id);
            writer.i64(*expected_revision);
            writer.u8(*action as u8);
            writer.i64(*pane_id);
            writer.u8(*value);
            writer.utf8_u16(url, limits::MAX_URL_BYTES, "browser workspace command URL")?;
        }
        Message::BrowserWorkspaceState {
            epoch,
            revision,
            layout,
            focused_pane_id,
            interaction_mode,
            page_fullscreen_pane_id,
            theater_pane_id,
            max_live_renderers,
            max_open_panes,
            panes,
        } => {
            writer.i64(*epoch);
            writer.i64(*revision);
            writer.u8(*layout as u8);
            writer.i64(*focused_pane_id);
            writer.u8(*interaction_mode as u8);
            writer.i64(*page_fullscreen_pane_id);
            writer.i64(*theater_pane_id);
            writer.u8(*max_live_renderers);
            writer.u8(*max_open_panes);
            writer.u8(panes.len() as u8);
            for pane in panes {
                encode_pane(&mut writer, pane)?;
            }
        }
        Message::BrowserWorkspaceInput {
            epoch,
            command_id,
            expected_revision,
            pane_id,
            kind,
            key,
            text,
        } => {
            writer.i64(*epoch);
            writer.i64(*command_id);
            writer.i64(*expected_revision);
            writer.i64(*pane_id);
            writer.u8(*kind as u8);
            match kind {
                BrowserWorkspaceInputKind::Key => {
                    writer.u8(key.ok_or(WireError::Invariant(
                        "browser workspace key input is missing a key",
                    ))? as u8);
                }
                BrowserWorkspaceInputKind::Text => {
                    writer.utf8_u16(
                        text,
                        limits::MAX_TEXT_BYTES,
                        "browser workspace input text",
                    )?;
                }
            }
        }
        _ => unreachable!("phase-three browser predicate and message match diverged"),
    }
    Ok(Some(writer.finish()))
}

/// Decodes one known additive browser workspace payload.
pub(super) fn decode(message_type: MessageType, payload: &[u8]) -> Result<Message, WireError> {
    let mut reader = Reader::new(payload);
    let message = match message_type {
        MessageType::BrowserWorkspaceResize => Message::BrowserWorkspaceResize {
            epoch: reader.i64("epoch")?, command_id: reader.i64("command ID")?,
            expected_revision: reader.i64("revision")?, column: reader.u16("column")?, row: reader.u16("row")?, mode: reader.u8("mode")?,
        },
        MessageType::BrowserWorkspaceGeometry => Message::BrowserWorkspaceGeometry {
            epoch: reader.i64("epoch")?, revision: reader.i64("revision")?,
            column: reader.u16("column")?, row: reader.u16("row")?, mode: reader.u8("mode")?,
        },
        MessageType::BrowserWorkspaceCommand => Message::BrowserWorkspaceCommand {
            epoch: reader.i64("browser workspace command epoch")?,
            command_id: reader.i64("browser workspace command ID")?,
            expected_revision: reader.i64("browser workspace command expected revision")?,
            action: read_enum(
                &mut reader,
                "browser workspace command action",
                BrowserWorkspaceCommandAction::from_id,
            )?,
            pane_id: reader.i64("browser workspace command pane ID")?,
            value: reader.u8("browser workspace command value")?,
            url: reader.utf8_u16(limits::MAX_URL_BYTES, "browser workspace command URL")?,
        },
        MessageType::BrowserWorkspaceState => {
            let epoch = reader.i64("browser workspace state epoch")?;
            let revision = reader.i64("browser workspace state revision")?;
            let layout = read_enum(
                &mut reader,
                "browser workspace layout",
                BrowserWorkspaceWireLayout::from_id,
            )?;
            let focused_pane_id = reader.i64("browser workspace focused pane ID")?;
            let interaction_mode = read_enum(
                &mut reader,
                "browser workspace interaction mode",
                BrowserWorkspaceWireInteractionMode::from_id,
            )?;
            let page_fullscreen_pane_id =
                reader.i64("browser workspace page-fullscreen pane ID")?;
            let theater_pane_id = reader.i64("browser workspace theater pane ID")?;
            let max_live_renderers = reader.u8("browser workspace max live renderers")?;
            let max_open_panes = reader.u8("browser workspace max open panes")?;
            let count = reader.u8("browser workspace pane count")? as usize;
            if count > limits::MAX_WORKSPACE_PANES {
                return Err(WireError::InvalidLength {
                    field: "browser workspace pane count",
                    length: count as i64,
                });
            }
            let mut panes = Vec::with_capacity(count);
            for _ in 0..count {
                panes.push(decode_pane(&mut reader)?);
            }
            Message::BrowserWorkspaceState {
                epoch,
                revision,
                layout,
                focused_pane_id,
                interaction_mode,
                page_fullscreen_pane_id,
                theater_pane_id,
                max_live_renderers,
                max_open_panes,
                panes,
            }
        }
        MessageType::BrowserWorkspaceInput => {
            let epoch = reader.i64("browser workspace input epoch")?;
            let command_id = reader.i64("browser workspace input command ID")?;
            let expected_revision = reader.i64("browser workspace input expected revision")?;
            let pane_id = reader.i64("browser workspace input pane ID")?;
            let kind = read_enum(
                &mut reader,
                "browser workspace input kind",
                BrowserWorkspaceInputKind::from_id,
            )?;
            match kind {
                BrowserWorkspaceInputKind::Key => Message::BrowserWorkspaceInput {
                    epoch,
                    command_id,
                    expected_revision,
                    pane_id,
                    kind,
                    key: Some(read_enum(
                        &mut reader,
                        "browser workspace input key",
                        BrowserSemanticKey::from_id,
                    )?),
                    text: String::new(),
                },
                BrowserWorkspaceInputKind::Text => Message::BrowserWorkspaceInput {
                    epoch,
                    command_id,
                    expected_revision,
                    pane_id,
                    kind,
                    key: None,
                    text: reader
                        .utf8_u16(limits::MAX_TEXT_BYTES, "browser workspace input text")?,
                },
            }
        }
        _ => {
            return Err(WireError::Invariant(
                "unhandled phase-three browser message type",
            ))
        }
    };
    validate(&message)?;
    reader.require_finished()?;
    Ok(message)
}

fn encode_pane(
    writer: &mut Writer,
    pane: &BrowserWorkspacePaneStateEntry,
) -> Result<(), WireError> {
    writer.i64(pane.pane_id);
    writer.u8(pane.slot);
    writer.u8(pane.residency as u8);
    writer.utf8_u16(
        &pane.url,
        limits::MAX_URL_BYTES,
        "browser workspace pane URL",
    )?;
    writer.utf8_u16(
        &pane.title,
        limits::MAX_TITLE_BYTES,
        "browser workspace pane title",
    )?;
    writer.boolean(pane.loading);
    writer.u8(pane.progress);
    writer.boolean(pane.can_go_back);
    writer.boolean(pane.can_go_forward);
    writer.boolean(pane.desired_muted);
    writer.u8(pane.mute_application as u8);
    writer.u8(pane.observed_playback as u8);
    Ok(())
}

fn decode_pane(reader: &mut Reader<'_>) -> Result<BrowserWorkspacePaneStateEntry, WireError> {
    Ok(BrowserWorkspacePaneStateEntry {
        pane_id: reader.i64("browser workspace pane ID")?,
        slot: reader.u8("browser workspace pane slot")?,
        residency: read_enum(
            reader,
            "browser workspace pane residency",
            BrowserWorkspaceWirePaneResidency::from_id,
        )?,
        url: reader.utf8_u16(limits::MAX_URL_BYTES, "browser workspace pane URL")?,
        title: reader.utf8_u16(limits::MAX_TITLE_BYTES, "browser workspace pane title")?,
        loading: reader.boolean("browser workspace pane loading flag")?,
        progress: reader.u8("browser workspace pane progress")?,
        can_go_back: reader.boolean("browser workspace pane can-go-back flag")?,
        can_go_forward: reader.boolean("browser workspace pane can-go-forward flag")?,
        desired_muted: reader.boolean("browser workspace pane desired-muted flag")?,
        mute_application: read_enum(
            reader,
            "browser workspace pane mute application",
            BrowserWorkspaceWireMuteApplication::from_id,
        )?,
        observed_playback: read_enum(
            reader,
            "browser workspace pane observed playback",
            BrowserWorkspaceWireObservedPlayback::from_id,
        )?,
    })
}

fn read_enum<T>(
    reader: &mut Reader<'_>,
    field: &'static str,
    from_id: fn(u8) -> Option<T>,
) -> Result<T, WireError> {
    let value = reader.u8(field)?;
    from_id(value).ok_or(WireError::InvalidValue {
        field,
        value: i64::from(value),
    })
}

fn validate(message: &Message) -> Result<(), WireError> {
    match message {
        Message::BrowserWorkspaceResize { epoch, command_id, expected_revision, column, row, mode } => {
            positive(*command_id, "command ID")?;
            validate_geometry(*epoch, *expected_revision, *column, *row, *mode)?;
        }
        Message::BrowserWorkspaceGeometry { epoch, revision, column, row, mode } => {
            validate_geometry(*epoch, *revision, *column, *row, *mode)?;
        }
        Message::BrowserWorkspaceCommand {
            epoch,
            command_id,
            expected_revision,
            action,
            pane_id,
            value,
            url,
        } => {
            positive(*epoch, "browser workspace command epoch")?;
            positive(*command_id, "browser workspace command ID")?;
            positive(
                *expected_revision,
                "browser workspace command expected revision",
            )?;
            text(url, limits::MAX_URL_BYTES, "browser workspace command URL")?;
            match action {
                BrowserWorkspaceCommandAction::Focus
                | BrowserWorkspaceCommandAction::ClosePane
                | BrowserWorkspaceCommandAction::Reload
                | BrowserWorkspaceCommandAction::Back
                | BrowserWorkspaceCommandAction::Forward
                | BrowserWorkspaceCommandAction::PlayPause
                | BrowserWorkspaceCommandAction::EnterTheater => {
                    positive(*pane_id, "browser workspace command pane ID")?;
                    require(
                        *value == 0,
                        "pane-targeted browser workspace command carries a value",
                    )?;
                    require(
                        url.is_empty(),
                        "pane-targeted browser workspace command carries a URL",
                    )?;
                }
                BrowserWorkspaceCommandAction::OpenPane => {
                    require(*pane_id == 0, "open-pane command carries a pane ID")?;
                    require(*value == 0, "open-pane command carries a value")?;
                }
                BrowserWorkspaceCommandAction::SetLayout => {
                    require(*pane_id == 0, "set-layout command carries a pane ID")?;
                    enum_value(
                        *value,
                        BrowserWorkspaceWireLayout::from_id,
                        "browser workspace layout",
                    )?;
                    require(url.is_empty(), "set-layout command carries a URL")?;
                }
                BrowserWorkspaceCommandAction::Navigate => {
                    positive(*pane_id, "browser workspace command pane ID")?;
                    require(*value == 0, "navigate command carries a value")?;
                    nonblank(url, "browser workspace navigate URL is blank")?;
                }
                BrowserWorkspaceCommandAction::SetMute => {
                    positive(*pane_id, "browser workspace command pane ID")?;
                    require(
                        *value == 0 || *value == 1,
                        "browser workspace mute value is not boolean",
                    )?;
                    require(url.is_empty(), "set-mute command carries a URL")?;
                }
                BrowserWorkspaceCommandAction::SetInteraction => {
                    require(*pane_id == 0, "set-interaction command carries a pane ID")?;
                    enum_value(
                        *value,
                        BrowserWorkspaceWireInteractionMode::from_id,
                        "browser workspace interaction mode",
                    )?;
                    require(url.is_empty(), "set-interaction command carries a URL")?;
                }
                BrowserWorkspaceCommandAction::ExitTheater
                | BrowserWorkspaceCommandAction::RequestSnapshot => {
                    require(
                        *pane_id == 0,
                        "parameterless browser workspace command carries a pane ID",
                    )?;
                    require(
                        *value == 0,
                        "parameterless browser workspace command carries a value",
                    )?;
                    require(
                        url.is_empty(),
                        "parameterless browser workspace command carries a URL",
                    )?;
                }
                BrowserWorkspaceCommandAction::MovePane => {
                    positive(*pane_id, "browser workspace command pane ID")?;
                    require(*value <= 3, "move-pane command slot is out of range")?;
                    require(url.is_empty(), "move-pane command carries a URL")?;
                }
            }
        }
        Message::BrowserWorkspaceState {
            epoch,
            revision,
            layout: _,
            focused_pane_id,
            interaction_mode: _,
            page_fullscreen_pane_id,
            theater_pane_id,
            max_live_renderers,
            max_open_panes,
            panes,
        } => {
            positive(*epoch, "browser workspace state epoch")?;
            positive(*revision, "browser workspace state revision")?;
            count(
                panes.len(),
                limits::MAX_WORKSPACE_PANES,
                "browser workspace pane count",
            )?;
            require(
                *max_live_renderers > 0,
                "browser workspace max live renderers must be positive",
            )?;
            require(
                *max_open_panes > 0,
                "browser workspace max open panes must be positive",
            )?;
            require(
                *max_live_renderers <= *max_open_panes,
                "browser workspace max live renderers exceeds max open panes",
            )?;

            let mut pane_ids = HashSet::with_capacity(panes.len());
            for pane in panes {
                validate_pane(pane)?;
                require(
                    pane_ids.insert(pane.pane_id),
                    "browser workspace pane IDs must be unique",
                )?;
            }

            if panes.is_empty() {
                require(
                    *focused_pane_id == 0,
                    "empty browser workspace carries a focused pane ID",
                )?;
                require(
                    *page_fullscreen_pane_id == 0,
                    "empty browser workspace carries page fullscreen",
                )?;
                require(
                    *theater_pane_id == 0,
                    "empty browser workspace carries theatre mode",
                )?;
            } else {
                if *focused_pane_id > 0 {
                    require(
                        pane_ids.contains(focused_pane_id),
                        "browser workspace focused pane is not present",
                    )?;
                }
                if *page_fullscreen_pane_id > 0 {
                    require(
                        pane_ids.contains(page_fullscreen_pane_id),
                        "browser workspace page-fullscreen pane is not present",
                    )?;
                }
                if *theater_pane_id > 0 {
                    require(
                        pane_ids.contains(theater_pane_id),
                        "browser workspace theatre pane is not present",
                    )?;
                }
            }
        }
        Message::BrowserWorkspaceInput {
            epoch,
            command_id,
            expected_revision,
            pane_id,
            kind,
            key,
            text: input_text,
        } => {
            positive(*epoch, "browser workspace input epoch")?;
            positive(*command_id, "browser workspace input command ID")?;
            positive(
                *expected_revision,
                "browser workspace input expected revision",
            )?;
            positive(*pane_id, "browser workspace input pane ID")?;
            match kind {
                BrowserWorkspaceInputKind::Key => {
                    require(
                        key.is_some(),
                        "browser workspace key input is missing a key",
                    )?;
                    require(
                        input_text.is_empty(),
                        "browser workspace key input carries text",
                    )?;
                }
                BrowserWorkspaceInputKind::Text => {
                    require(key.is_none(), "browser workspace text input carries a key")?;
                    nonblank(input_text, "browser workspace input text is blank")?;
                    text(
                        input_text,
                        limits::MAX_TEXT_BYTES,
                        "browser workspace input text",
                    )?;
                }
            }
        }
        _ => {
            return Err(WireError::Invariant(
                "unhandled phase-three browser message type",
            ))
        }
    }
    Ok(())
}

fn validate_pane(pane: &BrowserWorkspacePaneStateEntry) -> Result<(), WireError> {
    positive(pane.pane_id, "browser workspace pane ID")?;
    require(
        pane.progress <= 100,
        "browser workspace pane progress is out of range",
    )?;
    text(
        &pane.url,
        limits::MAX_URL_BYTES,
        "browser workspace pane URL",
    )?;
    text(
        &pane.title,
        limits::MAX_TITLE_BYTES,
        "browser workspace pane title",
    )?;
    if matches!(
        pane.residency,
        BrowserWorkspaceWirePaneResidency::Live | BrowserWorkspaceWirePaneResidency::Failed
    ) {
        nonblank(&pane.url, "active browser workspace pane URL is blank")?;
    }
    Ok(())
}

fn enum_value<T>(
    value: u8,
    from_id: fn(u8) -> Option<T>,
    field: &'static str,
) -> Result<T, WireError> {
    from_id(value).ok_or(WireError::InvalidValue {
        field,
        value: i64::from(value),
    })
}

fn positive(value: i64, field: &'static str) -> Result<(), WireError> {
    require(value > 0, field)
}

fn nonblank(value: &str, message: &'static str) -> Result<(), WireError> {
    require(!value.trim().is_empty(), message)
}

fn text(value: &str, maximum: usize, field: &'static str) -> Result<(), WireError> {
    if value.len() > maximum {
        return Err(WireError::InvalidLength {
            field,
            length: value.len() as i64,
        });
    }
    Ok(())
}

fn count(value: usize, maximum: usize, field: &'static str) -> Result<(), WireError> {
    if value > maximum {
        return Err(WireError::InvalidLength {
            field,
            length: value as i64,
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

fn validate_geometry(epoch: i64, revision: i64, column: u16, row: u16, mode: u8) -> Result<(), WireError> {
    positive(epoch, "epoch")?;
    positive(revision, "revision")?;
    if mode > 2 || !(1500..=8500).contains(&column) || !(1500..=8500).contains(&row) {
        return Err(WireError::Invariant("Invalid workspace geometry"));
    }
    Ok(())
}
