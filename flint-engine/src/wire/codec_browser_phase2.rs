//! Strict payload codec for additive browser messages 21 through 31.
//!
//! These messages deliberately live beside, rather than extend, IDs 14 through 20: every browser
//! decoder rejects trailing bytes, so adding fields to an existing payload would break older v2
//! peers. All values in this file remain TLS-browser-session-only through `browser::is_browser_*`.

use super::browser_payload::{Reader, Writer};
use super::browser_phase2_validate::{validate, view_text_limit};
use super::WireError;
use crate::wire::browser::limits;
use crate::wire::{
    BrowserDarkMode, BrowserInteractionMode, BrowserLibraryAction, BrowserLibraryEntry,
    BrowserLibraryEntryKind, BrowserLoadState, BrowserNetworkAction, BrowserProfileAction,
    BrowserProfileEntry, BrowserProfileSource, BrowserSearchEngine, BrowserTabAction,
    BrowserTabStateEntry, BrowserUserAgentMode, BrowserViewAction, BrowserVpnProvider,
    BrowserVpnSessionState, Message, MessageType,
};

/// Encodes an additive browser payload, returning `None` for every other message.
pub(super) fn encode(message: &Message) -> Result<Option<Vec<u8>>, WireError> {
    if !matches!(message.type_id(), 21..=31) {
        return Ok(None);
    }
    validate(message)?;

    let mut writer = Writer::default();
    match message {
        Message::BrowserTabCommand {
            epoch,
            command_id,
            action,
            tab_id,
            url,
        } => {
            writer.i64(*epoch);
            writer.i64(*command_id);
            writer.u8(*action as u8);
            writer.i64(*tab_id);
            writer.nullable_utf8_u16(
                url.as_deref(),
                limits::MAX_URL_BYTES,
                "browser tab command URL",
            )?;
        }
        Message::BrowserTabState {
            epoch,
            revision,
            active_tab_id,
            tabs,
        } => {
            writer.i64(*epoch);
            writer.i64(*revision);
            writer.i64(*active_tab_id);
            writer.u8(tabs.len() as u8);
            for tab in tabs {
                encode_tab(&mut writer, tab)?;
            }
        }
        Message::BrowserViewCommand {
            epoch,
            command_id,
            action,
            value,
            text,
        } => {
            writer.i64(*epoch);
            writer.i64(*command_id);
            writer.u8(*action as u8);
            writer.i32(*value);
            writer.utf8_u16(text, view_text_limit(*action), "browser view command text")?;
        }
        Message::BrowserViewState {
            epoch,
            revision,
            zoom_percent,
            ua_mode,
            dark_mode,
            input_mode,
            fullscreen,
            media_playing,
            editing_focused,
            find_active,
            find_current,
            find_total,
            search_engine,
        } => {
            writer.i64(*epoch);
            writer.i64(*revision);
            writer.i32(*zoom_percent);
            writer.u8(*ua_mode as u8);
            writer.u8(*dark_mode as u8);
            writer.u8(*input_mode as u8);
            writer.boolean(*fullscreen);
            writer.boolean(*media_playing);
            writer.boolean(*editing_focused);
            writer.boolean(*find_active);
            writer.i32(*find_current);
            writer.i32(*find_total);
            writer.u8(*search_engine as u8);
        }
        Message::BrowserFavicon {
            epoch,
            favicon_id,
            width,
            height,
            png,
        } => {
            writer.i64(*epoch);
            writer.i64(*favicon_id);
            writer.u16(*width);
            writer.u16(*height);
            writer.binary(png, limits::MAX_FAVICON_BYTES, "browser favicon PNG")?;
        }
        Message::BrowserLibraryCommand {
            epoch,
            command_id,
            action,
            url,
            title,
        } => {
            writer.i64(*epoch);
            writer.i64(*command_id);
            writer.u8(*action as u8);
            writer.utf8_u16(url, limits::MAX_URL_BYTES, "browser library command URL")?;
            writer.utf8_u16(
                title,
                limits::MAX_TITLE_BYTES,
                "browser library command title",
            )?;
        }
        Message::BrowserLibraryState {
            epoch,
            revision,
            bookmarks,
            history,
        } => {
            writer.i64(*epoch);
            writer.i64(*revision);
            writer.u8(bookmarks.len() as u8);
            writer.u8(history.len() as u8);
            for entry in bookmarks.iter().chain(history) {
                encode_library_entry(&mut writer, entry)?;
            }
        }
        Message::BrowserProfileCommand {
            epoch,
            command_id,
            action,
            profile_id,
            name,
        } => {
            writer.i64(*epoch);
            writer.i64(*command_id);
            writer.u8(*action as u8);
            writer.utf8_u16(
                profile_id,
                limits::MAX_PROFILE_ID_BYTES,
                "browser profile command profile ID",
            )?;
            writer.utf8_u16(
                name,
                limits::MAX_PROFILE_NAME_BYTES,
                "browser profile command name",
            )?;
        }
        Message::BrowserProfileState {
            epoch,
            revision,
            active_source,
            active_profile_id,
            device_name,
            profiles,
        } => {
            writer.i64(*epoch);
            writer.i64(*revision);
            writer.u8(*active_source as u8);
            writer.utf8_u16(
                active_profile_id,
                limits::MAX_PROFILE_ID_BYTES,
                "browser active profile ID",
            )?;
            writer.utf8_u16(
                device_name,
                limits::MAX_DEVICE_PROFILE_NAME_BYTES,
                "browser profile device name",
            )?;
            writer.u8(profiles.len() as u8);
            for profile in profiles {
                encode_profile(&mut writer, profile)?;
            }
        }
        Message::BrowserNetworkCommand {
            epoch,
            command_id,
            action,
            profile_id,
            vpn_enabled,
            provider,
            auto_connect_on_browser_start,
            require_vpn_before_browse,
            config_text,
        } => {
            writer.i64(*epoch);
            writer.i64(*command_id);
            writer.u8(*action as u8);
            writer.utf8_u16(
                profile_id,
                limits::MAX_PROFILE_ID_BYTES,
                "browser network command profile ID",
            )?;
            writer.boolean(*vpn_enabled);
            writer.u8(*provider as u8);
            writer.boolean(*auto_connect_on_browser_start);
            writer.boolean(*require_vpn_before_browse);
            writer.utf8_u32(
                config_text,
                limits::MAX_CONFIG_UTF8_BYTES,
                "browser network command config",
            )?;
        }
        Message::BrowserNetworkState {
            epoch,
            revision,
            profile_id,
            vpn_enabled,
            provider,
            auto_connect_on_browser_start,
            require_vpn_before_browse,
            config_present,
            capability_preparable,
            capability_reason,
            session_state,
            session_detail,
        } => {
            writer.i64(*epoch);
            writer.i64(*revision);
            writer.utf8_u16(
                profile_id,
                limits::MAX_PROFILE_ID_BYTES,
                "browser network state profile ID",
            )?;
            writer.boolean(*vpn_enabled);
            writer.u8(*provider as u8);
            writer.boolean(*auto_connect_on_browser_start);
            writer.boolean(*require_vpn_before_browse);
            writer.boolean(*config_present);
            writer.boolean(*capability_preparable);
            writer.utf8_u16(
                capability_reason,
                limits::MAX_DETAIL_BYTES,
                "browser network capability reason",
            )?;
            writer.u8(*session_state as u8);
            writer.utf8_u16(
                session_detail,
                limits::MAX_DETAIL_BYTES,
                "browser network session detail",
            )?;
        }
        _ => unreachable!("phase-two browser predicate and message match diverged"),
    }
    Ok(Some(writer.finish()))
}

/// Decodes one known additive browser payload.
pub(super) fn decode(message_type: MessageType, payload: &[u8]) -> Result<Message, WireError> {
    let mut reader = Reader::new(payload);
    let message = match message_type {
        MessageType::BrowserTabCommand => Message::BrowserTabCommand {
            epoch: reader.i64("browser tab command epoch")?,
            command_id: reader.i64("browser tab command ID")?,
            action: read_enum(
                &mut reader,
                "browser tab command action",
                BrowserTabAction::from_id,
            )?,
            tab_id: reader.i64("browser tab command tab ID")?,
            url: reader.nullable_utf8_u16(
                limits::MAX_URL_BYTES,
                "browser tab command URL",
                "browser tab command URL presence",
            )?,
        },
        MessageType::BrowserTabState => {
            let epoch = reader.i64("browser tab state epoch")?;
            let revision = reader.i64("browser tab state revision")?;
            let active_tab_id = reader.i64("browser active tab ID")?;
            let count = reader.u8("browser tab count")? as usize;
            if count > limits::MAX_TABS {
                return Err(WireError::InvalidLength {
                    field: "browser tab count",
                    length: count as i64,
                });
            }
            let mut tabs = Vec::with_capacity(count);
            for _ in 0..count {
                tabs.push(decode_tab(&mut reader)?);
            }
            Message::BrowserTabState {
                epoch,
                revision,
                active_tab_id,
                tabs,
            }
        }
        MessageType::BrowserViewCommand => {
            let epoch = reader.i64("browser view command epoch")?;
            let command_id = reader.i64("browser view command ID")?;
            let action = read_enum(
                &mut reader,
                "browser view command action",
                BrowserViewAction::from_id,
            )?;
            Message::BrowserViewCommand {
                epoch,
                command_id,
                action,
                value: reader.i32("browser view command value")?,
                text: reader.utf8_u16(view_text_limit(action), "browser view command text")?,
            }
        }
        MessageType::BrowserViewState => Message::BrowserViewState {
            epoch: reader.i64("browser view state epoch")?,
            revision: reader.i64("browser view state revision")?,
            zoom_percent: reader.i32("browser zoom percent")?,
            ua_mode: read_enum(
                &mut reader,
                "browser user-agent mode",
                BrowserUserAgentMode::from_id,
            )?,
            dark_mode: read_enum(&mut reader, "browser dark mode", BrowserDarkMode::from_id)?,
            input_mode: read_enum(
                &mut reader,
                "browser input mode",
                BrowserInteractionMode::from_id,
            )?,
            fullscreen: reader.boolean("browser fullscreen flag")?,
            media_playing: reader.boolean("browser media-playing flag")?,
            editing_focused: reader.boolean("browser editing-focused flag")?,
            find_active: reader.boolean("browser find-active flag")?,
            find_current: reader.i32("browser find current")?,
            find_total: reader.i32("browser find total")?,
            search_engine: read_enum(
                &mut reader,
                "browser search engine",
                BrowserSearchEngine::from_id,
            )?,
        },
        MessageType::BrowserFavicon => Message::BrowserFavicon {
            epoch: reader.i64("browser favicon epoch")?,
            favicon_id: reader.i64("browser favicon ID")?,
            width: reader.u16("browser favicon width")?,
            height: reader.u16("browser favicon height")?,
            png: reader.binary(limits::MAX_FAVICON_BYTES, "browser favicon PNG")?,
        },
        MessageType::BrowserLibraryCommand => Message::BrowserLibraryCommand {
            epoch: reader.i64("browser library command epoch")?,
            command_id: reader.i64("browser library command ID")?,
            action: read_enum(
                &mut reader,
                "browser library command action",
                BrowserLibraryAction::from_id,
            )?,
            url: reader.utf8_u16(limits::MAX_URL_BYTES, "browser library command URL")?,
            title: reader.utf8_u16(limits::MAX_TITLE_BYTES, "browser library command title")?,
        },
        MessageType::BrowserLibraryState => {
            let epoch = reader.i64("browser library state epoch")?;
            let revision = reader.i64("browser library state revision")?;
            let bookmark_count = reader.u8("browser bookmark count")? as usize;
            let history_count = reader.u8("browser history count")? as usize;
            let mut bookmarks = Vec::with_capacity(bookmark_count);
            let mut history = Vec::with_capacity(history_count);
            for _ in 0..bookmark_count {
                bookmarks.push(decode_library_entry(&mut reader)?);
            }
            for _ in 0..history_count {
                history.push(decode_library_entry(&mut reader)?);
            }
            Message::BrowserLibraryState {
                epoch,
                revision,
                bookmarks,
                history,
            }
        }
        MessageType::BrowserProfileCommand => Message::BrowserProfileCommand {
            epoch: reader.i64("browser profile command epoch")?,
            command_id: reader.i64("browser profile command ID")?,
            action: read_enum(
                &mut reader,
                "browser profile command action",
                BrowserProfileAction::from_id,
            )?,
            profile_id: reader.utf8_u16(
                limits::MAX_PROFILE_ID_BYTES,
                "browser profile command profile ID",
            )?,
            name: reader.utf8_u16(
                limits::MAX_PROFILE_NAME_BYTES,
                "browser profile command name",
            )?,
        },
        MessageType::BrowserProfileState => {
            let epoch = reader.i64("browser profile state epoch")?;
            let revision = reader.i64("browser profile state revision")?;
            let active_source = read_enum(
                &mut reader,
                "browser active profile source",
                BrowserProfileSource::from_id,
            )?;
            let active_profile_id =
                reader.utf8_u16(limits::MAX_PROFILE_ID_BYTES, "browser active profile ID")?;
            let device_name = reader.utf8_u16(
                limits::MAX_DEVICE_PROFILE_NAME_BYTES,
                "browser profile device name",
            )?;
            let profile_count = reader.u8("browser profile count")? as usize;
            if profile_count > limits::MAX_TV_PROFILES {
                return Err(WireError::InvalidLength {
                    field: "browser profile count",
                    length: profile_count as i64,
                });
            }
            let mut profiles = Vec::with_capacity(profile_count);
            for _ in 0..profile_count {
                profiles.push(decode_profile(&mut reader)?);
            }
            Message::BrowserProfileState {
                epoch,
                revision,
                active_source,
                active_profile_id,
                device_name,
                profiles,
            }
        }
        MessageType::BrowserNetworkCommand => Message::BrowserNetworkCommand {
            epoch: reader.i64("browser network command epoch")?,
            command_id: reader.i64("browser network command ID")?,
            action: read_enum(
                &mut reader,
                "browser network command action",
                BrowserNetworkAction::from_id,
            )?,
            profile_id: reader.utf8_u16(
                limits::MAX_PROFILE_ID_BYTES,
                "browser network command profile ID",
            )?,
            vpn_enabled: reader.boolean("browser network command vpn enabled")?,
            provider: read_enum(
                &mut reader,
                "browser network command provider",
                BrowserVpnProvider::from_id,
            )?,
            auto_connect_on_browser_start: reader
                .boolean("browser network command auto-connect")?,
            require_vpn_before_browse: reader
                .boolean("browser network command require VPN before browse")?,
            config_text: reader.utf8_u32(
                limits::MAX_CONFIG_UTF8_BYTES,
                "browser network command config",
            )?,
        },
        MessageType::BrowserNetworkState => Message::BrowserNetworkState {
            epoch: reader.i64("browser network state epoch")?,
            revision: reader.i64("browser network state revision")?,
            profile_id: reader.utf8_u16(
                limits::MAX_PROFILE_ID_BYTES,
                "browser network state profile ID",
            )?,
            vpn_enabled: reader.boolean("browser network state vpn enabled")?,
            provider: read_enum(
                &mut reader,
                "browser network state provider",
                BrowserVpnProvider::from_id,
            )?,
            auto_connect_on_browser_start: reader.boolean("browser network state auto-connect")?,
            require_vpn_before_browse: reader
                .boolean("browser network state require VPN before browse")?,
            config_present: reader.boolean("browser network state config present")?,
            capability_preparable: reader.boolean("browser network state capability preparable")?,
            capability_reason: reader.utf8_u16(
                limits::MAX_DETAIL_BYTES,
                "browser network capability reason",
            )?,
            session_state: read_enum(
                &mut reader,
                "browser network session state",
                BrowserVpnSessionState::from_id,
            )?,
            session_detail: reader
                .utf8_u16(limits::MAX_DETAIL_BYTES, "browser network session detail")?,
        },
        _ => {
            return Err(WireError::Invariant(
                "unhandled phase-two browser message type",
            ))
        }
    };
    validate(&message)?;
    reader.require_finished()?;
    Ok(message)
}

fn encode_tab(writer: &mut Writer, tab: &BrowserTabStateEntry) -> Result<(), WireError> {
    writer.i64(tab.tab_id);
    writer.u8(tab.load_state as u8);
    writer.u8(tab.progress);
    writer.boolean(tab.can_go_back);
    writer.boolean(tab.can_go_forward);
    writer.boolean(tab.frozen);
    writer.i64(tab.favicon_id);
    writer.utf8_u16(&tab.url, limits::MAX_URL_BYTES, "browser tab URL")?;
    writer.utf8_u16(&tab.title, limits::MAX_TITLE_BYTES, "browser tab title")
}

fn decode_tab(reader: &mut Reader<'_>) -> Result<BrowserTabStateEntry, WireError> {
    Ok(BrowserTabStateEntry {
        tab_id: reader.i64("browser tab ID")?,
        load_state: read_enum(reader, "browser tab load state", BrowserLoadState::from_id)?,
        progress: reader.u8("browser tab progress")?,
        can_go_back: reader.boolean("browser tab can-go-back flag")?,
        can_go_forward: reader.boolean("browser tab can-go-forward flag")?,
        frozen: reader.boolean("browser tab frozen flag")?,
        favicon_id: reader.i64("browser tab favicon ID")?,
        url: reader.utf8_u16(limits::MAX_URL_BYTES, "browser tab URL")?,
        title: reader.utf8_u16(limits::MAX_TITLE_BYTES, "browser tab title")?,
    })
}

fn encode_library_entry(writer: &mut Writer, entry: &BrowserLibraryEntry) -> Result<(), WireError> {
    writer.u8(entry.kind as u8);
    writer.i64(entry.favicon_id);
    writer.i64(entry.last_visited_ms);
    writer.utf8_u16(&entry.url, limits::MAX_URL_BYTES, "browser library URL")?;
    writer.utf8_u16(
        &entry.title,
        limits::MAX_TITLE_BYTES,
        "browser library title",
    )
}

fn decode_library_entry(reader: &mut Reader<'_>) -> Result<BrowserLibraryEntry, WireError> {
    Ok(BrowserLibraryEntry {
        kind: read_enum(
            reader,
            "browser library entry kind",
            BrowserLibraryEntryKind::from_id,
        )?,
        favicon_id: reader.i64("browser library favicon ID")?,
        last_visited_ms: reader.i64("browser library last-visited time")?,
        url: reader.utf8_u16(limits::MAX_URL_BYTES, "browser library URL")?,
        title: reader.utf8_u16(limits::MAX_TITLE_BYTES, "browser library title")?,
    })
}

fn encode_profile(writer: &mut Writer, profile: &BrowserProfileEntry) -> Result<(), WireError> {
    writer.utf8_u16(
        &profile.profile_id,
        limits::MAX_PROFILE_ID_BYTES,
        "browser profile ID",
    )?;
    writer.utf8_u16(
        &profile.name,
        limits::MAX_PROFILE_NAME_BYTES,
        "browser profile name",
    )
}

fn decode_profile(reader: &mut Reader<'_>) -> Result<BrowserProfileEntry, WireError> {
    Ok(BrowserProfileEntry {
        profile_id: reader.utf8_u16(limits::MAX_PROFILE_ID_BYTES, "browser profile ID")?,
        name: reader.utf8_u16(limits::MAX_PROFILE_NAME_BYTES, "browser profile name")?,
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
