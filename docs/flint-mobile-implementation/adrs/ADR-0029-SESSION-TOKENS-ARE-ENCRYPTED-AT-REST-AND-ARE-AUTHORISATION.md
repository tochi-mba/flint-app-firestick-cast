# ADR-0029 — session tokens are stored encrypted at rest and are authorisation, never encryption

- **Status:** Accepted
- **Implementation:** Planned
- **Date:** 2026-09-11
- **Scope:** Flint Mobile
- **Deciders:** Flint engineering
- **Owners:** Slice 04 (pairing, the cast session, and the token store) and Slice 01 (capability
  honesty and the copy rules); Slice 09 owns the published release notes the same rules bind.
- **Related:** ADR-0024, ADR-0025, ADR-0026, ADR-0027, ADR-0028, ADR-0030, and Fire TV browser
  [ADR-0003](../../fire-tv-browser-implementation/adrs/ADR-0003-SEPARATE-PINNED-TLS-BROWSER-SESSION.md),
  which describes the only session in this product that may be called encrypted
- **Supersedes:** None
- **Superseded by:** None

## Context

The television already grants a token and no host has ever kept one. `ReceiverHandshake.onAuth`
answers a successful `AuthMessage(PAIRING_CODE)` with `AuthMessage(SESSION_TOKEN, …)`,
`SenderHandshake` captures it as `grantedToken`, and `CastSessionMachine` hands it out on
`SessionEffect.Established`. The Windows host then drops it: `CastPageViewModel` requires the
six-digit code to be typed on every connect, and `CastSession.ConnectAsync` takes a pairing code
rather than a token. On a desktop beside the television that is mildly annoying. On a phone it is
the difference between casting and walking across the room, because the code is only legible while
the receiver is in the foreground on the screen the person is trying to use.

So the phone is the first Flint host with a reason to persist the token, which makes storage a real
question for the first time. The default answer is the dangerous one. A `SharedPreferences` XML file
lands in the application's data directory in plain text, and Android's app-data backup and
device-transfer stream copy that directory wholesale unless the manifest says otherwise. A token is
a bearer credential: `ReceiverHandshake` accepts whoever presents the matching bytes, with no bound
identity behind it. Restored onto a different handset, a copied token authorises that handset
against the television with no code and no prompt. The receiver's own manifest already recognised
the shape of this problem and set `android:allowBackup="false"` with a comment explaining that
Keystore ciphertext must not travel in a device-transfer stream.

The second pressure is linguistic and it is the one this record exists for. Once a file is genuinely
encrypted and a screen says "paired", the next sentence somebody writes is that the cast link is
private. It is not. The cast wire is length-prefixed framing over plain TCP on a hotspot, and
anybody associated with that hotspot can read every frame of it. `AGENTS.md` states the rule in its
network section — tokens are authorisation and not encryption, and until an authenticated encrypted
transport is implemented and verified, no document or UI string may call local traffic private or
encrypted. `HonestyRules.CONFIDENTIALITY_WORDS` in `:castcore` already lists the words, with a
comment explaining that an exception for "a secure place to keep it" is the first step towards an
exception for the sentence that matters. What has been missing is a test that runs it over
everything.

## Decision

- A granted `SessionToken` is stored **per receiver**, keyed by that receiver's stable identity, in
  a file `:mobile` owns in its private storage. It is not a `SharedPreferences` entry.
- The record is sealed with **AES-256-GCM under a key generated in the Android keystore**, which is
  non-exportable and never leaves it. A fresh random initialisation vector is used for every write,
  `setRandomizedEncryptionRequired(true)` is set, and the receiver identity is passed as associated
  data so a record cannot be lifted from one television's entry to another's.
- Storage **fails closed**. If the keystore is unavailable, the key has been invalidated, or
  authentication of the ciphertext fails, the record is discarded and the phone asks for the pairing
  code again. There is no plaintext fallback and no guessed plaintext, matching the policy
  `AndroidKeystoreBrowserNetworkCrypto` already states on the receiver.
- `:mobile`'s manifest sets **`android:allowBackup="false"`** and grants no data-extraction
  exemption for the token file, so a token cannot travel in a backup or device-transfer stream.
- **`androidx.security:security-crypto` is rejected.** `EncryptedSharedPreferences` is deprecated,
  so adopting it means taking a dependency with no maintenance path — and its transitive Tink
  surface — in exchange for roughly eighty lines of keystore work this repository has already
  written once and tested. That is a maintenance problem bought in the shape of a security property.
- The token value **never reaches a log line, diagnostic export, crash report or screenshot**;
  neither does the pairing code. Presence, age and the receiver it belongs to may be logged. The
  bytes may not.
- **The cast link is authorised, not encrypted.** No string in `:castcore`, `:mobile`, the logs, the
  release notes or this documentation set may describe cast traffic as private, confidential, secure
  or encrypted. Only the pinned-TLS browser session of ADR-0003 may be described that way, and the
  phone app does not open one. Where the phrase "encrypted at rest" appears at all, it names the
  file and never the link.
- A rejected token is a failure, not a state to paper over. `AuthMethod.SESSION_TOKEN` returning
  `AUTHENTICATION_FAILED` clears the record and returns the person to the pairing code. Nothing
  shows "connected" on a credential the television refused.

## Consequences

### Positive

- The everyday case — casting again to a television already paired — costs no trip to the screen.
- A stolen backup, a transfer to a new handset and an extracted data directory all yield ciphertext
  whose key stayed on the old phone.
- Corruption, tampering and a wiped keystore all land on the same safe path: re-pair with the code.
- The confidentiality claim becomes executable. A reader who believes "private" behaves differently
  on a shared hotspot, so the words are enforced rather than reviewed.

### Trade-offs

- The key is device-bound by design, so a new phone pairs again. That is the property, not a defect.
- The receiver's server generates its token when it is constructed, so a stored token stops matching
  once the television restarts the receiver. The saved credential is a convenience with a short life
  and it will often cost one refused handshake before the code prompt appears.
- Flint maintains its own file format, versioning and corruption handling instead of a library's.
- The word list is blunt, and per-string exemptions are not granted. Copy that reads well and claims
  confidentiality will be rejected and rewritten.
- The emulator and physical-device tests for keystore storage and re-pairing are written but have
  not been run.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| `EncryptedSharedPreferences` from `androidx.security:security-crypto`. | The library is deprecated. Taking a dependency with no maintenance path, plus its transitive Tink surface, to avoid eighty lines of `KeyGenParameterSpec` and `Cipher` code already proven in this repository buys a future migration rather than a security property. |
| Plain `SharedPreferences` or a plain file, relying on app-private storage. | App-private storage is not private on a rooted or debuggable device, and it is exactly what the backup and device-transfer stream copies. A bearer token restored onto another handset authorises that handset, because `ReceiverHandshake` binds nothing but the bytes. |
| Derive the storage key from the six-digit pairing code. | `PairingCode` is six ASCII digits — a million candidates, exhausted offline against the stored file in negligible work. It is a human confirmation value, not key material. |
| Require user authentication on the keystore key (`setUserAuthenticationRequired(true)`). | Reconnection happens while the phone is being used as the cockpit and from service callbacks, so a key needing a fresh unlock turns a silent reconnect into a biometric prompt, and its failure is indistinguishable from a token the television refused. |
| Keep no token at all, as the Windows host does. | It is not a security improvement. The same token crosses the same unencrypted wire either way; refusing to store it only keeps it in memory and sends the person back to the television for a code on every session. |
| Call the link secure on the strength of the stored token and the pairing step. | No transport encryption exists. Frames are readable by anyone on the hotspot, and `AGENTS.md` forbids the claim until an authenticated encrypted transport is implemented and verified. |

## Invariants and validation

- A **copy test in `:castcore`** walks every user-facing string reachable from `:castcore` — the
  `copy` objects, every `ModeVerdict` reason and remedy the assessor can produce — and every string
  resource in `:mobile`, and fails when `HonestyRules.confidentialityClaims` returns anything for
  one of them. The browser context is the only exemption, and the phone app has no browser strings,
  so the exemption list must stay empty here. It needs no device, emulator or network.
- A **source guard** fails on `EncryptedSharedPreferences`, `MasterKey` or any
  `androidx.security` import in `:mobile` and `:castcore` sources, and a manifest test asserts
  `android:allowBackup="false"` with no data-extraction exemption covering the token file.
- A **crypto contract test** proves a tampered ciphertext byte, a swapped initialisation vector and
  a record sealed under a different receiver identity all raise rather than return plaintext, and
  that two writes of the same token produce different ciphertext.
- A **redaction test** seals a known token, exercises the pairing, session and failure paths, and
  asserts that no emitted log line, diagnostic string or error message contains the token or the
  pairing code.
- A **session test** drives `CastSessionMachine` through a refused `SESSION_TOKEN` handshake and
  asserts the record is cleared, the pairing prompt returns, and no state claims a connection.
- The real Android keystore path is an instrumented test on an emulator and a physical handset. It
  is written, it has not been run, and no result is claimed for it.

## Revisit criteria

Only one thing retires the language ban: an authenticated encrypted transport for the cast wire,
agreed byte for byte across Kotlin, C# and Rust with regenerated golden vectors, and verified on
real hardware. When that exists, a superseding record names precisely which traffic it covers and
what wording becomes true. Nothing less does — not a stored token, not a pairing step, not a
hotspot the user believes is theirs, and not pressure from a release note that wants a stronger
word. The storage decision itself is revisited if Android ships a maintained first-party encrypted
preference API, or if device evidence shows keystore key invalidation is common enough on target
handsets that the fail-closed path becomes the normal one rather than the exception.

## References

- [Project constraints](../../../AGENTS.md)
- [Flint Mobile implementation slices](../README.md)
- [`SessionToken`](../../../protocol/src/main/kotlin/com/rextechnologies/flint/protocol/http/SessionToken.kt)
- [`PairingCode`](../../../protocol/src/main/kotlin/com/rextechnologies/flint/protocol/discovery/PairingCode.kt)
- [`ReceiverHandshake` and `SenderHandshake`](../../../protocol/src/main/kotlin/com/rextechnologies/flint/protocol/session/CastHandshake.kt)
- [`CastSessionMachine`](../../../castcore/src/main/kotlin/com/rextechnologies/flint/castcore/session/CastSessionMachine.kt)
- [`HonestyRules`](../../../castcore/src/main/kotlin/com/rextechnologies/flint/castcore/copy/Honesty.kt)
- [`AndroidKeystoreBrowserNetworkCrypto`](../../../receiver/src/main/kotlin/com/rextechnologies/flint/receiver/browser/BrowserNetworkCrypto.kt)
- [Receiver manifest and its backup exclusion](../../../receiver/src/main/AndroidManifest.xml)
- [`ReceiverServer`](../../../receiver/src/main/kotlin/com/rextechnologies/flint/receiver/net/ReceiverServer.kt)
- [`CastPageViewModel`, which stores no token today](../../../src/Flint.App/ViewModels/CastPageViewModel.cs)
