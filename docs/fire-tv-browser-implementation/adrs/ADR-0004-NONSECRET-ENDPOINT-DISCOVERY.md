# ADR-0004 — non-secret browser endpoint discovery

- **Status:** Accepted
- **Implementation:** Planned
- **Date:** 2026-09-04
- **Deciders:** Flint engineering
- **Owners:** Slices 03 and 09
- **Related:** ADR-0002, ADR-0003, ADR-0005

## Context

Windows needs the receiver BrowserTlsServer port before it can create TLS. The current product has
legacy receiver/discovery paths and an ordinary control socket, but browser data must not be sent
over that socket. A browser endpoint bootstrap is needed without treating unauthenticated LAN
metadata as receiver identity.

Using the legacy plaintext listener to detect TLS or exchange browser data would violate the
separate-transport decision. Hiding a port in a hard-coded/subnet scan would violate network
constraints and make endpoint changes brittle.

## Decision

Advertise browser endpoint information as **non-secret receiver discovery metadata** associated with
the Flint receiver service:

- publish the explicitly bound BrowserTlsServer port, browser protocol min/max version, and
  availability class in receiver discovery/mDNS TXT metadata;
- add a typed host resolver that expires stale metadata, responds to interface/port changes, and
  only offers a browser verification flow for an already selected eligible receiver;
- consider this metadata untrusted routing data. It cannot establish identity, authorize a client,
  or cause automatic trust;
- BrowserSession performs TLS/certificate pin verification before AUTH. A spoofed advertisement
  therefore results in a pin/handshake failure, never a trusted receiver;
- withdraw/update metadata atomically with listener start/stop/network restart. Do not advertise a
  port that is not currently bound to the selected interface.

The exact TXT key names are a v2 discovery contract reviewed with the receiver's existing
ReceiverMdnsResponder and the Windows resolver. They must not contain pairing code, certificate,
full fingerprint, session token, browsing state, URL, or user data.

## Consequences

### Positive

- Browser TLS has a discoverable endpoint without polluting the legacy control protocol.
- Port/interface changes are observable and testable.
- Identity stays where it belongs: the physical fingerprint comparison and full certificate pin.
- Endpoint metadata remains safe to broadcast because it contains no browser/session secret.

### Trade-offs

- Discovery/host selection must reconcile stale/duplicate advertisements.
- Initial verification has an extra route from selected device to browser endpoint.
- A LAN attacker may advertise a bogus endpoint, but cannot become trusted; the UI needs an exact
  error rather than a vague discovery success claim.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Send endpoint/browser bootstrap over legacy CastSession | Weakens strict browser transport separation and encourages future sensitive additions. |
| Use the same port and sniff TLS versus wire framing | Fragile, downgrade-prone, and conflicts with the explicit no-sniffing decision. |
| Hard-code a browser port/subnet scan | Violates network constraints and fails interface/port migration. |
| Put full cert/pairing secret in discovery TXT | Exposes unnecessary identity/authorization data to LAN observers. |
| Have Windows trust discovery IP/name alone | Discovery is not authenticated identity. |

## Invariants and validation

- Typed advertisement/resolver tests cover absent, stale, duplicate, changed port, changed network,
  wrong address, receiver restart, and withdrawal.
- Tests prove metadata includes no pairing code/token/raw certificate/fingerprint or browser
  content.
- BrowserSession test doubles show a valid discovery record plus wrong pin cannot reach AUTH.
- Listener/discovery lifecycle test proves endpoint is not advertised while unbound/stopped.
- Target network-change run records resulting UI and reconnect state.

## Revisit criteria

A different bootstrap mechanism must retain explicit interface binding, zero browser secrets in
unauthenticated metadata, dynamic endpoint handling, and certificate-pin identity verification.
It needs a new ADR and target spoof/restart evidence.

## References

- [Slice 03 — secure BrowserSession bootstrap](../SLICE-03-SECURE-BROWSER-SESSION-BOOTSTRAP.md)
- [ADR-0003 — separate pinned TLS](ADR-0003-SEPARATE-PINNED-TLS-BROWSER-SESSION.md)
