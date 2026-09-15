use flint_engine::wire::{
    BrowserNetworkAction, BrowserVpnProvider, BrowserVpnSessionState, Frame, Message,
};

use super::Vector;

const SAMPLE_WIREGUARD_CONFIG: &str =
    "[Interface]\nPrivateKey = AAA=\n\n[Peer]\nPublicKey = BBB=\n";

pub(super) fn vectors() -> Vec<Vector> {
    vec![
        Vector {
            name: "browser-network-command-set",
            frame: Frame::new(Message::BrowserNetworkCommand {
                epoch: 3,
                command_id: 110,
                action: BrowserNetworkAction::Set,
                profile_id: "family".into(),
                vpn_enabled: true,
                provider: BrowserVpnProvider::WireGuard,
                auto_connect_on_browser_start: true,
                require_vpn_before_browse: false,
                config_text: SAMPLE_WIREGUARD_CONFIG.into(),
            }),
        },
        Vector {
            name: "browser-network-command-clear",
            frame: Frame::new(Message::BrowserNetworkCommand {
                epoch: 3,
                command_id: 111,
                action: BrowserNetworkAction::Clear,
                profile_id: "family".into(),
                vpn_enabled: false,
                provider: BrowserVpnProvider::None,
                auto_connect_on_browser_start: false,
                require_vpn_before_browse: false,
                config_text: String::new(),
            }),
        },
        Vector {
            name: "browser-network-command-request-snapshot",
            frame: Frame::new(Message::BrowserNetworkCommand {
                epoch: 3,
                command_id: 112,
                action: BrowserNetworkAction::RequestSnapshot,
                profile_id: String::new(),
                vpn_enabled: false,
                provider: BrowserVpnProvider::None,
                auto_connect_on_browser_start: false,
                require_vpn_before_browse: false,
                config_text: String::new(),
            }),
        },
        Vector {
            name: "browser-network-state-connected",
            frame: Frame::new(Message::BrowserNetworkState {
                epoch: 3,
                revision: 120,
                profile_id: "family".into(),
                vpn_enabled: true,
                provider: BrowserVpnProvider::WireGuard,
                auto_connect_on_browser_start: true,
                require_vpn_before_browse: false,
                config_present: true,
                capability_preparable: true,
                capability_reason: "VpnService available".into(),
                session_state: BrowserVpnSessionState::Connected,
                session_detail: String::new(),
            }),
        },
        Vector {
            name: "browser-network-state-idle",
            frame: Frame::new(Message::BrowserNetworkState {
                epoch: 3,
                revision: 121,
                profile_id: "family".into(),
                vpn_enabled: false,
                provider: BrowserVpnProvider::None,
                auto_connect_on_browser_start: false,
                require_vpn_before_browse: false,
                config_present: false,
                capability_preparable: false,
                capability_reason: "VPN probe not configured".into(),
                session_state: BrowserVpnSessionState::Idle,
                session_detail: String::new(),
            }),
        },
    ]
}
