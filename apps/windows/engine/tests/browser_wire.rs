//! Deterministic v2 browser-wire coverage independent of a Fire TV or WebView.

use flint_engine::wire::browser;
use flint_engine::wire::codec::{self, WireError};
use flint_engine::wire::{
    BrowserCapabilityStatus, BrowserCommandAction, BrowserDarkMode, BrowserDialogType,
    BrowserInputEvent, BrowserInteractionMode, BrowserLibraryAction, BrowserLibraryEntry,
    BrowserLibraryEntryKind, BrowserLoadState, BrowserPointerAction, BrowserPreviewState,
    BrowserSearchEngine, BrowserSemanticKey, BrowserTabAction, BrowserTabStateEntry,
    BrowserUserAgentMode, BrowserViewAction, Frame, MediaAction, Message, PlaybackState,
    SurfaceMode,
};

fn assert_round_trip(message: Message) {
    assert_round_trip_at_version(message, 2);
}

fn assert_round_trip_at_version(message: Message, protocol_version: u16) {
    let frame = Frame {
        protocol_version,
        message: message.clone(),
        flags: 0,
    };
    let bytes = codec::encode(&frame).expect("browser frame encodes");
    let decoded = codec::decode(&bytes).expect("browser frame decodes");
    assert_eq!(decoded, frame);
}

#[test]
fn browser_messages_round_trip_with_the_v2_contract() {
    assert_round_trip(Message::BrowserCapability {
        status: BrowserCapabilityStatus::Available,
        secure_endpoint_port: 8443,
        api_level: 25,
        web_view_version: "120.0.1".into(),
        preview_supported: true,
        preview_max_width: 960,
        preview_max_height: 540,
        interactive_preview_frames_per_second: 5,
        idle_preview_frames_per_second: 1,
        preview_max_bytes: 768 * 1024,
        detail: "ready".into(),
    });
    assert_round_trip(Message::BrowserCommand {
        epoch: 1,
        command_id: 2,
        action: BrowserCommandAction::Open,
        url: Some("https://example.test/start".into()),
        preview_enabled: None,
    });
    assert_round_trip(Message::BrowserCommand {
        epoch: 1,
        command_id: 3,
        action: BrowserCommandAction::SetPreviewEnabled,
        url: None,
        preview_enabled: Some(true),
    });
    assert_round_trip(Message::BrowserInput {
        epoch: 1,
        sequence: 1,
        event: BrowserInputEvent::Pointer {
            action: BrowserPointerAction::Down,
            navigation_id: 3,
            frame_id: 4,
            x: 32_767,
            y: 65_535,
            buttons: 1,
        },
    });
    assert_round_trip(Message::BrowserInput {
        epoch: 1,
        sequence: 2,
        event: BrowserInputEvent::Scroll {
            navigation_id: 3,
            frame_id: 4,
            x: 1,
            y: 2,
            delta_x: 0,
            delta_y: -120,
        },
    });
    assert_round_trip(Message::BrowserInput {
        epoch: 1,
        sequence: 3,
        event: BrowserInputEvent::SemanticKey(BrowserSemanticKey::Select),
    });
    assert_round_trip(Message::BrowserInput {
        epoch: 1,
        sequence: 4,
        event: BrowserInputEvent::Text("A browser input value".into()),
    });
    assert_round_trip(Message::BrowserState {
        epoch: 1,
        revision: 7,
        navigation_id: 3,
        last_accepted_command_id: 3,
        last_accepted_input_sequence: 4,
        load_state: BrowserLoadState::Loaded,
        url: "https://example.test/start".into(),
        title: "Example".into(),
        progress: 100,
        can_go_back: true,
        can_go_forward: false,
        viewport_width: 1920,
        viewport_height: 1080,
        preview_state: BrowserPreviewState::Enabled,
        error_detail: String::new(),
    });
    assert_round_trip(Message::BrowserPreview {
        epoch: 1,
        navigation_id: 3,
        frame_id: 4,
        width: 320,
        height: 180,
        jpeg: vec![0xff, 0xd8, 0xff, 0xd9],
    });
    assert_round_trip(Message::BrowserDialog {
        epoch: 1,
        dialog_id: 9,
        dialog_type: BrowserDialogType::Prompt,
        origin: "https://example.test".into(),
        message: "Name".into(),
        default_value: "Guest".into(),
        timeout_milliseconds: 60_000,
    });
    assert_round_trip(Message::BrowserDialogReply {
        epoch: 1,
        dialog_id: 9,
        accepted: true,
        prompt_text: Some("Ada".into()),
    });
}

#[test]
fn browser_command_has_a_stable_byte_vector() {
    let frame = Frame {
        protocol_version: 2,
        flags: 0,
        message: Message::BrowserCommand {
            epoch: 1,
            command_id: 2,
            action: BrowserCommandAction::Open,
            url: Some("https://example.com".into()),
            preview_enabled: None,
        },
    };
    let expected = vec![
        0, 0, 0, 46, 0x52, 0x43, 0, 2, 0, 15, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 2,
        1, 0, 19, b'h', b't', b't', b'p', b's', b':', b'/', b'/', b'e', b'x', b'a', b'm', b'p',
        b'l', b'e', b'.', b'c', b'o', b'm',
    ];

    assert_eq!(codec::encode(&frame).expect("command encodes"), expected);
    assert_eq!(codec::decode(&expected).expect("command decodes"), frame);
}

#[test]
fn browser_values_are_rejected_before_v2_and_on_the_ordinary_channel() {
    let browser_command = Message::BrowserCommand {
        epoch: 1,
        command_id: 1,
        action: BrowserCommandAction::Close,
        url: None,
        preview_enabled: None,
    };
    assert!(matches!(
        codec::encode(&Frame {
            protocol_version: 1,
            flags: 0,
            message: browser_command.clone(),
        }),
        Err(WireError::Invariant(_))
    ));
    assert!(browser::is_forbidden_on_ordinary_channel(&browser_command));

    let browser_surface = Message::Surface {
        mode: SurfaceMode::Browser,
        caption: String::new(),
    };
    assert!(matches!(
        codec::encode(&Frame {
            protocol_version: 1,
            flags: 0,
            message: browser_surface.clone(),
        }),
        Err(WireError::Invariant(_))
    ));
    assert!(browser::is_forbidden_on_ordinary_channel(&browser_surface));
}

#[test]
fn browser_decoder_rejects_a_v1_browser_frame_and_invalid_pointer_transition() {
    let frame = Frame::new(Message::BrowserCommand {
        epoch: 1,
        command_id: 1,
        action: BrowserCommandAction::Close,
        url: None,
        preview_enabled: None,
    });
    let mut bytes = codec::encode(&frame).expect("browser command encodes");
    bytes[6] = 0;
    bytes[7] = 1;
    assert!(matches!(
        codec::decode(&bytes),
        Err(WireError::Invariant(_))
    ));

    let invalid_pointer = Frame::new(Message::BrowserInput {
        epoch: 1,
        sequence: 1,
        event: BrowserInputEvent::Pointer {
            action: BrowserPointerAction::Down,
            navigation_id: 1,
            frame_id: 1,
            x: 0,
            y: 0,
            buttons: 0,
        },
    });
    assert!(matches!(
        codec::encode(&invalid_pointer),
        Err(WireError::Invariant(_))
    ));
}

#[test]
fn v1_media_messages_remain_compatible() {
    assert_round_trip_at_version(
        Message::MediaCommand {
            action: MediaAction::Load,
            url: String::new(),
            title: "Pushed clip".into(),
            mime_type: "video/mp4".into(),
            duration_ms: -1,
            start_position_ms: 0,
            subtitle_url: None,
        },
        1,
    );
    assert_round_trip_at_version(
        Message::MediaData {
            data: vec![1, 2, 3],
            is_final: true,
        },
        1,
    );
    assert_round_trip_at_version(
        Message::Surface {
            mode: SurfaceMode::Presentation,
            caption: "Slides".into(),
        },
        1,
    );
    assert_round_trip_at_version(
        Message::PlaybackState {
            state: PlaybackState::Playing,
            position_ms: 10,
            duration_ms: 100,
            detail: String::new(),
        },
        1,
    );
}

#[test]
fn additive_browser_messages_and_every_new_enum_value_round_trip() {
    for (index, action) in [
        BrowserTabAction::New,
        BrowserTabAction::Close,
        BrowserTabAction::Select,
        BrowserTabAction::Move,
        BrowserTabAction::Duplicate,
    ]
    .into_iter()
    .enumerate()
    {
        assert_round_trip(Message::BrowserTabCommand {
            epoch: 2,
            command_id: 21 + index as i64,
            action,
            tab_id: if action == BrowserTabAction::New {
                0
            } else {
                7 + index as i64
            },
            url: (action == BrowserTabAction::New).then(|| "https://example.test/new".into()),
        });
    }

    assert_round_trip(tab_state());
    assert_round_trip(Message::BrowserTabState {
        epoch: 2,
        revision: 32,
        active_tab_id: 0,
        tabs: vec![],
    });

    let view_commands = [
        (40, BrowserViewAction::SetZoom, 125, ""),
        (
            41,
            BrowserViewAction::SetUa,
            BrowserUserAgentMode::Tv as i32,
            "",
        ),
        (
            42,
            BrowserViewAction::SetUa,
            BrowserUserAgentMode::Desktop as i32,
            "",
        ),
        (
            43,
            BrowserViewAction::SetUa,
            BrowserUserAgentMode::Mobile as i32,
            "",
        ),
        (
            44,
            BrowserViewAction::SetDark,
            BrowserDarkMode::FollowSystem as i32,
            "",
        ),
        (
            45,
            BrowserViewAction::SetDark,
            BrowserDarkMode::Light as i32,
            "",
        ),
        (
            46,
            BrowserViewAction::SetDark,
            BrowserDarkMode::Dark as i32,
            "",
        ),
        (
            47,
            BrowserViewAction::SetInputMode,
            BrowserInteractionMode::Cursor as i32,
            "",
        ),
        (
            48,
            BrowserViewAction::SetInputMode,
            BrowserInteractionMode::Focus as i32,
            "",
        ),
        (49, BrowserViewAction::SetFullscreen, 1, ""),
        (50, BrowserViewAction::FindStart, 0, "needle"),
        (51, BrowserViewAction::FindNext, 0, ""),
        (52, BrowserViewAction::FindPrev, 0, ""),
        (53, BrowserViewAction::FindClear, 0, ""),
        (
            54,
            BrowserViewAction::SetSearchEngine,
            BrowserSearchEngine::DuckDuckGo as i32,
            "",
        ),
        (
            55,
            BrowserViewAction::SetSearchEngine,
            BrowserSearchEngine::Google as i32,
            "",
        ),
        (
            56,
            BrowserViewAction::SetSearchEngine,
            BrowserSearchEngine::Bing as i32,
            "",
        ),
        (
            57,
            BrowserViewAction::SetSearchEngine,
            BrowserSearchEngine::Custom as i32,
            "https://search.example/?q={q}",
        ),
    ];
    for (command_id, action, value, text) in view_commands {
        assert_round_trip(Message::BrowserViewCommand {
            epoch: 2,
            command_id,
            action,
            value,
            text: text.into(),
        });
    }

    for message in [
        view_state(),
        favicon(),
        library_state(),
        Message::BrowserLibraryState {
            epoch: 2,
            revision: 81,
            bookmarks: vec![],
            history: vec![],
        },
    ] {
        assert_round_trip(message);
    }

    for (index, action) in [
        BrowserLibraryAction::AddBookmark,
        BrowserLibraryAction::RemoveBookmark,
        BrowserLibraryAction::ClearHistory,
        BrowserLibraryAction::ClearBookmarks,
        BrowserLibraryAction::RequestSnapshot,
    ]
    .into_iter()
    .enumerate()
    {
        let carries_url = matches!(
            action,
            BrowserLibraryAction::AddBookmark | BrowserLibraryAction::RemoveBookmark
        );
        assert_round_trip(Message::BrowserLibraryCommand {
            epoch: 2,
            command_id: 70 + index as i64,
            action,
            url: if carries_url {
                "https://example.test/bookmark".into()
            } else {
                String::new()
            },
            title: if action == BrowserLibraryAction::AddBookmark {
                "Example".into()
            } else {
                String::new()
            },
        });
    }
}

#[test]
fn each_additive_browser_type_requires_v2_and_is_plain_channel_forbidden() {
    for message in phase_two_representatives() {
        assert!(matches!(
            codec::encode(&Frame {
                protocol_version: 1,
                flags: 0,
                message: message.clone(),
            }),
            Err(WireError::Invariant(_))
        ));
        assert!(browser::is_browser_message(&message));
        assert!(browser::is_browser_type(message.type_id()));
        assert!(browser::is_forbidden_on_ordinary_channel(&message));
    }
}

#[test]
fn each_additive_browser_decoder_rejects_trailing_payload_bytes() {
    for message in phase_two_representatives() {
        let mut bytes = codec::encode(&Frame::new(message)).expect("phase-two frame encodes");
        let body_length = u32::from_be_bytes(bytes[0..4].try_into().expect("length prefix"));
        bytes[0..4].copy_from_slice(&(body_length + 1).to_be_bytes());
        bytes.push(0);
        assert!(matches!(
            codec::decode(&bytes),
            Err(WireError::TrailingBytes(1))
        ));
    }
}

#[test]
fn additive_browser_decoders_reject_unknown_enum_tags() {
    let mutations = [
        (
            Message::BrowserTabCommand {
                epoch: 2,
                command_id: 21,
                action: BrowserTabAction::Close,
                tab_id: 7,
                url: None,
            },
            16,
        ),
        (tab_state(), 33),
        (
            Message::BrowserViewCommand {
                epoch: 2,
                command_id: 40,
                action: BrowserViewAction::SetZoom,
                value: 125,
                text: String::new(),
            },
            16,
        ),
        (view_state(), 20),
        (
            Message::BrowserLibraryCommand {
                epoch: 2,
                command_id: 70,
                action: BrowserLibraryAction::AddBookmark,
                url: "https://example.test".into(),
                title: "Example".into(),
            },
            16,
        ),
        (library_state(), 18),
    ];

    for (message, payload_offset) in mutations {
        let mut bytes = codec::encode(&Frame::new(message)).expect("valid frame encodes");
        bytes[12 + payload_offset] = 0;
        assert!(matches!(
            codec::decode(&bytes),
            Err(WireError::InvalidValue { .. })
        ));
    }
}

#[test]
fn additive_browser_bounds_and_action_invariants_are_rejected() {
    let too_many_tabs = (1..=9)
        .map(|tab_id| BrowserTabStateEntry {
            tab_id,
            load_state: BrowserLoadState::Idle,
            progress: 0,
            can_go_back: false,
            can_go_forward: false,
            frozen: false,
            favicon_id: 0,
            url: String::new(),
            title: String::new(),
        })
        .collect();
    let invalid = [
        Message::BrowserTabCommand {
            epoch: 2,
            command_id: 1,
            action: BrowserTabAction::New,
            tab_id: 1,
            url: None,
        },
        Message::BrowserTabState {
            epoch: 2,
            revision: 1,
            active_tab_id: 1,
            tabs: too_many_tabs,
        },
        Message::BrowserViewCommand {
            epoch: 2,
            command_id: 1,
            action: BrowserViewAction::SetZoom,
            value: 49,
            text: String::new(),
        },
        Message::BrowserViewState {
            epoch: 2,
            revision: 1,
            zoom_percent: 125,
            ua_mode: BrowserUserAgentMode::Tv,
            dark_mode: BrowserDarkMode::Light,
            input_mode: BrowserInteractionMode::Cursor,
            fullscreen: false,
            media_playing: false,
            editing_focused: false,
            find_active: false,
            find_current: 1,
            find_total: 1,
            search_engine: BrowserSearchEngine::DuckDuckGo,
        },
        Message::BrowserFavicon {
            epoch: 2,
            favicon_id: 1,
            width: 65,
            height: 1,
            png: vec![1],
        },
        Message::BrowserLibraryCommand {
            epoch: 2,
            command_id: 1,
            action: BrowserLibraryAction::ClearHistory,
            url: "https://example.test".into(),
            title: String::new(),
        },
        Message::BrowserLibraryState {
            epoch: 2,
            revision: 1,
            bookmarks: vec![BrowserLibraryEntry {
                kind: BrowserLibraryEntryKind::History,
                favicon_id: 0,
                last_visited_ms: 0,
                url: "https://example.test".into(),
                title: String::new(),
            }],
            history: vec![],
        },
    ];

    for message in invalid {
        assert!(codec::encode(&Frame::new(message)).is_err());
    }
}

#[test]
fn arbitrary_phase_two_payloads_never_panic() {
    let mut state = 0x7a_d4_12_83_u32;
    for type_id in 21_u16..=29 {
        for length in 0_usize..=96 {
            let mut payload = vec![0_u8; length];
            for byte in &mut payload {
                state = state.wrapping_mul(1_664_525).wrapping_add(1_013_904_223);
                *byte = (state >> 24) as u8;
            }
            let mut bytes = Vec::with_capacity(12 + length);
            bytes.extend_from_slice(&(8_u32 + length as u32).to_be_bytes());
            bytes.extend_from_slice(&0x5243_u16.to_be_bytes());
            bytes.extend_from_slice(&2_u16.to_be_bytes());
            bytes.extend_from_slice(&type_id.to_be_bytes());
            bytes.extend_from_slice(&0_u16.to_be_bytes());
            bytes.extend_from_slice(&payload);
            assert!(std::panic::catch_unwind(|| codec::decode(&bytes)).is_ok());
        }
    }
}

fn phase_two_representatives() -> [Message; 7] {
    [
        Message::BrowserTabCommand {
            epoch: 2,
            command_id: 21,
            action: BrowserTabAction::New,
            tab_id: 0,
            url: Some("https://example.test/new".into()),
        },
        tab_state(),
        Message::BrowserViewCommand {
            epoch: 2,
            command_id: 40,
            action: BrowserViewAction::SetZoom,
            value: 125,
            text: String::new(),
        },
        view_state(),
        favicon(),
        Message::BrowserLibraryCommand {
            epoch: 2,
            command_id: 70,
            action: BrowserLibraryAction::AddBookmark,
            url: "https://example.test".into(),
            title: "Example".into(),
        },
        library_state(),
    ]
}

fn tab_state() -> Message {
    Message::BrowserTabState {
        epoch: 2,
        revision: 31,
        active_tab_id: 7,
        tabs: vec![
            BrowserTabStateEntry {
                tab_id: 7,
                load_state: BrowserLoadState::Loaded,
                progress: 100,
                can_go_back: true,
                can_go_forward: false,
                frozen: false,
                favicon_id: 41,
                url: "https://example.test/a".into(),
                title: "Alpha".into(),
            },
            BrowserTabStateEntry {
                tab_id: 8,
                load_state: BrowserLoadState::Loading,
                progress: 37,
                can_go_back: false,
                can_go_forward: true,
                frozen: true,
                favicon_id: 0,
                url: "https://example.test/b".into(),
                title: "Beta".into(),
            },
        ],
    }
}

fn view_state() -> Message {
    Message::BrowserViewState {
        epoch: 2,
        revision: 60,
        zoom_percent: 125,
        ua_mode: BrowserUserAgentMode::Desktop,
        dark_mode: BrowserDarkMode::Dark,
        input_mode: BrowserInteractionMode::Focus,
        fullscreen: true,
        media_playing: true,
        editing_focused: true,
        find_active: true,
        find_current: 2,
        find_total: 5,
        search_engine: BrowserSearchEngine::Bing,
    }
}

fn favicon() -> Message {
    Message::BrowserFavicon {
        epoch: 2,
        favicon_id: 61,
        width: 2,
        height: 2,
        png: vec![0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a],
    }
}

fn library_state() -> Message {
    Message::BrowserLibraryState {
        epoch: 2,
        revision: 80,
        bookmarks: vec![BrowserLibraryEntry {
            kind: BrowserLibraryEntryKind::Bookmark,
            favicon_id: 61,
            last_visited_ms: 1_700_000_000_000,
            url: "https://example.test/bookmark".into(),
            title: "Example".into(),
        }],
        history: vec![BrowserLibraryEntry {
            kind: BrowserLibraryEntryKind::History,
            favicon_id: 0,
            last_visited_ms: 1_700_000_001_234,
            url: "https://example.test/recent".into(),
            title: "Recent".into(),
        }],
    }
}
