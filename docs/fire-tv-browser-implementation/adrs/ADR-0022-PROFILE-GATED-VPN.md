# ADR-0022 — profile-gated optional VPN on the receiver

- **Status:** Accepted
- **Implementation:** Superseded prototype; not release-ready
- **Date:** 2026-09-06
- **Scope:** Fire TV browser experiment
- **Deciders:** Flint engineering
- **Owners:** TV browser UX / network workstream
- **Related:** ADR-0001, ADR-0018
- **Supersedes:** None
- **Superseded by:** ADR-0023

## Context

Browsing egress is TV-resident (ADR-0001). A Windows VPN cannot wrap Stick page traffic without a
host proxy (explicit non-goal). Profiles today store library ownership, not network egress. Fire OS
`VpnService` requires user consent and is OEM-fragile.

## Decision

- Optional **Network** settings live per TV profile in a separate `BrowserNetworkStore` (not mixed
  into the bookmark library file).
- Supported provider for v1 schema: **WireGuard config text** only. Auto-connect runs **on browser
  (or mosaic) session start**, never on idle pairing alone.
- Auto-connect only when `vpnEnabled`, provider configured, config validates, capability probe says
  preparable, and the viewer has granted VPN consent via the system prompt — never bypassed.
- Soft-fail with a visible banner if connect fails; do not pretend the tunnel is up.
- Tunnel implementation: official `com.wireguard.android:tunnel` **GoBackend**. The manifest
  registers `GoBackend$VpnService` (required by that library). `BrowserVpnTunnelImpl` parses config,
  refuses without consent, then brings the tunnel UP/DOWN. Never log config private keys.
- Host (Windows) paste lives in Browser → **TV Network / VPN**; wire types **30/31** push settings
  to the Stick. State snapshots never echo full config — only `configPresent` + session status.
- Offer disable/remove wherever enable is offered.
- Do not call LAN traffic private/encrypted beyond what the tunnel actually provides (AGENTS.md).

## Consequences

### Positive

- Profile-scoped opt-in without forcing every viewer through a tunnel.
- Consent and honesty match project constraints.
- Real Stick egress can follow a pasted WireGuard config when consent is granted.

### Trade-offs

- One OS tunnel for the whole device — not per pane.
- Full-tunnel AllowedIPs may disrupt LAN cast; soak-test on target Fire OS; split-tunnel configs are
  the operator's responsibility.
- Native `wg-go` ABI packaging and VPN OEM quirks remain device-dependent.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Host VPN / HTTP proxy | Contradicts ADR-0001 and FIRE_TV_BROWSER_PLAN non-goals. |
| Silent always-on without consent | Violates Fire OS and AGENTS.md. |
| Multiple VPN backends in v1 | Scope explosion; one validated provider first. |
| Local stub `VpnService` without WireGuard | Cannot move packets; risk of empty `establish()` breaking networking. |

## Invariants and validation

- `BrowserVpnCoordinatorTest`, `BrowserVpnTunnelImplTest`, `ProfileNetworkSettingsTest`,
  `WireGuardConfigValidatorTest`.
- Capability probe gates any working-looking control.
- Headless suite must not load `wg-go` for failure paths (invalid config / missing consent).

## Revisit criteria

Measured VpnService + LAN cast behaviour on target Fire OS builds with a real peer; config paste UX
polish; Activity result wiring if system consent Intent must be launched from the Activity.

## References

- [ADR-0001](ADR-0001-TV-RESIDENT-BROWSER-OWNERSHIP.md)
- [AGENTS.md](../../../AGENTS.md)
- WireGuard Android tunnel: https://git.zx2c4.com/wireguard-android/
