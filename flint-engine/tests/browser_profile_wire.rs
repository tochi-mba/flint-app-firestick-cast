use flint_engine::wire::browser;
use flint_engine::wire::codec::{self, WireError};
use flint_engine::wire::{
    BrowserProfileAction, BrowserProfileEntry, BrowserProfileSource, Frame, Message, MessageType,
};

fn assert_round_trip(message: Message) {
    let frame = Frame::new(message);
    let encoded = codec::encode(&frame).expect("valid profile message encodes");
    assert_eq!(
        codec::decode(&encoded).expect("profile message decodes"),
        frame
    );
}

fn tv_state() -> Message {
    Message::BrowserProfileState {
        epoch: 3,
        revision: 7,
        active_source: BrowserProfileSource::Tv,
        active_profile_id: "family".into(),
        device_name: String::new(),
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
    }
}

#[test]
fn every_profile_action_and_source_round_trips() {
    for (command_id, action, profile_id, name) in [
        (1, BrowserProfileAction::SelectTvProfile, "family", ""),
        (2, BrowserProfileAction::CreateTvProfile, "", "Kids 🚀"),
        (
            3,
            BrowserProfileAction::RenameTvProfile,
            "family_2",
            "Family room",
        ),
        (4, BrowserProfileAction::DeleteTvProfile, "family-2", ""),
        (5, BrowserProfileAction::SelectDevice, "", ""),
        (6, BrowserProfileAction::RequestSnapshot, "", ""),
    ] {
        assert_round_trip(Message::BrowserProfileCommand {
            epoch: 3,
            command_id,
            action,
            profile_id: profile_id.into(),
            name: name.into(),
        });
    }
    assert_round_trip(tv_state());
    assert_round_trip(Message::BrowserProfileState {
        epoch: 3,
        revision: 8,
        active_source: BrowserProfileSource::Device,
        active_profile_id: String::new(),
        device_name: "Tochukwu's PC".into(),
        profiles: vec![],
    });
}

#[test]
fn profile_types_are_additive_v2_tls_browser_messages() {
    assert_eq!(MessageType::BrowserProfileCommand as u16, 28);
    assert_eq!(MessageType::BrowserProfileState as u16, 29);
    for message in [
        Message::BrowserProfileCommand {
            epoch: 3,
            command_id: 1,
            action: BrowserProfileAction::RequestSnapshot,
            profile_id: String::new(),
            name: String::new(),
        },
        Message::BrowserProfileState {
            epoch: 3,
            revision: 1,
            active_source: BrowserProfileSource::Device,
            active_profile_id: String::new(),
            device_name: "PC".into(),
            profiles: vec![],
        },
    ] {
        assert!(browser::is_browser_message(&message));
        assert!(browser::is_browser_type(message.type_id()));
        assert!(browser::is_forbidden_on_ordinary_channel(&message));
        assert!(codec::encode(&Frame {
            protocol_version: 1,
            flags: 0,
            message,
        })
        .is_err());
    }
    assert!(browser::is_browser_type(30));
    assert!(browser::is_browser_type(31));
    assert!(browser::is_browser_type(32));
    assert!(browser::is_browser_type(34));
    assert!(browser::is_browser_type(35));
    assert!(browser::is_browser_type(36));
    assert!(!browser::is_browser_type(37));
}

#[test]
fn invalid_profile_command_combinations_are_rejected() {
    let invalid = [
        (0, 1, BrowserProfileAction::SelectTvProfile, "family", ""),
        (3, 0, BrowserProfileAction::SelectTvProfile, "family", ""),
        (3, 1, BrowserProfileAction::SelectTvProfile, "", ""),
        (3, 1, BrowserProfileAction::SelectTvProfile, "bad id", ""),
        (
            3,
            1,
            BrowserProfileAction::SelectTvProfile,
            "family",
            "extra",
        ),
        (
            3,
            1,
            BrowserProfileAction::CreateTvProfile,
            "generated",
            "Family",
        ),
        (3, 1, BrowserProfileAction::CreateTvProfile, "", ""),
        (3, 1, BrowserProfileAction::CreateTvProfile, "", " Family"),
        (
            3,
            1,
            BrowserProfileAction::CreateTvProfile,
            "",
            "Family\nRoom",
        ),
        (3, 1, BrowserProfileAction::RenameTvProfile, "family", ""),
        (
            3,
            1,
            BrowserProfileAction::DeleteTvProfile,
            "family",
            "extra",
        ),
        (3, 1, BrowserProfileAction::SelectDevice, "family", ""),
        (3, 1, BrowserProfileAction::RequestSnapshot, "", "extra"),
    ];
    for (epoch, command_id, action, profile_id, name) in invalid {
        assert!(codec::encode(&Frame::new(Message::BrowserProfileCommand {
            epoch,
            command_id,
            action,
            profile_id: profile_id.into(),
            name: name.into(),
        }))
        .is_err());
    }

    for (profile_id, name) in [
        ("a".repeat(65), String::new()),
        (String::new(), "🚀".repeat(17)),
    ] {
        let action = if profile_id.is_empty() {
            BrowserProfileAction::CreateTvProfile
        } else {
            BrowserProfileAction::SelectTvProfile
        };
        assert!(codec::encode(&Frame::new(Message::BrowserProfileCommand {
            epoch: 3,
            command_id: 1,
            action,
            profile_id,
            name,
        }))
        .is_err());
    }
}

#[test]
fn invalid_profile_state_ownership_and_catalogs_are_rejected() {
    let valid_profiles = vec![BrowserProfileEntry {
        profile_id: "family".into(),
        name: "Family".into(),
    }];
    let cases = [
        (
            0,
            1,
            BrowserProfileSource::Tv,
            "family",
            "",
            valid_profiles.clone(),
        ),
        (
            3,
            0,
            BrowserProfileSource::Tv,
            "family",
            "",
            valid_profiles.clone(),
        ),
        (
            3,
            1,
            BrowserProfileSource::Tv,
            "",
            "",
            valid_profiles.clone(),
        ),
        (
            3,
            1,
            BrowserProfileSource::Tv,
            "missing",
            "",
            valid_profiles.clone(),
        ),
        (
            3,
            1,
            BrowserProfileSource::Device,
            "family",
            "PC",
            valid_profiles.clone(),
        ),
        (
            3,
            1,
            BrowserProfileSource::Device,
            "",
            "PC\0",
            valid_profiles.clone(),
        ),
        (
            3,
            1,
            BrowserProfileSource::Tv,
            "family",
            "",
            vec![
                BrowserProfileEntry {
                    profile_id: "family".into(),
                    name: "Family".into(),
                },
                BrowserProfileEntry {
                    profile_id: "family".into(),
                    name: "Other".into(),
                },
            ],
        ),
        (
            3,
            1,
            BrowserProfileSource::Tv,
            "family",
            "",
            vec![
                BrowserProfileEntry {
                    profile_id: "family".into(),
                    name: "Family".into(),
                },
                BrowserProfileEntry {
                    profile_id: "other".into(),
                    name: "Family".into(),
                },
            ],
        ),
    ];
    for (epoch, revision, active_source, active_profile_id, device_name, profiles) in cases {
        assert!(codec::encode(&Frame::new(Message::BrowserProfileState {
            epoch,
            revision,
            active_source,
            active_profile_id: active_profile_id.into(),
            device_name: device_name.into(),
            profiles,
        }))
        .is_err());
    }

    let too_many = (0..9)
        .map(|index| BrowserProfileEntry {
            profile_id: format!("p{index}"),
            name: format!("Profile {index}"),
        })
        .collect();
    assert!(codec::encode(&Frame::new(Message::BrowserProfileState {
        epoch: 3,
        revision: 1,
        active_source: BrowserProfileSource::Tv,
        active_profile_id: "p0".into(),
        device_name: String::new(),
        profiles: too_many,
    }))
    .is_err());
}

#[test]
fn unknown_profile_enums_and_trailing_bytes_are_rejected() {
    for (message, payload_offset) in [
        (
            Message::BrowserProfileCommand {
                epoch: 3,
                command_id: 1,
                action: BrowserProfileAction::RequestSnapshot,
                profile_id: String::new(),
                name: String::new(),
            },
            16,
        ),
        (tv_state(), 16),
    ] {
        let mut bytes = codec::encode(&Frame::new(message)).expect("valid frame encodes");
        bytes[12 + payload_offset] = 0;
        assert!(matches!(
            codec::decode(&bytes),
            Err(WireError::InvalidValue { .. })
        ));
    }

    let mut trailing = codec::encode(&Frame::new(tv_state())).expect("valid frame encodes");
    let body = u32::from_be_bytes(trailing[0..4].try_into().unwrap());
    trailing[0..4].copy_from_slice(&(body + 1).to_be_bytes());
    trailing.push(0);
    assert!(matches!(
        codec::decode(&trailing),
        Err(WireError::TrailingBytes(1))
    ));

    let empty = Message::BrowserProfileState {
        epoch: 3,
        revision: 1,
        active_source: BrowserProfileSource::Device,
        active_profile_id: String::new(),
        device_name: String::new(),
        profiles: vec![],
    };
    let mut excessive_count = codec::encode(&Frame::new(empty)).expect("valid frame encodes");
    excessive_count[12 + 21] = 9;
    assert!(matches!(
        codec::decode(&excessive_count),
        Err(WireError::InvalidLength {
            field: "browser profile count",
            length: 9,
        })
    ));
}
