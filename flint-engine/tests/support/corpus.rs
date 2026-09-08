//! The canonical corpus: every case the committed vectors are generated from.
//!
//! Held apart from the assertions in `golden.rs` so the list can grow without the tests scrolling
//! away from it. Adding a message type without adding a representative case here is an incomplete
//! change - the cross-language check only catches a drift it has a vector for.

use flint_engine::wire::{
    AuthMethod, BrowserCapabilityStatus, BrowserCommandAction, BrowserDarkMode, BrowserDialogType,
    BrowserInputEvent, BrowserInteractionMode, BrowserLibraryAction, BrowserLibraryEntry,
    BrowserLibraryEntryKind, BrowserLoadState, BrowserPointerAction, BrowserPreviewState,
    BrowserSearchEngine, BrowserSemanticKey, BrowserTabAction, BrowserTabStateEntry,
    BrowserUserAgentMode, BrowserViewAction, ByeReason, CodecId, ControlEvent, Frame, KeyAction,
    MediaAction, Message, PlaybackState, PointerAction, SurfaceMode, TransportAction,
};

#[path = "browser_network_vectors.rs"]
mod browser_network_vectors;
#[path = "browser_profile_vectors.rs"]
mod browser_profile_vectors;
#[path = "browser_workspace_vectors.rs"]
mod browser_workspace_vectors;

/// One named case in the corpus.
pub struct Vector {
    /// Stable file name. Never renamed: the name is how the other languages find the case.
    pub name: &'static str,
    pub frame: Frame,
}

/// The canonical corpus.
///
/// Adding a message type without adding a representative case here is an incomplete change.
pub fn corpus() -> Vec<Vector> {
    let mut vectors = vec![
        Vector {
            name: "hello-minimal",
            frame: Frame::new(Message::Hello {
                minimum_version: 1,
                maximum_version: 1,
                device_name: "TV".into(),
                codec_capabilities: vec![],
                screen_width: 1280,
                screen_height: 720,
                density_dpi: 160,
            }),
        },
        Vector {
            name: "hello-full",
            frame: Frame {
                protocol_version: 1,
                flags: 0xabcd,
                message: Message::Hello {
                    minimum_version: 1,
                    maximum_version: 3,
                    // Multi-byte UTF-8, because device names are user-supplied and routinely
                    // contain emoji.
                    device_name: "Living Room 📺".into(),
                    // Deliberately unsorted and duplicated on input: the encoder must normalise,
                    // or the same capability set would produce different bytes.
                    codec_capabilities: vec![
                        CodecId::H265,
                        CodecId::H264,
                        CodecId::H265,
                        CodecId::OPUS,
                    ],
                    screen_width: 3840,
                    screen_height: 2160,
                    density_dpi: 320,
                },
            },
        },
        Vector {
            name: "auth-pairing-code",
            frame: Frame::new(Message::Auth {
                method: AuthMethod::PairingCode,
                credential: b"012345".to_vec(),
                public_key_fingerprint: None,
            }),
        },
        Vector {
            name: "auth-with-fingerprint",
            frame: Frame::new(Message::Auth {
                method: AuthMethod::SessionToken,
                credential: vec![0x00, 0xff, 0x7f, 0x80],
                public_key_fingerprint: Some("sha256:01:02:ab".into()),
            }),
        },
        Vector {
            name: "video-config",
            frame: Frame::new(Message::VideoConfig {
                codec: CodecId::H264,
                width: 1920,
                height: 1080,
                codec_specific_data: vec![vec![0, 0, 0, 1, 0x67], vec![0, 0, 0, 1, 0x68]],
            }),
        },
        Vector {
            name: "video-config-no-csd",
            frame: Frame::new(Message::VideoConfig {
                codec: CodecId::AV1,
                width: 1,
                height: 1,
                codec_specific_data: vec![],
            }),
        },
        Vector {
            name: "video-keyframe",
            frame: Frame::new(Message::Video {
                presentation_time_us: 9_876_543,
                key_frame: true,
                data: vec![0, 0, 1, 0x65],
            }),
        },
        Vector {
            name: "video-delta",
            frame: Frame::new(Message::Video {
                presentation_time_us: 0,
                key_frame: false,
                data: vec![0x41],
            }),
        },
        Vector {
            name: "audio-config",
            frame: Frame::new(Message::AudioConfig {
                codec: CodecId::OPUS,
                sample_rate_hz: 48_000,
                channel_count: 2,
                codec_specific_data: vec![],
            }),
        },
        Vector {
            name: "audio-packet",
            frame: Frame::new(Message::Audio {
                presentation_time_us: 1,
                data: vec![9, 8, 7],
            }),
        },
        Vector {
            name: "control-transport-play",
            frame: Frame::new(Message::Control {
                sequence_number: 1,
                event: ControlEvent::Transport {
                    action: TransportAction::Play,
                    position_ms: -1,
                },
            }),
        },
        Vector {
            name: "control-transport-seek",
            frame: Frame::new(Message::Control {
                sequence_number: i64::MAX,
                event: ControlEvent::Transport {
                    action: TransportAction::SeekTo,
                    position_ms: 123_456,
                },
            }),
        },
        Vector {
            name: "control-pointer",
            frame: Frame::new(Message::Control {
                sequence_number: 3,
                event: ControlEvent::Pointer {
                    action: PointerAction::Move,
                    x: 0.25,
                    y: 0.75,
                    buttons: 1,
                },
            }),
        },
        Vector {
            name: "control-key",
            frame: Frame::new(Message::Control {
                sequence_number: 4,
                event: ControlEvent::Key {
                    action: KeyAction::Down,
                    key_code: 23,
                },
            }),
        },
        Vector {
            name: "control-text",
            frame: Frame::new(Message::Control {
                sequence_number: 5,
                event: ControlEvent::Text("Hello, TV 👋".into()),
            }),
        },
        Vector {
            name: "control-volume",
            frame: Frame::new(Message::Control {
                sequence_number: 6,
                event: ControlEvent::Volume(0.5),
            }),
        },
        Vector {
            name: "stats",
            frame: Frame::new(Message::Stats {
                receiver_queue_depth: 3,
                decode_latency_us: 17_000,
                round_trip_time_us: 8_000,
                dropped_video_frames: 12,
            }),
        },
        Vector {
            name: "stats-zero",
            frame: Frame::new(Message::Stats {
                receiver_queue_depth: 0,
                decode_latency_us: 0,
                round_trip_time_us: 0,
                dropped_video_frames: 0,
            }),
        },
        Vector {
            name: "bye-normal",
            frame: Frame::new(Message::Bye {
                reason: ByeReason::Normal,
                detail: String::new(),
            }),
        },
        Vector {
            name: "bye-protocol-error",
            frame: Frame::new(Message::Bye {
                reason: ByeReason::ProtocolError,
                detail: "malformed packet".into(),
            }),
        },
        Vector {
            name: "unknown-future-message",
            frame: Frame {
                protocol_version: 1,
                flags: 0x0001,
                message: Message::Unknown {
                    type_id: 0xfefe,
                    payload: vec![1, 2, 3, 4],
                },
            },
        },
        // Media family (IDs 10–13). These remain legal on the ordinary cast channel at v1.
        Vector {
            name: "media-command-load",
            frame: Frame::new(Message::MediaCommand {
                action: MediaAction::Load,
                url: String::new(),
                title: "Demo Clip".into(),
                mime_type: "video/mp4".into(),
                duration_ms: 12_345,
                start_position_ms: 0,
                subtitle_url: Some("https://example.test/subs.vtt".into()),
            }),
        },
        Vector {
            name: "media-command-clear",
            frame: Frame::new(Message::MediaCommand {
                action: MediaAction::Clear,
                url: String::new(),
                title: String::new(),
                mime_type: String::new(),
                duration_ms: -1,
                start_position_ms: 0,
                subtitle_url: None,
            }),
        },
        Vector {
            name: "media-data-chunk",
            frame: Frame::new(Message::MediaData {
                data: vec![0x00, 0x01, 0xfe, 0xff],
                is_final: false,
            }),
        },
        Vector {
            name: "media-data-final",
            frame: Frame::new(Message::MediaData {
                data: vec![],
                is_final: true,
            }),
        },
        Vector {
            name: "surface-idle",
            frame: Frame::new(Message::Surface {
                mode: SurfaceMode::Idle,
                caption: String::new(),
            }),
        },
        Vector {
            name: "surface-player",
            frame: Frame::new(Message::Surface {
                mode: SurfaceMode::Player,
                caption: "Playing".into(),
            }),
        },
        Vector {
            name: "playback-state-playing",
            frame: Frame::new(Message::PlaybackState {
                state: PlaybackState::Playing,
                position_ms: 1_000,
                duration_ms: 12_345,
                detail: String::new(),
            }),
        },
        Vector {
            name: "playback-state-error",
            frame: Frame::new(Message::PlaybackState {
                state: PlaybackState::Error,
                position_ms: 0,
                duration_ms: -1,
                detail: "decode failed".into(),
            }),
        },
        // Browser family (IDs 14–20) plus Browser surface. These require protocol version 2 and
        // are forbidden on the ordinary cast channel.
        Vector {
            name: "browser-capability-available",
            frame: Frame::new(Message::BrowserCapability {
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
            }),
        },
        Vector {
            name: "browser-command-open",
            frame: Frame::new(Message::BrowserCommand {
                epoch: 1,
                command_id: 2,
                action: BrowserCommandAction::Open,
                url: Some("https://example.test/start".into()),
                preview_enabled: None,
            }),
        },
        Vector {
            name: "browser-command-preview",
            frame: Frame::new(Message::BrowserCommand {
                epoch: 1,
                command_id: 3,
                action: BrowserCommandAction::SetPreviewEnabled,
                url: None,
                preview_enabled: Some(true),
            }),
        },
        Vector {
            name: "browser-input-pointer",
            frame: Frame::new(Message::BrowserInput {
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
            }),
        },
        Vector {
            name: "browser-input-scroll",
            frame: Frame::new(Message::BrowserInput {
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
            }),
        },
        Vector {
            name: "browser-input-key",
            frame: Frame::new(Message::BrowserInput {
                epoch: 1,
                sequence: 3,
                event: BrowserInputEvent::SemanticKey(BrowserSemanticKey::Select),
            }),
        },
        Vector {
            name: "browser-input-text",
            frame: Frame::new(Message::BrowserInput {
                epoch: 1,
                sequence: 4,
                event: BrowserInputEvent::Text("A browser input value".into()),
            }),
        },
        Vector {
            name: "browser-state-loaded",
            frame: Frame::new(Message::BrowserState {
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
                preview_state: BrowserPreviewState::Disabled,
                error_detail: String::new(),
            }),
        },
        Vector {
            name: "browser-preview-jpeg",
            frame: Frame::new(Message::BrowserPreview {
                epoch: 1,
                navigation_id: 3,
                frame_id: 9,
                width: 2,
                height: 2,
                jpeg: vec![0xff, 0xd8, 0xff, 0xd9],
            }),
        },
        Vector {
            name: "browser-dialog-confirm",
            frame: Frame::new(Message::BrowserDialog {
                epoch: 1,
                dialog_id: 11,
                dialog_type: BrowserDialogType::Confirm,
                origin: "https://example.test".into(),
                message: "Leave this page?".into(),
                default_value: String::new(),
                timeout_milliseconds: 10_000,
            }),
        },
        Vector {
            name: "browser-dialog-reply-accept",
            frame: Frame::new(Message::BrowserDialogReply {
                epoch: 1,
                dialog_id: 11,
                accepted: true,
                prompt_text: None,
            }),
        },
        Vector {
            name: "surface-browser",
            frame: Frame::new(Message::Surface {
                mode: SurfaceMode::Browser,
                caption: "Browser".into(),
            }),
        },
    ];

    for (name, command_id, action, tab_id, url) in [
        (
            "browser-tab-command-new",
            21,
            BrowserTabAction::New,
            0,
            Some("https://example.test/new"),
        ),
        (
            "browser-tab-command-close",
            22,
            BrowserTabAction::Close,
            7,
            None,
        ),
        (
            "browser-tab-command-select",
            23,
            BrowserTabAction::Select,
            8,
            None,
        ),
        (
            "browser-tab-command-move",
            24,
            BrowserTabAction::Move,
            9,
            None,
        ),
        (
            "browser-tab-command-duplicate",
            25,
            BrowserTabAction::Duplicate,
            10,
            None,
        ),
    ] {
        vectors.push(Vector {
            name,
            frame: Frame::new(Message::BrowserTabCommand {
                epoch: 2,
                command_id,
                action,
                tab_id,
                url: url.map(str::to_owned),
            }),
        });
    }

    vectors.extend([
        Vector {
            name: "browser-tab-state-empty",
            frame: Frame::new(Message::BrowserTabState {
                epoch: 2,
                revision: 30,
                active_tab_id: 0,
                tabs: vec![],
            }),
        },
        Vector {
            name: "browser-tab-state-populated",
            frame: Frame::new(Message::BrowserTabState {
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
            }),
        },
    ]);
    vectors.extend(browser_profile_vectors::vectors());
    vectors.extend(browser_network_vectors::vectors());
    vectors.extend(browser_workspace_vectors::vectors());

    for (name, command_id, action, value, text) in [
        (
            "browser-view-command-zoom",
            40,
            BrowserViewAction::SetZoom,
            125,
            "",
        ),
        (
            "browser-view-command-ua-tv",
            41,
            BrowserViewAction::SetUa,
            BrowserUserAgentMode::Tv as i32,
            "",
        ),
        (
            "browser-view-command-ua-desktop",
            42,
            BrowserViewAction::SetUa,
            BrowserUserAgentMode::Desktop as i32,
            "",
        ),
        (
            "browser-view-command-ua-mobile",
            43,
            BrowserViewAction::SetUa,
            BrowserUserAgentMode::Mobile as i32,
            "",
        ),
        (
            "browser-view-command-dark-follow-system",
            44,
            BrowserViewAction::SetDark,
            BrowserDarkMode::FollowSystem as i32,
            "",
        ),
        (
            "browser-view-command-dark-light",
            45,
            BrowserViewAction::SetDark,
            BrowserDarkMode::Light as i32,
            "",
        ),
        (
            "browser-view-command-dark-dark",
            46,
            BrowserViewAction::SetDark,
            BrowserDarkMode::Dark as i32,
            "",
        ),
        (
            "browser-view-command-input-cursor",
            47,
            BrowserViewAction::SetInputMode,
            BrowserInteractionMode::Cursor as i32,
            "",
        ),
        (
            "browser-view-command-input-focus",
            48,
            BrowserViewAction::SetInputMode,
            BrowserInteractionMode::Focus as i32,
            "",
        ),
        (
            "browser-view-command-fullscreen",
            49,
            BrowserViewAction::SetFullscreen,
            1,
            "",
        ),
        (
            "browser-view-command-find-start",
            50,
            BrowserViewAction::FindStart,
            0,
            "needle",
        ),
        (
            "browser-view-command-find-next",
            51,
            BrowserViewAction::FindNext,
            0,
            "",
        ),
        (
            "browser-view-command-find-prev",
            52,
            BrowserViewAction::FindPrev,
            0,
            "",
        ),
        (
            "browser-view-command-find-clear",
            53,
            BrowserViewAction::FindClear,
            0,
            "",
        ),
        (
            "browser-view-command-search-duckduckgo",
            54,
            BrowserViewAction::SetSearchEngine,
            BrowserSearchEngine::DuckDuckGo as i32,
            "",
        ),
        (
            "browser-view-command-search-google",
            55,
            BrowserViewAction::SetSearchEngine,
            BrowserSearchEngine::Google as i32,
            "",
        ),
        (
            "browser-view-command-search-bing",
            56,
            BrowserViewAction::SetSearchEngine,
            BrowserSearchEngine::Bing as i32,
            "",
        ),
        (
            "browser-view-command-search-custom",
            57,
            BrowserViewAction::SetSearchEngine,
            BrowserSearchEngine::Custom as i32,
            "https://search.example/?q={q}",
        ),
    ] {
        vectors.push(Vector {
            name,
            frame: Frame::new(Message::BrowserViewCommand {
                epoch: 2,
                command_id,
                action,
                value,
                text: text.into(),
            }),
        });
    }

    vectors.extend([
        Vector {
            name: "browser-view-state-active",
            frame: Frame::new(Message::BrowserViewState {
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
            }),
        },
        Vector {
            name: "browser-favicon-png",
            frame: Frame::new(Message::BrowserFavicon {
                epoch: 2,
                favicon_id: 61,
                width: 2,
                height: 2,
                png: vec![0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a],
            }),
        },
    ]);

    for (name, command_id, action, url, title) in [
        (
            "browser-library-command-add",
            70,
            BrowserLibraryAction::AddBookmark,
            "https://example.test/bookmark",
            "Example",
        ),
        (
            "browser-library-command-remove",
            71,
            BrowserLibraryAction::RemoveBookmark,
            "https://example.test/bookmark",
            "",
        ),
        (
            "browser-library-command-clear-history",
            72,
            BrowserLibraryAction::ClearHistory,
            "",
            "",
        ),
        (
            "browser-library-command-clear-bookmarks",
            73,
            BrowserLibraryAction::ClearBookmarks,
            "",
            "",
        ),
        (
            "browser-library-command-request-snapshot",
            74,
            BrowserLibraryAction::RequestSnapshot,
            "",
            "",
        ),
    ] {
        vectors.push(Vector {
            name,
            frame: Frame::new(Message::BrowserLibraryCommand {
                epoch: 2,
                command_id,
                action,
                url: url.into(),
                title: title.into(),
            }),
        });
    }

    vectors.extend([
        Vector {
            name: "browser-library-state-populated",
            frame: Frame::new(Message::BrowserLibraryState {
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
            }),
        },
        Vector {
            name: "browser-library-state-empty",
            frame: Frame::new(Message::BrowserLibraryState {
                epoch: 2,
                revision: 81,
                bookmarks: vec![],
                history: vec![],
            }),
        },
    ]);

    // Legacy/media cases stay on v1; browser cases and its surface stay on v2; workspace on v3.
    for vector in &mut vectors {
        let requires_v3 = matches!(
            &vector.frame.message,
            Message::BrowserWorkspaceCommand { .. }
                | Message::BrowserWorkspaceState { .. }
                | Message::BrowserWorkspaceInput { .. }
        );
        let requires_v2 = matches!(
            &vector.frame.message,
            Message::BrowserCapability { .. }
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
                | Message::Surface {
                    mode: SurfaceMode::Browser,
                    ..
                }
        );
        vector.frame.protocol_version = if matches!(vector.frame.message, Message::BrowserWorkspaceResize { .. } | Message::BrowserWorkspaceGeometry { .. }) {
            4
        } else if requires_v3 {
            3
        } else if requires_v2 {
            2
        } else {
            1
        };
    }
    vectors
}
