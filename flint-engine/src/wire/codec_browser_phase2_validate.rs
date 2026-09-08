//! Bounds and shape checks for browser messages 21 through 31.
//!
//! Split from the encoder so the two halves can be read for different reasons. Everything here
//! answers one question — is this payload allowed to exist — and it runs on both sides of the
//! wire: before an encode, and again after a decode. A check that only guarded one direction
//! would let a hostile or buggy peer put values into the receiver that Flint would never send.

use std::collections::HashSet;

use super::WireError;
use crate::wire::browser::{self, limits};
use crate::wire::{
    BrowserDarkMode, BrowserInteractionMode, BrowserLibraryAction, BrowserLibraryEntry,
    BrowserLibraryEntryKind, BrowserLoadState, BrowserNetworkAction, BrowserProfileAction,
    BrowserProfileSource, BrowserSearchEngine, BrowserTabAction, BrowserTabStateEntry,
    BrowserUserAgentMode, BrowserViewAction, BrowserVpnProvider, Message,
};

pub(super) fn validate(message: &Message) -> Result<(), WireError> {
    match message {
        Message::BrowserTabCommand {
            epoch,
            command_id,
            action,
            tab_id,
            url,
        } => {
            positive(*epoch, "browser tab command epoch")?;
            positive(*command_id, "browser tab command ID")?;
            if *action == BrowserTabAction::New {
                require(*tab_id == 0, "new browser tab command must use tab ID zero")?;
            } else {
                positive(*tab_id, "browser tab command tab ID")?;
            }
            require(
                (*action == BrowserTabAction::New) || url.is_none(),
                "only new browser tab command carries a URL",
            )?;
            if let Some(url) = url {
                nonblank(url, "browser tab command URL is blank")?;
                text(url, limits::MAX_URL_BYTES, "browser tab command URL")?;
            }
        }
        Message::BrowserTabState {
            epoch,
            revision,
            active_tab_id,
            tabs,
        } => {
            positive(*epoch, "browser tab state epoch")?;
            positive(*revision, "browser tab state revision")?;
            if tabs.len() > limits::MAX_TABS {
                return Err(WireError::InvalidLength {
                    field: "browser tab count",
                    length: tabs.len() as i64,
                });
            }
            require(
                (tabs.is_empty() && *active_tab_id == 0)
                    || (!tabs.is_empty() && *active_tab_id > 0),
                "browser active tab ID does not match tab presence",
            )?;
            let mut ids = HashSet::with_capacity(tabs.len());
            for tab in tabs {
                validate_tab(tab)?;
                require(ids.insert(tab.tab_id), "browser tab IDs must be unique")?;
            }
            if !tabs.is_empty() {
                require(
                    ids.contains(active_tab_id),
                    "browser active tab ID is not present",
                )?;
            }
        }
        Message::BrowserViewCommand {
            epoch,
            command_id,
            action,
            value,
            text: command_text,
        } => {
            positive(*epoch, "browser view command epoch")?;
            positive(*command_id, "browser view command ID")?;
            validate_view_command(*action, *value, command_text)?;
        }
        Message::BrowserViewState {
            epoch,
            revision,
            zoom_percent,
            find_active,
            find_current,
            find_total,
            ..
        } => {
            positive(*epoch, "browser view state epoch")?;
            positive(*revision, "browser view state revision")?;
            require(
                (limits::MIN_ZOOM_PERCENT..=limits::MAX_ZOOM_PERCENT).contains(zoom_percent),
                "browser zoom percent is out of range",
            )?;
            require(
                *find_current >= 0 && *find_total >= 0,
                "browser find counters are negative",
            )?;
            if *find_active {
                require(
                    (*find_total == 0 && *find_current == 0)
                        || (*find_total > 0 && (1..=*find_total).contains(find_current)),
                    "browser find counters are inconsistent",
                )?;
            } else {
                require(
                    *find_current == 0 && *find_total == 0,
                    "inactive browser find carries counters",
                )?;
            }
        }
        Message::BrowserFavicon {
            epoch,
            favicon_id,
            width,
            height,
            png,
        } => {
            positive(*epoch, "browser favicon epoch")?;
            positive(*favicon_id, "browser favicon ID")?;
            require(
                (1..=limits::MAX_FAVICON_DIMENSION).contains(width),
                "browser favicon width is out of range",
            )?;
            require(
                (1..=limits::MAX_FAVICON_DIMENSION).contains(height),
                "browser favicon height is out of range",
            )?;
            require(
                !png.is_empty() && png.len() <= limits::MAX_FAVICON_BYTES,
                "browser favicon PNG length is out of range",
            )?;
        }
        Message::BrowserLibraryCommand {
            epoch,
            command_id,
            action,
            url,
            title,
        } => {
            positive(*epoch, "browser library command epoch")?;
            positive(*command_id, "browser library command ID")?;
            text(url, limits::MAX_URL_BYTES, "browser library command URL")?;
            text(
                title,
                limits::MAX_TITLE_BYTES,
                "browser library command title",
            )?;
            match action {
                BrowserLibraryAction::AddBookmark => {
                    nonblank(url, "add-bookmark URL is blank")?;
                }
                BrowserLibraryAction::RemoveBookmark => {
                    nonblank(url, "remove-bookmark URL is blank")?;
                    require(title.is_empty(), "remove-bookmark command carries a title")?;
                }
                BrowserLibraryAction::ClearHistory
                | BrowserLibraryAction::ClearBookmarks
                | BrowserLibraryAction::RequestSnapshot => require(
                    url.is_empty() && title.is_empty(),
                    "parameterless browser library command carries text",
                )?,
            }
        }
        Message::BrowserLibraryState {
            epoch,
            revision,
            bookmarks,
            history,
        } => {
            positive(*epoch, "browser library state epoch")?;
            positive(*revision, "browser library state revision")?;
            count(
                bookmarks.len(),
                limits::MAX_BOOKMARKS,
                "browser bookmark count",
            )?;
            count(
                history.len(),
                limits::MAX_HISTORY_ENTRIES,
                "browser history count",
            )?;
            for entry in bookmarks {
                validate_library_entry(entry, BrowserLibraryEntryKind::Bookmark)?;
            }
            for entry in history {
                validate_library_entry(entry, BrowserLibraryEntryKind::History)?;
            }
        }
        Message::BrowserProfileCommand {
            epoch,
            command_id,
            action,
            profile_id,
            name,
        } => {
            positive(*epoch, "browser profile command epoch")?;
            positive(*command_id, "browser profile command ID")?;
            text(
                profile_id,
                limits::MAX_PROFILE_ID_BYTES,
                "browser profile command profile ID",
            )?;
            text(
                name,
                limits::MAX_PROFILE_NAME_BYTES,
                "browser profile command name",
            )?;
            match action {
                BrowserProfileAction::SelectTvProfile | BrowserProfileAction::DeleteTvProfile => {
                    validate_profile_id(profile_id)?;
                    require(name.is_empty(), "browser profile command carries a name")?;
                }
                BrowserProfileAction::CreateTvProfile => {
                    require(
                        profile_id.is_empty(),
                        "create-profile command carries a profile ID",
                    )?;
                    validate_profile_name(name)?;
                }
                BrowserProfileAction::RenameTvProfile => {
                    validate_profile_id(profile_id)?;
                    validate_profile_name(name)?;
                }
                BrowserProfileAction::SelectDevice | BrowserProfileAction::RequestSnapshot => {
                    require(
                        profile_id.is_empty() && name.is_empty(),
                        "parameterless browser profile command carries text",
                    )?;
                }
            }
        }
        Message::BrowserProfileState {
            epoch,
            revision,
            active_source,
            active_profile_id,
            device_name,
            profiles,
        } => {
            positive(*epoch, "browser profile state epoch")?;
            positive(*revision, "browser profile state revision")?;
            text(
                active_profile_id,
                limits::MAX_PROFILE_ID_BYTES,
                "browser active profile ID",
            )?;
            validate_printable(
                device_name,
                limits::MAX_DEVICE_PROFILE_NAME_BYTES,
                "browser profile device name",
            )?;
            count(
                profiles.len(),
                limits::MAX_TV_PROFILES,
                "browser profile count",
            )?;
            let mut ids = HashSet::with_capacity(profiles.len());
            let mut names = HashSet::with_capacity(profiles.len());
            for profile in profiles {
                validate_profile_id(&profile.profile_id)?;
                validate_profile_name(&profile.name)?;
                require(
                    ids.insert(profile.profile_id.as_str()),
                    "browser profile IDs must be unique",
                )?;
                require(
                    names.insert(profile.name.as_str()),
                    "browser profile names must be unique",
                )?;
            }
            if *active_source == BrowserProfileSource::Tv {
                validate_profile_id(active_profile_id)?;
                require(
                    ids.contains(active_profile_id.as_str()),
                    "active TV browser profile is not present",
                )?;
            } else {
                require(
                    active_profile_id.is_empty(),
                    "device profile source carries a TV profile ID",
                )?;
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
            positive(*epoch, "browser network command epoch")?;
            positive(*command_id, "browser network command ID")?;
            text(
                profile_id,
                limits::MAX_PROFILE_ID_BYTES,
                "browser network command profile ID",
            )?;
            text(
                config_text,
                limits::MAX_CONFIG_UTF8_BYTES,
                "browser network command config",
            )?;
            match action {
                BrowserNetworkAction::Set => {
                    validate_profile_id(profile_id)?;
                    if *vpn_enabled {
                        require(
                            *provider == BrowserVpnProvider::WireGuard,
                            "enabled VPN requires the WireGuard provider",
                        )?;
                    }
                    if !config_text.is_empty() {
                        require(
                            browser::is_valid_wireguard_config(config_text),
                            "WireGuard config is incomplete",
                        )?;
                    }
                }
                BrowserNetworkAction::Clear => {
                    validate_profile_id(profile_id)?;
                    require_network_command_fields_empty(
                        *vpn_enabled,
                        *provider,
                        *auto_connect_on_browser_start,
                        *require_vpn_before_browse,
                        config_text,
                    )?;
                }
                BrowserNetworkAction::RequestSnapshot => {
                    if !profile_id.is_empty() {
                        validate_profile_id(profile_id)?;
                    }
                    require_network_command_fields_empty(
                        *vpn_enabled,
                        *provider,
                        *auto_connect_on_browser_start,
                        *require_vpn_before_browse,
                        config_text,
                    )?;
                }
            }
        }
        Message::BrowserNetworkState {
            epoch,
            revision,
            profile_id,
            vpn_enabled: _,
            provider: _,
            auto_connect_on_browser_start: _,
            require_vpn_before_browse: _,
            config_present: _,
            capability_preparable: _,
            capability_reason,
            session_state: _,
            session_detail,
        } => {
            positive(*epoch, "browser network state epoch")?;
            positive(*revision, "browser network state revision")?;
            if !profile_id.is_empty() {
                validate_profile_id(profile_id)?;
            }
            text(
                capability_reason,
                limits::MAX_DETAIL_BYTES,
                "browser network capability reason",
            )?;
            text(
                session_detail,
                limits::MAX_DETAIL_BYTES,
                "browser network session detail",
            )?;
        }
        _ => {
            return Err(WireError::Invariant(
                "unhandled phase-two browser message type",
            ))
        }
    }
    Ok(())
}

pub(super) fn require_network_command_fields_empty(
    vpn_enabled: bool,
    provider: BrowserVpnProvider,
    auto_connect_on_browser_start: bool,
    require_vpn_before_browse: bool,
    config_text: &str,
) -> Result<(), WireError> {
    require(!vpn_enabled, "browser network command carries vpnEnabled")?;
    require(
        provider == BrowserVpnProvider::None,
        "browser network command carries a provider",
    )?;
    require(
        !auto_connect_on_browser_start,
        "browser network command carries auto-connect",
    )?;
    require(
        !require_vpn_before_browse,
        "browser network command carries require VPN before browse",
    )?;
    require(
        config_text.is_empty(),
        "browser network command carries config text",
    )
}

pub(super) fn validate_tab(tab: &BrowserTabStateEntry) -> Result<(), WireError> {
    positive(tab.tab_id, "browser tab ID")?;
    nonnegative(tab.favicon_id, "browser tab favicon ID")?;
    require(tab.progress <= 100, "browser tab progress is out of range")?;
    text(&tab.url, limits::MAX_URL_BYTES, "browser tab URL")?;
    text(&tab.title, limits::MAX_TITLE_BYTES, "browser tab title")?;
    if matches!(
        tab.load_state,
        BrowserLoadState::Loading | BrowserLoadState::Loaded | BrowserLoadState::Failed
    ) {
        nonblank(&tab.url, "active browser tab URL is blank")?;
    }
    Ok(())
}

pub(super) fn validate_view_command(
    action: BrowserViewAction,
    value: i32,
    command_text: &str,
) -> Result<(), WireError> {
    text(
        command_text,
        view_text_limit(action),
        "browser view command text",
    )?;
    match action {
        BrowserViewAction::SetZoom => {
            require(
                (limits::MIN_ZOOM_PERCENT..=limits::MAX_ZOOM_PERCENT).contains(&value),
                "browser zoom percent is out of range",
            )?;
            require(command_text.is_empty(), "set-zoom command carries text")?;
        }
        BrowserViewAction::SetUa => {
            enum_value(
                value,
                BrowserUserAgentMode::from_id,
                "browser user-agent mode",
            )?;
            require(command_text.is_empty(), "set-UA command carries text")?;
        }
        BrowserViewAction::SetDark => {
            enum_value(value, BrowserDarkMode::from_id, "browser dark mode")?;
            require(command_text.is_empty(), "set-dark command carries text")?;
        }
        BrowserViewAction::SetInputMode => {
            enum_value(value, BrowserInteractionMode::from_id, "browser input mode")?;
            require(
                command_text.is_empty(),
                "set-input-mode command carries text",
            )?;
        }
        BrowserViewAction::SetFullscreen => {
            require(
                value == 0 || value == 1,
                "browser fullscreen value is not boolean",
            )?;
            require(
                command_text.is_empty(),
                "set-fullscreen command carries text",
            )?;
        }
        BrowserViewAction::FindStart => {
            require(value == 0, "find-start command value must be zero")?;
            nonblank(command_text, "browser find text is blank")?;
        }
        BrowserViewAction::FindNext
        | BrowserViewAction::FindPrev
        | BrowserViewAction::FindClear => {
            require(value == 0, "browser find command value must be zero")?;
            require(command_text.is_empty(), "browser find command carries text")?;
        }
        BrowserViewAction::SetSearchEngine => {
            let engine = enum_value(value, BrowserSearchEngine::from_id, "browser search engine")?;
            if engine == BrowserSearchEngine::Custom {
                nonblank(command_text, "custom browser search template is blank")?;
            } else {
                require(
                    command_text.is_empty(),
                    "preset browser search-engine command carries text",
                )?;
            }
        }
    }
    Ok(())
}

pub(super) fn validate_library_entry(
    entry: &BrowserLibraryEntry,
    expected_kind: BrowserLibraryEntryKind,
) -> Result<(), WireError> {
    require(
        entry.kind == expected_kind,
        "browser library entry is in the wrong collection",
    )?;
    nonnegative(entry.favicon_id, "browser library favicon ID")?;
    nonnegative(entry.last_visited_ms, "browser library last-visited time")?;
    nonblank(&entry.url, "browser library URL is blank")?;
    text(&entry.url, limits::MAX_URL_BYTES, "browser library URL")?;
    text(
        &entry.title,
        limits::MAX_TITLE_BYTES,
        "browser library title",
    )
}

pub(super) fn validate_profile_id(profile_id: &str) -> Result<(), WireError> {
    text(
        profile_id,
        limits::MAX_PROFILE_ID_BYTES,
        "browser profile ID",
    )?;
    require(!profile_id.is_empty(), "browser profile ID is empty")?;
    require(
        profile_id
            .bytes()
            .all(|value| value.is_ascii_alphanumeric() || value == b'_' || value == b'-'),
        "browser profile ID contains unsafe characters",
    )
}

pub(super) fn validate_profile_name(name: &str) -> Result<(), WireError> {
    nonblank(name, "browser profile name is blank")?;
    require(name.trim() == name, "browser profile name is not trimmed")?;
    validate_printable(name, limits::MAX_PROFILE_NAME_BYTES, "browser profile name")
}

pub(super) fn validate_printable(
    value: &str,
    maximum: usize,
    field: &'static str,
) -> Result<(), WireError> {
    text(value, maximum, field)?;
    require(
        !value.chars().any(char::is_control),
        "browser profile text contains control characters",
    )
}

pub(super) fn view_text_limit(action: BrowserViewAction) -> usize {
    if action == BrowserViewAction::FindStart {
        limits::MAX_FIND_TEXT_BYTES
    } else {
        limits::MAX_URL_BYTES
    }
}

pub(super) fn enum_value<T>(
    value: i32,
    from_id: fn(u8) -> Option<T>,
    field: &'static str,
) -> Result<T, WireError> {
    let id = u8::try_from(value).map_err(|_| WireError::InvalidValue {
        field,
        value: i64::from(value),
    })?;
    from_id(id).ok_or(WireError::InvalidValue {
        field,
        value: i64::from(value),
    })
}

pub(super) fn positive(value: i64, field: &'static str) -> Result<(), WireError> {
    require(value > 0, field)
}

pub(super) fn nonnegative(value: i64, field: &'static str) -> Result<(), WireError> {
    require(value >= 0, field)
}

pub(super) fn nonblank(value: &str, message: &'static str) -> Result<(), WireError> {
    require(!value.trim().is_empty(), message)
}

pub(super) fn text(value: &str, maximum: usize, field: &'static str) -> Result<(), WireError> {
    if value.len() > maximum {
        return Err(WireError::InvalidLength {
            field,
            length: value.len() as i64,
        });
    }
    Ok(())
}

pub(super) fn count(value: usize, maximum: usize, field: &'static str) -> Result<(), WireError> {
    if value > maximum {
        return Err(WireError::InvalidLength {
            field,
            length: value as i64,
        });
    }
    Ok(())
}

pub(super) fn require(condition: bool, message: &'static str) -> Result<(), WireError> {
    if condition {
        Ok(())
    } else {
        Err(WireError::Invariant(message))
    }
}
