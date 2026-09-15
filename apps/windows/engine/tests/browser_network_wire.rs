use flint_engine::wire::browser;
use flint_engine::wire::codec::{self, WireError};
use flint_engine::wire::{
    BrowserNetworkAction, BrowserVpnProvider, BrowserVpnSessionState, Frame, Message, MessageType,
};

const SAMPLE_WIREGUARD_CONFIG: &str =
    "[Interface]\nPrivateKey = AAA=\n\n[Peer]\nPublicKey = BBB=\n";

fn assert_round_trip(message: Message) {
    let frame = Frame::new(message);
    let encoded = codec::encode(&frame).expect("valid network message encodes");
    assert_eq!(
        codec::decode(&encoded).expect("network message decodes"),
        frame
    );
}

#[test]
fn every_network_action_and_session_state_round_trips() {
    for (command_id, action, profile_id, vpn_enabled, provider, auto_connect, config) in [
        (
            1,
            BrowserNetworkAction::Set,
            "family",
            true,
            BrowserVpnProvider::WireGuard,
            true,
            SAMPLE_WIREGUARD_CONFIG,
        ),
        (
            2,
            BrowserNetworkAction::Set,
            "kids_2",
            false,
            BrowserVpnProvider::None,
            false,
            "",
        ),
        (
            3,
            BrowserNetworkAction::Clear,
            "family",
            false,
            BrowserVpnProvider::None,
            false,
            "",
        ),
        (
            4,
            BrowserNetworkAction::RequestSnapshot,
            "",
            false,
            BrowserVpnProvider::None,
            false,
            "",
        ),
        (
            5,
            BrowserNetworkAction::RequestSnapshot,
            "family",
            false,
            BrowserVpnProvider::None,
            false,
            "",
        ),
    ] {
        assert_round_trip(Message::BrowserNetworkCommand {
            epoch: 3,
            command_id,
            action,
            profile_id: profile_id.into(),
            vpn_enabled,
            provider,
            auto_connect_on_browser_start: auto_connect,
            require_vpn_before_browse: false,
            config_text: config.into(),
        });
    }

    for session_state in [
        BrowserVpnSessionState::Idle,
        BrowserVpnSessionState::NeedsConsent,
        BrowserVpnSessionState::Connecting,
        BrowserVpnSessionState::Connected,
        BrowserVpnSessionState::Failed,
        BrowserVpnSessionState::Unavailable,
    ] {
        assert_round_trip(Message::BrowserNetworkState {
            epoch: 3,
            revision: 7,
            profile_id: "family".into(),
            vpn_enabled: true,
            provider: BrowserVpnProvider::WireGuard,
            auto_connect_on_browser_start: true,
            require_vpn_before_browse: false,
            config_present: true,
            capability_preparable: true,
            capability_reason: "ok".into(),
            session_state,
            session_detail: if session_state == BrowserVpnSessionState::Failed {
                "connect failed".into()
            } else {
                String::new()
            },
        });
    }
}

#[test]
fn network_types_are_additive_v2_tls_browser_messages() {
    assert_eq!(MessageType::BrowserNetworkCommand as u16, 30);
    assert_eq!(MessageType::BrowserNetworkState as u16, 31);
    for message in [
        Message::BrowserNetworkCommand {
            epoch: 3,
            command_id: 1,
            action: BrowserNetworkAction::RequestSnapshot,
            profile_id: String::new(),
            vpn_enabled: false,
            provider: BrowserVpnProvider::None,
            auto_connect_on_browser_start: false,
            require_vpn_before_browse: false,
            config_text: String::new(),
        },
        Message::BrowserNetworkState {
            epoch: 3,
            revision: 1,
            profile_id: "family".into(),
            vpn_enabled: false,
            provider: BrowserVpnProvider::None,
            auto_connect_on_browser_start: false,
            require_vpn_before_browse: false,
            config_present: false,
            capability_preparable: false,
            capability_reason: "probe".into(),
            session_state: BrowserVpnSessionState::Idle,
            session_detail: String::new(),
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
fn invalid_network_commands_are_rejected() {
    let invalid = [
        Message::BrowserNetworkCommand {
            epoch: 0,
            command_id: 1,
            action: BrowserNetworkAction::Clear,
            profile_id: "family".into(),
            vpn_enabled: false,
            provider: BrowserVpnProvider::None,
            auto_connect_on_browser_start: false,
            require_vpn_before_browse: false,
            config_text: String::new(),
        },
        Message::BrowserNetworkCommand {
            epoch: 3,
            command_id: 1,
            action: BrowserNetworkAction::Set,
            profile_id: "bad id".into(),
            vpn_enabled: false,
            provider: BrowserVpnProvider::None,
            auto_connect_on_browser_start: false,
            require_vpn_before_browse: false,
            config_text: String::new(),
        },
        Message::BrowserNetworkCommand {
            epoch: 3,
            command_id: 1,
            action: BrowserNetworkAction::Set,
            profile_id: "family".into(),
            vpn_enabled: true,
            provider: BrowserVpnProvider::None,
            auto_connect_on_browser_start: false,
            require_vpn_before_browse: false,
            config_text: String::new(),
        },
        Message::BrowserNetworkCommand {
            epoch: 3,
            command_id: 1,
            action: BrowserNetworkAction::Set,
            profile_id: "family".into(),
            vpn_enabled: true,
            provider: BrowserVpnProvider::WireGuard,
            auto_connect_on_browser_start: true,
            require_vpn_before_browse: false,
            config_text: "[Interface]\n".into(),
        },
        Message::BrowserNetworkCommand {
            epoch: 3,
            command_id: 1,
            action: BrowserNetworkAction::Clear,
            profile_id: "family".into(),
            vpn_enabled: true,
            provider: BrowserVpnProvider::None,
            auto_connect_on_browser_start: false,
            require_vpn_before_browse: false,
            config_text: String::new(),
        },
        Message::BrowserNetworkCommand {
            epoch: 3,
            command_id: 1,
            action: BrowserNetworkAction::RequestSnapshot,
            profile_id: String::new(),
            vpn_enabled: false,
            provider: BrowserVpnProvider::WireGuard,
            auto_connect_on_browser_start: false,
            require_vpn_before_browse: false,
            config_text: String::new(),
        },
    ];
    for message in invalid {
        assert!(matches!(
            codec::encode(&Frame::new(message)),
            Err(WireError::Invariant(_))
        ));
    }
}

#[test]
fn config_uses_u32_length_prefix() {
    let frame = Frame::new(Message::BrowserNetworkCommand {
        epoch: 3,
        command_id: 1,
        action: BrowserNetworkAction::Set,
        profile_id: "family".into(),
        vpn_enabled: true,
        provider: BrowserVpnProvider::WireGuard,
        auto_connect_on_browser_start: false,
        require_vpn_before_browse: false,
        config_text: SAMPLE_WIREGUARD_CONFIG.into(),
    });
    let encoded = codec::encode(&frame).expect("encodes");
    // envelope is 12 bytes; then epoch(8)+commandId(8)+action(1)+profileId utf8u16
    // profile "family" = 2 + 6; then 3 bool/provider bytes; then u32 length
    let payload = &encoded[12..];
    let mut offset = 8 + 8 + 1;
    let profile_len = u16::from_be_bytes([payload[offset], payload[offset + 1]]) as usize;
    offset += 2 + profile_len;
    offset += 4; // vpnEnabled, provider, autoConnect, requireVpnBeforeBrowse
    let config_len = u32::from_be_bytes([
        payload[offset],
        payload[offset + 1],
        payload[offset + 2],
        payload[offset + 3],
    ]) as usize;
    assert_eq!(config_len, SAMPLE_WIREGUARD_CONFIG.len());
}
