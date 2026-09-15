use flint_engine::wire::{
    BrowserWorkspaceCommandAction, BrowserWorkspaceInputKind, BrowserWorkspacePaneStateEntry,
    BrowserWorkspaceWireInteractionMode, BrowserWorkspaceWireLayout,
    BrowserWorkspaceWireMuteApplication, BrowserWorkspaceWireObservedPlayback,
    BrowserWorkspaceWirePaneResidency, Frame, Message,
};

use super::Vector;

pub(super) fn vectors() -> Vec<Vector> {
    vec![
        Vector {
            name: "browser-workspace-resize",
            frame: Frame::new(Message::BrowserWorkspaceResize {
                epoch: 4,
                command_id: 150,
                expected_revision: 140,
                column: 7000,
                row: 3000,
                mode: 0,
            }),
        },
        Vector {
            name: "browser-workspace-geometry",
            frame: Frame::new(Message::BrowserWorkspaceGeometry {
                epoch: 4,
                revision: 141,
                column: 7000,
                row: 3000,
                mode: 0,
            }),
        },
        Vector {
            name: "browser-workspace-command-focus",
            frame: Frame::new(Message::BrowserWorkspaceCommand {
                epoch: 4,
                command_id: 130,
                expected_revision: 140,
                action: BrowserWorkspaceCommandAction::Focus,
                pane_id: 42,
                value: 0,
                url: String::new(),
            }),
        },
        Vector {
            name: "browser-workspace-command-open-pane",
            frame: Frame::new(Message::BrowserWorkspaceCommand {
                epoch: 4,
                command_id: 128,
                expected_revision: 139,
                action: BrowserWorkspaceCommandAction::OpenPane,
                pane_id: 0,
                value: 0,
                url: "https://example.test/new".into(),
            }),
        },
        Vector {
            name: "browser-workspace-command-move-pane",
            frame: Frame::new(Message::BrowserWorkspaceCommand {
                epoch: 4,
                command_id: 143,
                expected_revision: 140,
                action: BrowserWorkspaceCommandAction::MovePane,
                pane_id: 42,
                value: 1,
                url: String::new(),
            }),
        },
        Vector {
            name: "browser-workspace-state-single",
            frame: Frame::new(Message::BrowserWorkspaceState {
                epoch: 4,
                revision: 140,
                layout: BrowserWorkspaceWireLayout::Single,
                focused_pane_id: 42,
                interaction_mode: BrowserWorkspaceWireInteractionMode::WorkspaceChrome,
                page_fullscreen_pane_id: 0,
                theater_pane_id: 0,
                max_live_renderers: 2,
                max_open_panes: 4,
                panes: vec![BrowserWorkspacePaneStateEntry {
                    pane_id: 42,
                    slot: 0,
                    residency: BrowserWorkspaceWirePaneResidency::Live,
                    url: "https://example.test/new".into(),
                    title: String::new(),
                    loading: false,
                    progress: 100,
                    can_go_back: false,
                    can_go_forward: false,
                    desired_muted: false,
                    mute_application: BrowserWorkspaceWireMuteApplication::NotRequested,
                    observed_playback: BrowserWorkspaceWireObservedPlayback::Unknown,
                }],
            }),
        },
        Vector {
            name: "browser-workspace-input-text",
            frame: Frame::new(Message::BrowserWorkspaceInput {
                epoch: 4,
                command_id: 131,
                expected_revision: 140,
                pane_id: 42,
                kind: BrowserWorkspaceInputKind::Text,
                key: None,
                text: "hello".into(),
            }),
        },
    ]
}
