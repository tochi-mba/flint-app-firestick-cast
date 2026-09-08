# Slice 03 — secure BrowserSession bootstrap

**Governing ADRs:** [ADR-0003](adrs/ADR-0003-SEPARATE-PINNED-TLS-BROWSER-SESSION.md), [ADR-0004](adrs/ADR-0004-NONSECRET-ENDPOINT-DISCOVERY.md), [ADR-0005](adrs/ADR-0005-PROTOCOL-V2-CANONICAL-CONTRACT.md), and [ADR-0011](adrs/ADR-0011-TEST-EVIDENCE-AND-LATENCY-GOVERNANCE.md).

## Outcome

A selected Windows host and receiver can establish exactly one **separate**, TLS-protected browser
session. First pairing requires a human comparison of the TV and Windows fingerprint plus the
existing pairing code inside TLS. Later sessions require an exact persisted pin. The host receives a
secure browser capability response and shows “secure receiver ready — browser installation
continues in the next slice.”

No URL, text, dialog, preview, browser command, or WebView object exists in the normal product path
yet. This slice proves that it can never be tempted onto the legacy plaintext CastSession.

## Entry criteria

- Slice 02's v2 vectors and ordinary-channel rejection proof are green.
- The chosen endpoint-discovery design has a written threat model: endpoint metadata is non-secret,
  but it is not trusted as a receiver identity; the certificate pin is the identity proof.
- A device has passed or explicitly failed Slice 01's probe. An explicitly failed device remains
  unavailable and cannot begin a TLS setup flow.

## User-visible vertical behaviour

1. The Windows Web destination says why it is unavailable, or offers **Verify secure receiver** only
   for an eligible v2 receiver.
2. The TV displays the short fingerprint in its browser/pairing screen. Windows displays the same
   value and a clear instruction to compare the two physical screens.
3. After the user accepts the matching fingerprint, Windows sends the pairing code only over the
   established TLS session. A success pins the full SPKI SHA-256 fingerprint in the Windows trust
   store.
4. A later matching receiver reconnects silently within the existing pairing/session rules. A
   mismatch, expired/unusable identity, bad code, downgrade, or endpoint change blocks the browser
   flow with a specific recoverable error. It never quietly falls back to the plain control socket.
5. Once secure capability arrives, the UI says browser navigation is pending. It does not enable
   address, text, preview, or remote controls.

## Scope

### In scope

- A dedicated receiver BrowserTlsServer on an explicit validated IPv4 interface and a dedicated
  Windows BrowserSession TLS client. The existing ReceiverServer and CastSession retain their
  legacy purpose unchanged.
- Non-secret endpoint advertisement/discovery metadata for the TLS port and v2 support. Choose one
  documented route, preferably the receiver's own mDNS/TXT metadata, and test the Windows discovery
  consumer. It must not be the only trust decision.
- ReceiverIdentity backed by Android Keystore: persistent private key, self-signed certificate,
  stable full SPKI SHA-256 pin, and a human-comparable short fingerprint. The private key is never
  exported or logged.
- BrowserTrustStore backed by DPAPI or the platform credential store: full pin, receiver identity
  key, atomic persistence, corruption handling, explicit remove/forget behaviour, and no secret
  written to logs.
- TLS 1.2 or newer only. The receiver uses API-25-compatible configuration; the host does not
  compensate for invalid certificates by accepting all policy errors.
- TLS-only v2 HELLO/AUTH/capability exchange, pairing-code rate limiting, one active client, clean
  cancellation/reconnect, and a bounded writer architecture.
- Browser-specific receiver restart/session-end cleanup, including port withdrawal, writer
  cancellation, buffer disposal, and a final state transition.
- UI states for verify, ready, mismatch, rejected code, unsupported endpoint, and disconnected.

### Explicitly out of scope

- WebView creation, navigation, text, pointer/semantic input, preview production/decoding, page
  dialogs, or persisted browser data.
- Using the existing PushbackInputStream/first-byte classifier to multiplex TLS and plaintext on one
  port. TLS has its own listener; no protocol sniffing or byte pushback is allowed.
- A self-signed certificate “trust all” callback, user override of a pin mismatch, certificate
  export, a plain fallback, or a second simultaneous browser client.
- Refactoring the legacy ReceiverServer send path opportunistically. Browser traffic owns a
  dedicated bounded writer so preview cannot inherit one-coroutine-per-send behaviour later.

## Trust and transport contract

| Topic | Required rule | Failure response |
|---|---|---|
| Endpoint bootstrap | Advertise only port/protocol metadata; treat it as untrusted routing information. | Do not connect automatically to an unpinned endpoint. |
| Listener binding | Bind receiver listener to the validated selected Inet4Address, never wildcard. Restart on network change. | Close listener/session and publish unavailable/disconnected. |
| Host source binding | Bind the client source address explicitly using the same tested approach as CastSession network selection. | Show a network-selection error; do not alter default route. |
| TLS version | Negotiate TLS 1.2 or later; never downgrade for API-25 compatibility. | Reject before AUTH. |
| Identity | Keystore private key persists only on receiver; Windows stores full SHA-256 SPKI pin. | Refuse missing/corrupt/unexpected identity. |
| First pairing | Human compares TV/Windows short code, then host sends pairing code inside TLS. | Reject/abort leaves no pin unless verification succeeded. |
| Reconnect | Exact full pin is mandatory; user cannot click through a mismatch. | Block and offer explicit Forget receiver / re-verify flow. |
| Authentication | Existing pairing code is rate-limited and checked only after TLS + pin rule. | Close session with a bounded safe error; no code echo/log. |
| Framing | Browser v2 frames are accepted only after TLS, identity, pairing, and capability gate. | Close/reject; ordinary listener cannot relay them. |
| Capacity | One active client; reliable control/state queue is bounded; preview has one overwrite slot when later enabled. | Reject extra client; apply backpressure/disconnect policy with counters. |

## Planned files and boundaries

| Area | Planned change |
|---|---|
| receiver net | New BrowserTlsServer, secure framed session, explicit interface binding, lifecycle/restart integration, bounded writer. |
| receiver identity | Keystore identity provider, self-signed certificate factory/adapter, fingerprint formatter, TV pairing presentation state. |
| receiver service/UI | Browser secure-session lifecycle/state only; no WebView ownership. Add secure endpoint status/fingerprint display. |
| receiver discovery | Non-secret endpoint/protocol advertisement and tests for add/change/remove/network restart. |
| src/Flint.Session | BrowserSession, browser transport interface, trust policy, DPAPI trust store, source-address binding, serialized writer/reader. |
| src/Flint.App | Verify secure receiver and secure-ready page states; trust decision and forget-receiver commands. |
| tests/Flint.Session.Tests | Loopback TLS, pin, trust-store, protocol, source binding, cancellation, writer priority tests. |
| receiver tests | Identity fake/provider, listener, restart, pairing, one-client, rejection, and TV-screen model tests. |
| docs | TLS boundary, endpoint-discovery truth, pin lifecycle, recovery instructions, and non-secret telemetry definitions. |

## Test-first implementation order

| Step | Red test to add first | Minimum production change | Required green proof |
|---:|---|---|---|
| 1 | BrowserEndpointAdvertisementTests prove endpoint metadata is absent/stale/wrong after network transition. | Add typed non-secret advertisement and Windows discovery parse/expiry logic. | Metadata never creates a trusted state before pin verification. |
| 2 | FingerprintNormalizerTests prove same SPKI formats identically and invalid/short/truncated values fail. | Shared narrow full-hash and display-code formatter. | Full pin versus display code cannot be confused in model/UI/log tests. |
| 3 | ReceiverIdentityProviderTests use fake Keystore and prove create-once, reopen, non-export, failure/recovery. | Android identity port plus real Keystore adapter. | No production state exposes private key/certificate raw bytes unnecessarily. |
| 4 | BrowserTrustStoreTests cover first write, atomic replace, corruption, user forget, duplicate device identity, and redaction. | DPAPI/credential-backed store behind fakeable interface. | Pin data survives expected restart and never appears in telemetry. |
| 5 | FirstPairingTests cover user accepts/rejects/mismatched TV code and prove pairing code is not sent before acceptable pin. | BrowserSession TLS handshake/trust decision state machine. | Loopback capture asserts no AUTH payload before pin decision. |
| 6 | TlsPolicyTests reject TLS below 1.2, unpinned/mismatched cert, wrong host response, wrong code, replay, and rate limit. | Strict client/server TLS/auth policy. | All failures are safe, bounded, specific, and no plaintext retry occurs. |
| 7 | BrowserTlsServerTests cover explicit bind, one active client, cancellation, network restart, shutdown, and browser frames before auth. | Dedicated listener/session lifecycle. | Plain ReceiverServer and secure listener each reject wrong traffic without byte sniffing. |
| 8 | BrowserWriterTests cover control/state order, bounded reliable queue, cancellation, and replacement of the one preview slot. | Dedicated writer loop/mailboxes with injected scheduler. | No coroutine/task per message and no queue may exceed its declared capacity. |
| 9 | BrowserSecureCapabilityTests cover v2 HELLO/AUTH/capability flow and v1/no-capability unavailable UI. | TLS-only secure capability exchange and page state mapper. | Host state changes to secure-ready only after every gate is true. |
| 10 | BrowserTlsHardwareTest is explicit/ignored and checks target handshake/pin/reconnect. | Add hardware test runner scenario and evidence schema. | Packet capture/manual inspection shows pairing code and capability bytes are not readable on the browser LAN channel. |

### Required adversarial cases

The tests must include at least these conditions:

- Spoofed mDNS/endpoint port, stale endpoint advertisement, changed port on known device, unavailable
  interface, wildcard bind attempt, wrong local source address, and a network change while handshaking.
- Certificate not yet trusted, changed full SPKI with same short display collision attempt,
  malformed certificate, invalid validity period, TLS 1.0/1.1 proposal, failed handshake, and a
  user rejecting the displayed code.
- Pairing code sent too early, incorrect code, replayed AUTH frame, rate-limit boundary,
  simultaneous second client, host cancellation, receiver shutdown, and reconnect after normal
  disconnect.
- Browser frame on legacy CastSession, browser frame before TLS, before pin, before AUTH, after
  close, at v1, and after capability says unavailable.
- Slow peer, full control queue, full preview slot, cancellation between dequeue/write, and writer
  exception. Control/state must remain ordered and preview must be dropped/replaced, never
  accumulated.

## Full-suite checkpoint

Focused development uses unit/loopback filters first, then ends with:

~~~powershell
.\scripts\build.ps1
.\gradlew.bat --no-daemon :protocol:check :receiver:testDebugUnitTest :receiver:lintDebug --console plain
python scripts/check-site.py
~~~

The physical TLS capture/reconnect test is explicit and named. It must never run through normal CI
or install/replace a receiver without the serial/acknowledgement runner from Slice 01.

## Latency/resource checkpoint

Handshake security has priority over speed. Measure, but do not optimize away verification:

- Record receiver listener start, TLS handshake, pin decision, pairing result, and capability receipt
  as separate local spans. Report failure class and sample count, never the pairing code or raw cert.
- Establish baseline connection/reconnect percentiles on the target only after enough warm/cold
  samples. Label them measured; do not promise them as user-visible latency.
- Assert queue capacity, maximum outstanding reliable messages, preview-slot count, task/thread
  count, and cancellation latency in deterministic loopback tests.
- No browser preview is emitted in this slice. The preview mailbox is tested as an empty capacity-one
  policy only, so it cannot contaminate handshake/control measurements.

## Exit evidence

| Evidence | Required result |
|---|---|
| Trust proof | First pairing requires physical fingerprint comparison; full pin persists; mismatch has no bypass. |
| Encryption proof | Target capture/manual inspection confirms pairing code and browser capability are not readable on the BrowserSession channel. |
| Fallback proof | Legacy CastSession and ordinary ReceiverServer reject all browser frames/surfaces; no TLS failure triggers a plain retry. |
| Lifecycle proof | Explicit bind/restart/close paths leave no active listener/session/writer leak and allow normal reconnect. |
| Capacity proof | One client, bounded reliable queue, and capacity-one future preview mailbox are demonstrated by tests. |
| Coverage | New trust, endpoint, queue, session, and UI-state decisions are 100% line/branch covered; protocol vectors remain green. |
| User experience | Verify, secure-ready, mismatch, rejection, and unavailable states are accessible and exact. |

## Rollback and stop conditions

A browser secure session is an independent capability. Disable its advertisement/listener/UI entry if
a defect is found; do not change the legacy CastSession security claim or transport behaviour.

Stop rather than continue if Android Keystore cannot supply a stable identity on the target, the
host cannot pin it safely, TLS 1.2 cannot be negotiated, source/interface binding is unreliable, an
endpoint-discovery route leaks trust, or packet evidence contradicts encryption. Do not implement
navigation before these facts are resolved.
