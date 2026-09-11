# ADR-0026 — Flint Mobile consumes :protocol in-repo and never forks the wire

- **Status:** Accepted
- **Implementation:** Planned
- **Date:** 2026-09-11
- **Scope:** Flint Mobile
- **Deciders:** Flint engineering
- **Owners:** Slice 01 (modules, build and capability honesty) and Slice 04 (pairing and the cast
  session); the CI and release workstream owns the workflow split.
- **Related:** ADR-0024, ADR-0025, ADR-0027, ADR-0028, ADR-0029, ADR-0030, and Fire TV browser
  [ADR-0005](../../fire-tv-browser-implementation/adrs/ADR-0005-PROTOCOL-V2-CANONICAL-CONTRACT.md)
  and [ADR-0017](../../fire-tv-browser-implementation/adrs/ADR-0017-ADDITIVE-COCKPIT-MESSAGE-IDS.md).
- **Supersedes:** None
- **Superseded by:** None

## Context

The phone app is the fourth thing that has to speak this wire, after the Rust engine, the .NET host
and the Kotlin receiver. `testdata/golden/` holds 95 committed frames and the enum manifest
`browser-enums.txt`, and Rust, C# and Kotlin each assert they produce and parse those exact bytes.
That corpus is why three implementations have stayed in agreement; a round-trip test proves only
that an implementation agrees with itself.

Two failures set this decision's shape. Rust's browser dispatch range once missed an id extension,
and only decoding the committed bytes exposed it. The second is still open: `MEDIA_COMMAND`'s
mimeType cap is 255 bytes in Kotlin, which passes `MAX_MIME_BYTES`, and 512 in C# and Rust, which
pass `MAX_TITLE_BYTES` to the same field. The framing never changed; two people chose different
constants. A 256-to-512-byte MIME type therefore encodes on Windows and is rejected by Kotlin, and
no vector catches it because every committed MIME type is short.

A separate Flint-mobile repository was the obvious shape, since the phone shares no toolchain with
the Windows job. It would have had to vendor the vectors and the manifest — a copy pinned to the
revision it was taken at — or write a fourth codec with no cross-language check behind it. One
repository's cost was measurable instead: `ci.yml` had no path filters, so every phone pull request
would have run the forty-five-minute Windows job for a desktop it never touched.

## Decision

- `:mobile` and `:castcore` depend on `project(":protocol")`. No wire type, codec, handshake,
  discovery record or enum is reimplemented in phone sources. A phone need is met by using the
  module, or by a reviewed cross-language change to it, never by a private copy.
- `:protocol` is consumed **unchanged** here. Widening it — bound UDP and multicast factories, a
  converged MIME cap — is separate cross-language work with its own vectors.
- One repository. The push target is `tochi-mba/flint-app-firestick-cast` on branch
  `claude/flint-mobile-phone-host-pm9vsg`, not a separate Flint-mobile repository.
- The CI cost is paid explicitly: `paths-ignore` on `ci.yml` for `mobile/**`, `design/**`,
  `castcore/**` and the mobile workflows, and a new `.github/workflows/mobile.yml` on
  `ubuntu-latest` whose filters include `protocol/**` and `testdata/golden/**`, so a shared change
  still runs both jobs.
- The interop hazards are encoded from the first commit:
  - `HELLO` rides envelope version 1, `ProtocolVersion.MIN_SUPPORTED`, although its payload
    advertises the whole supported range: a receiver too old to parse a newer envelope must still
    read the first exchange in order to say so. Every frame after negotiation uses
    `negotiatedVersion`, never `ProtocolVersion.CURRENT`, which is this build's ceiling rather than
    the agreement. The receiver's own `HELLO` and every pre-establishment refusal ride a version 1
    envelope for the same reason.
  - `HELLO`'s codec capabilities are a `Set`, encoded ascending by codec value. That deduplication
    and ordering are what keep the vectors stable across three languages.
  - Message ids 14 to 36 are browser traffic, forbidden on the plaintext cast channel along with
    `SurfaceMode.BROWSER`; the receiver answers `BYE(PROTOCOL_ERROR, "Browser traffic requires the
    secure browser session")` and closes. The phone opens no browser session, sends none of those
    ids, and surfaces that refusal text unchanged.
  - The phone caps `MEDIA_COMMAND` mimeType at 255 bytes through `MediaHandoff.MAXIMUM_MIME_BYTES`,
    returning `null` rather than truncating, because a truncated MIME type is a different one. The
    255/512 divergence is recorded as outstanding cross-language work.
- Sharing the module changes nothing about confidentiality. The session token is authorisation; no
  string may call cast traffic private or encrypted.

## Consequences

### Positive

- The phone is held to the same committed bytes as the other three implementations, by tests already
  in the build.
- A protocol change is one diff — vectors, manifest and every consumer — not a change here and a
  follow-up elsewhere.
- `:castcore` is a plain `kotlin("jvm")` module, so the session machine, discovery ladder and media
  policy are tested with no Android SDK and no television.
- A phone-only pull request no longer runs a Windows job, and a shared change still runs both.

### Trade-offs

- The phone cannot outrun the wire. A new message waits for three languages to agree and for
  regenerated, reviewed vectors.
- The path filters are duplicated, because Actions does not resolve YAML anchors. If `ci.yml`
  becomes a required check, the branch rule needs the same exemptions: a path-filtered workflow
  reports as pending rather than passing.
- The MIME divergence stays open, so a 256-to-512-byte MIME a Windows host would send is refused
  here.
- Two products now share one review surface and one release history.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| A separate Flint-mobile repository vendoring `:protocol` and the golden corpus. | All three languages assert the 95 vectors and the manifest byte-identical from one directory. A vendored copy is pinned to the revision it was taken at, and nothing fails when the original moves. |
| A separate repository with a fresh Kotlin codec for the phone. | A fourth implementation with no cross-language check; Rust's missed browser id extension was found only by decoding committed bytes. |
| Publish `:protocol` as a versioned Maven artefact. | The corpus is data, not code, so it ships as a resource or is duplicated. No publishing pipeline exists, and a wire change would land green in one repository and red in the other. |
| Fork the wire: send `CURRENT` on `HELLO`, cap MIME at 512 to match C#. | A `HELLO` on a newer envelope cannot be parsed by the receiver that must answer it, turning a graceful `UNSUPPORTED_VERSION` refusal into a dead socket. Raising the cap also changes `:protocol` for the receiver. |
| Keep one repository and leave `ci.yml` unfiltered. | Every phone pull request pays forty-five Windows minutes for a desktop it did not touch, and a check that is always slow and irrelevant is one reviewers learn to ignore. |

## Invariants and validation

The invariant is the golden corpus and the enum manifest. A phone change that alters encoded bytes
or an enum's value set must turn `:protocol`'s `GoldenVectorTest` and `BrowserEnumManifestTest` red,
alongside `golden.rs`, `enum_manifest.rs` and their C# counterparts. Regeneration stays gated behind
`cargo test --test golden -- --ignored regenerate`.

- A source guard in `:castcore` fails the build if phone sources declare a wire message type, codec,
  frame reader or writer, or a duplicate `:protocol` enum. It needs no device to run.
- A session test asserts `CastSessionMachine.start()` emits `WireFrame(MIN_SUPPORTED, HELLO)`, that
  every later frame carries `negotiatedVersion`, and that `ProtocolVersion.CURRENT` never appears as
  an outgoing frame version.
- An encoding test builds `HELLO` from unordered, duplicate-bearing codec sets and asserts the
  encoded list is ascending and deduplicated.
- A refusal test drives ids 14 to 36 and `SurfaceMode.BROWSER` through
  `BrowserWireRules.isForbiddenOnOrdinaryChannel` and asserts the phone constructs none of them.
- `MediaHandoff.mimeTypeOrNull` returns `null` at 256 bytes and refuses to truncate. No test claims
  agreement with C# or Rust at that boundary until the cap converges.
- `:castcore` is gated at 95% line and 85% branch by JaCoCo, with `allWarningsAsErrors` set. The
  emulator and physical-device handshake tests are written and have not been run.
- No document or interface string calls cast traffic private, confidential or encrypted.

## Revisit criteria

A superseding record needs evidence. Either the three languages agree on one `MEDIA_COMMAND` MIME
cap, at which point the phone's 255-byte constant becomes the shared one and the divergence leaves
the record; or the phone app acquires separate owners and a release cadence **and** a publishing
pipeline carrying the vectors and manifest as reviewed artefacts, with a cross-repository check that
fails on drift. A tidier repository and any unmeasured build-time claim are not triggers.

## References

- [Project constraints](../../../AGENTS.md)
- [Flint Mobile implementation slices](../README.md)
- [Wire protocol and golden vectors](../../PROTOCOL.md)
- [`WireCodec`](../../../protocol/src/main/kotlin/com/rextechnologies/flint/protocol/wire/WireCodec.kt)
- [`ProtocolVersion`](../../../protocol/src/main/kotlin/com/rextechnologies/flint/protocol/wire/WireMessages.kt)
- [`BrowserWireRules`](../../../protocol/src/main/kotlin/com/rextechnologies/flint/protocol/wire/BrowserWireMessages.kt)
- [`CastSessionMachine`](../../../castcore/src/main/kotlin/com/rextechnologies/flint/castcore/session/CastSessionMachine.kt)
- [`MediaHandoff`](../../../castcore/src/main/kotlin/com/rextechnologies/flint/castcore/media/MediaHandoff.kt)
- [Receiver plaintext-channel refusal](../../../receiver/src/main/kotlin/com/rextechnologies/flint/receiver/net/ReceiverServer.kt)
- [Golden enum manifest](../../../testdata/golden/browser-enums.txt)
- [Windows CI path filters](../../../.github/workflows/ci.yml)
- [Mobile CI workflow](../../../.github/workflows/mobile.yml)
