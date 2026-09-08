use flint_engine::wire::{
    BrowserProfileAction, BrowserProfileEntry, BrowserProfileSource, Frame, Message,
};

use super::Vector;

pub(super) fn vectors() -> Vec<Vector> {
    let commands = [
        (
            "browser-profile-command-select-tv",
            90,
            BrowserProfileAction::SelectTvProfile,
            "family",
            "",
        ),
        (
            "browser-profile-command-create-tv",
            91,
            BrowserProfileAction::CreateTvProfile,
            "",
            "Kids 🚀",
        ),
        (
            "browser-profile-command-rename-tv",
            92,
            BrowserProfileAction::RenameTvProfile,
            "kids_2",
            "Children",
        ),
        (
            "browser-profile-command-delete-tv",
            93,
            BrowserProfileAction::DeleteTvProfile,
            "kids_2",
            "",
        ),
        (
            "browser-profile-command-select-device",
            94,
            BrowserProfileAction::SelectDevice,
            "",
            "",
        ),
        (
            "browser-profile-command-request-snapshot",
            95,
            BrowserProfileAction::RequestSnapshot,
            "",
            "",
        ),
    ];
    let mut vectors: Vec<Vector> = commands
        .into_iter()
        .map(
            |(name, command_id, action, profile_id, profile_name)| Vector {
                name,
                frame: Frame::new(Message::BrowserProfileCommand {
                    epoch: 3,
                    command_id,
                    action,
                    profile_id: profile_id.into(),
                    name: profile_name.into(),
                }),
            },
        )
        .collect();
    vectors.extend([
        Vector {
            name: "browser-profile-state-tv",
            frame: Frame::new(Message::BrowserProfileState {
                epoch: 3,
                revision: 100,
                active_source: BrowserProfileSource::Tv,
                active_profile_id: "family".into(),
                device_name: "Tochukwu's PC".into(),
                profiles: vec![
                    BrowserProfileEntry {
                        profile_id: "family".into(),
                        name: "Family".into(),
                    },
                    BrowserProfileEntry {
                        profile_id: "kids_2".into(),
                        name: "Kids 🚀".into(),
                    },
                ],
            }),
        },
        Vector {
            name: "browser-profile-state-device",
            frame: Frame::new(Message::BrowserProfileState {
                epoch: 3,
                revision: 101,
                active_source: BrowserProfileSource::Device,
                active_profile_id: String::new(),
                device_name: "Tochukwu's PC".into(),
                profiles: vec![BrowserProfileEntry {
                    profile_id: "family".into(),
                    name: "Family".into(),
                }],
            }),
        },
    ]);
    vectors
}
