# ADR-0029 — session tokens are stored encrypted at rest and are authorisation, never encryption

- **Status:** Accepted
- **Implementation:** Planned
- **Date:** 2026-09-11
- **Scope:** Flint Mobile
- **Deciders:** Flint engineering
- **Owners:** Slice 04 (pairing, the cast session, and the token store) and Slice 01 (capability
  honesty and the copy rules); Slice 09 owns the published release notes, which the same rules bind
- **Related:** ADR-0024, ADR-0025, ADR-0026, ADR-0027, ADR-0028, ADR-0030, and Fire TV browser
  [ADR-0003](../../fire-tv-browser-implementation/adrs/ADR-0003-SEPARATE-PINNED-TLS-BROWSER-SESSION.md),
  which describes the only session in this product that may be called encrypted, with
  [ADR-0004](../../fire-tv-browser-implementation/adrs/ADR-0004-NONSECRET-ENDPOINT-DISCOVERY.md) on
  why discovery is never identity
- **Supersedes:** None
- **Superseded by:** None

## Context

The television already grants a token and no host has ever kept one. `ReceiverHandshake.onAuth`
answers a matching `AuthMessage(PAIRING_CODE)` with an ack carrying `AuthMethod.SESSION_TOKEN`,
`SenderHandshake` captures it as `grantedToken`, and `CastSessionMachine` hands it out when the
session is established. The Windows host then drops it, because `CastPageViewModel` asks for the six
digits on every connect. Beside a desk that is mildly annoying. On a phone it decides whether casting
happens at all, since the code is only legible while the receiver is in the foreground on the very
screen the person wants to cast to.

The phone is therefore the first Flint host with a reason to persist a token, which makes storage a
real question for the first time, and the default answer is the dangerous one. A `SharedPreferences`
value is plain text in the application's data directory, and Android's app-data backup and
device-transfer stream copy that directory wholesale unless the manifest says otherwise. A
`SessionToken` is 256 bits of bearer credential: `constantTimeMatches` compares bytes and binds no
identity behind them, so a token restored onto a different handset authorises that handset against
the television with no code and no prompt. The receiver's manifest already recognised the shape of
this and set `android:allowBackup="false"`, with a comment saying Keystore ciphertext must not travel
in a device-transfer stream.

The second pressure is linguistic, and it is the one this record exists for. Once a file is genuinely
encrypted and a screen says "paired", the next sentence somebody writes is that the cast link is
private. It is not. Cast frames are length-prefixed over plain TCP on a hotspot, and anybody
associated with that hotspot can read every one of them. `AGENTS.md` states the rule in its network
section, and `HonestyRules.CONFIDENTIALITY_WORDS` in `:castcore` already lists the words, with a
comment explaining that an exception for "a secure place to keep it" is the first step towards an
exception for the sentence that matters. What has been missing is a test that runs that list over
everything.

## Decision

- A granted `SessionToken` is stored **per receiver**, keyed by the receiver address, in
  `flint-mobile-tokens` — a `MODE_PRIVATE` file `:mobile` owns. The token's characters never reach
  that file. What is written is Base64 of a length-prefixed initialisation vector followed by
  AES-256-GCM ciphertext with a 128-bit tag.
- The key is **generated in the Android keystore and never leaves it**, under alias
  `flint-mobile-token`, GCM block mode with no padding, so the platform supplies a fresh
  initialisation vector on every encryption and refuses a caller-supplied one. The vector is stored
  length-prefixed rather than assumed to be twelve bytes, because a provider is entitled to choose a
  different nonce size and a hard-coded split would fail on the handset that did.
- The key requires **no user authentication**. Anyone holding the unlocked phone can start a cast
  from the app regardless, so a fingerprint gate would cost a tap and protect nothing.
- Storage **fails closed**. An unavailable keystore, an invalidated key, or a tag that does not
  authenticate discards the record and returns the person to the pairing code. There is no plaintext
  fallback and no partial read.
- `:mobile`'s manifest sets **`android:allowBackup="false"`** with no data-extraction exemption
  covering the token file, so a token cannot travel in a backup or a device-transfer stream.
- **`androidx.security:security-crypto` is rejected.** `EncryptedSharedPreferences` is deprecated, so
  adopting it means taking a dependency with no maintenance path, and its transitive Tink surface, in
  place of roughly eighty lines of `KeyGenParameterSpec` and `Cipher` work. That buys a maintenance
  problem rather than a security property.
- The token bytes and the pairing code **never reach a log line, diagnostic export or crash report**.
  Presence, age and the receiver an entry belongs to may be logged. The characters may not.
- **The cast link is authorised, not encrypted.** No string in `:castcore`, `:mobile`, the logs, the
  release notes or this documentation set may describe cast traffic as private, confidential, secure
  or encrypted. Only the pinned-TLS browser session of ADR-0003 may be described that way, and the
  phone app does not open one. Where "encrypted at rest" appears, it names the stored file and never
  the link.
- A refused token is a failure, not a state to paper over. A `SESSION_TOKEN` attempt that returns
  `AUTHENTICATION_FAILED` clears the record and shows the code prompt. Nothing claims a connection on
  a credential the television rejected.

## Consequences

### Positive

- Casting again to a paired television costs no walk to the screen the person is already using.
- A lifted data directory, a backup and a transfer to a new handset all yield ciphertext whose key
  stayed in the old phone's keystore.
- Corruption, tampering and a wiped key land on one safe path: pair again with the code.
- The confidentiality boundary becomes executable rather than reviewed, which matters because a
  reader who believes "private" behaves differently on a shared hotspot.

### Trade-offs

- The key is device-bound by design, so a new phone pairs again. That is the property, not a defect.
- `ReceiverServer` generates its token when it is constructed, so a stored token stops matching once
  the television restarts the receiver. The saved credential is a convenience with a short life, and
  it will often cost one refused handshake before the code prompt appears.
- Flint owns its record format, versioning and corruption handling instead of a library's.
- The word list is blunt and per-string exemptions are not granted. Copy that reads well and claims
  confidentiality is rejected and rewritten.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| `EncryptedSharedPreferences` from `androidx.security:security-crypto`. | The library is deprecated. A dependency with no maintenance path, plus its transitive Tink surface, replaces eighty lines of keystore code with a migration nobody has scheduled. |
| Plain `SharedPreferences` or a plain file, trusting app-private storage. | That directory is exactly what the backup and device-transfer stream copies, and it is readable on a rooted or debuggable handset. `ReceiverHandshake` binds nothing but the bytes, so a restored copy authorises the handset that restored it. |
| Derive the storage key from the six-digit pairing code. | `PairingCode` is six ASCII digits. A million candidates is an offline exhaustion against the stored file in negligible work; it is a human confirmation value, not key material. |
| `setUserAuthenticationRequired(true)` on the keystore key. | Reconnection happens from service callbacks while the phone is being used as the cockpit, so the key turns a silent reconnect into a biometric prompt whose failure is indistinguishable from a token the television refused. |
| Keep no token at all, as the Windows host does. | It is not a security improvement. The same token crosses the same unencrypted wire either way; refusing to store it only keeps it in memory and sends the person back to the television on every session. |
| Call the link secure on the strength of pairing and a sealed token. | No transport encryption exists. Frames are readable by anyone on the hotspot, and `AGENTS.md` forbids the claim until an authenticated encrypted transport is implemented and verified. |

## Invariants and validation

- **A copy test fails the build on the words.** `CopyVoiceTest` in `:castcore` already walks every
  copy constant and every `ModeVerdict` reason and remedy the assessor can produce, asserting
  `HonestyRules.confidentialityClaims` returns nothing. It is extended to `:mobile`'s string
  resources, which is a short list because product copy deliberately lives in `:castcore` as Kotlin
  constants. The constants are enumerated explicitly rather than found by reflection, so adding a
  surface makes somebody come to the list. The browser context is the only exemption the rule admits,
  and the phone app has no browser strings, so that exemption list stays empty here. No device,
  emulator or network is required.
- **A source guard** fails on any `androidx.security` import in `:mobile` or `:castcore`, and a
  manifest assertion covers `android:allowBackup="false"` with no data-extraction exemption reaching
  the token file.
- **A crypto contract test** proves that a flipped ciphertext byte, a truncated record and a swapped
  initialisation vector all fail rather than return plaintext, and that two writes of one token
  produce different ciphertext.
- **A redaction test** seals a known token, drives the pairing, session and failure paths, and
  asserts no emitted log line, error or diagnostic string contains the token or the pairing code.
- **A session test** drives `CastSessionMachine` through a refused `SESSION_TOKEN` handshake and
  asserts the record is cleared, the code prompt returns, and no state claims a connection.
- The real keystore cannot run on the JVM, so that path is an instrumented test on an emulator and on
  a physical handset. Nothing has run it yet, and no result is claimed for it.

## Revisit criteria

One thing retires the language ban: an authenticated encrypted transport for the cast wire, agreed
byte for byte across Kotlin, C# and Rust with regenerated golden vectors, and verified on real
hardware. A superseding record would then name exactly which traffic it covers and which wording
becomes true. Nothing less qualifies — not a stored token, not a pairing step, not a hotspot the user
believes is theirs, and not a release note that wants a stronger word. The storage decision itself is
revisited if Android ships a maintained first-party encrypted preference API, or if device evidence
shows key invalidation is common enough on target handsets that the fail-closed path becomes the
normal one rather than the exception.

## References

- [Project constraints](../../../AGENTS.md)
- [Flint Mobile implementation slices](../README.md)
- [`TokenStore`](../../../apps/phone/app/src/main/kotlin/com/rextechnologies/flint/mobile/platform/TokenStore.kt)
- [`:mobile` manifest](../../../apps/phone/app/src/main/AndroidManifest.xml)
- [`HonestyRules`](../../../apps/phone/core/src/main/kotlin/com/rextechnologies/flint/castcore/copy/HonestyRules.kt)
- [`CopyVoiceTest`](../../../apps/phone/core/src/test/kotlin/com/rextechnologies/flint/castcore/copy/CopyVoiceTest.kt)
- [`CastSessionMachine`](../../../apps/phone/core/src/main/kotlin/com/rextechnologies/flint/castcore/session/CastSessionMachine.kt)
- [`SessionToken`](../../../protocol/kotlin/src/main/kotlin/com/rextechnologies/flint/protocol/http/SessionToken.kt)
- [`ReceiverHandshake` and `SenderHandshake`](../../../protocol/kotlin/src/main/kotlin/com/rextechnologies/flint/protocol/session/CastHandshake.kt)
- [`ReceiverServer`](../../../apps/receiver/app/src/main/kotlin/com/rextechnologies/flint/receiver/net/ReceiverServer.kt)
