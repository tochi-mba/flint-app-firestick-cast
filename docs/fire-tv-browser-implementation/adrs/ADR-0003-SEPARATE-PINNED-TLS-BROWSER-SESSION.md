# ADR-0003 — separate pinned TLS BrowserSession

- **Status:** Accepted
- **Implementation:** Planned
- **Date:** 2026-09-04
- **Deciders:** Flint engineering
- **Owners:** Slices 02–09
- **Related:** ADR-0004, ADR-0005, ADR-0008, ADR-0009

## Context

The existing CastSession/ReceiverServer control channel is authorization-oriented but plaintext.
Its pairing token does not encrypt URLs, typed text, dialogs, or preview pixels. Reusing it for
browser interaction would make a sensitive feature appear private when it is not. It also uses a
legacy server shape that should not be made to sniff/multiplex TLS bytes.

Browser controls need both authenticated encryption and a receiver identity that a person can verify
on the physical TV. A LAN discovery record alone is not a trustworthy identity.

## Decision

Introduce a separate BrowserSession with these mandatory properties:

- receiver runs BrowserTlsServer on its own explicitly validated IPv4 listener; it never shares the
  legacy plaintext port or first-byte/PushbackInputStream classifier;
- TLS 1.2 or newer only; no downgrade and no trust-all callback;
- receiver creates/persists a private key in Android Keystore and presents a self-signed certificate;
  the private key is never exported;
- Windows uses the full SHA-256 SPKI pin stored through DPAPI/credential storage. A short display
  fingerprint is only for human comparison, not the stored identity;
- first pairing: user compares TV and Windows fingerprint, accepts deliberately, then sends the
  pairing code inside established TLS; later sessions require exact pin match;
- BrowserSession permits one active client and uses bounded control/state queues plus a future
  capacity-one preview mailbox;
- browser typed frames are accepted only after TLS, pin, pairing authentication, v2 negotiation, and
  secure capability success. There is no plaintext fallback.

## Consequences

### Positive

- URLs, text, dialogs, and optional preview have an explicit authenticated encrypted transport.
- A spoofed endpoint can route a failed handshake but cannot become trusted without the physical pin
  verification; pin mismatch cannot be clicked through.
- Legacy media/mirror behaviour can remain stable while browser transport evolves independently.
- Bounded writer design prevents optional preview from inheriting legacy unbounded send behaviour.

### Trade-offs

- A second listener, identity lifecycle, trust store, endpoint bootstrap, restart logic, and
  loopback/target test matrix are required.
- First pairing requires a physical-TV interaction and cannot be completely automated.
- Browser availability is unavailable when any TLS/pin/auth gate fails, even if the legacy session
  is otherwise paired.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Send browser data over current authenticated control channel | Authorization is not encryption; violates the security requirement. |
| Add TLS opportunistically to the legacy listener | Risks TLS/plain protocol sniffing, downgrade bugs, and legacy regression. |
| Trust all self-signed certificates after code entry | Makes MITM/endpoint spoofing possible and undermines physical verification. |
| Use only pairing code as identity | Pairing code is not a stable certificate identity and may be observed/replayed without TLS. |
| Use custom/bespoke cryptography | Unnecessary and riskier than platform TLS/Keystore/DPAPI primitives. |

## Invariants and validation

- Loopback captures prove AUTH/body browser bytes are absent before acceptable TLS/pin state and are
  not readable on the BrowserSession channel.
- Tests cover pin mismatch, user reject, TLS <1.2, wrong/replayed code, rate limit, second client,
  source/interface binding, cancellation, reconnect, and no plaintext fallback.
- Keystore/trust-store adapters use fakes for unit tests and real target evidence for platform
  behaviour.
- Security docs never describe the legacy channel as encrypted/private.

## Revisit criteria

A different transport must provide at least equivalent authenticated encryption, physical identity
verification, bounded backpressure, explicit socket binding, cross-language framing proof, and
no-downgrade semantics. It requires a superseding ADR and packet/test evidence.

## References

- [Slice 03 — secure BrowserSession bootstrap](../SLICE-03-SECURE-BROWSER-SESSION-BOOTSTRAP.md)
- [Slice 02 — plain-channel rejection](../SLICE-02-PROTOCOL-V2-AND-PLAIN-CHANNEL-REJECTION.md)
- [Master security design](../../FIRE_TV_BROWSER_PLAN.md#security-privacy-and-trust-design)
