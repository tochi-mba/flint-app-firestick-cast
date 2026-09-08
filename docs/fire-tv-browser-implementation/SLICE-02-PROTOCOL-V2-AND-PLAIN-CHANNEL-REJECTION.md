# Slice 02 — protocol v2 contract and plain-channel rejection

**Governing ADRs:** [ADR-0003](adrs/ADR-0003-SEPARATE-PINNED-TLS-BROWSER-SESSION.md), [ADR-0005](adrs/ADR-0005-PROTOCOL-V2-CANONICAL-CONTRACT.md), [ADR-0008](adrs/ADR-0008-PORTABLE-INPUT-AND-STALE-GUARDS.md), [ADR-0009](adrs/ADR-0009-OPT-IN-BOUNDED-JPEG-PREVIEW.md), and [ADR-0011](adrs/ADR-0011-TEST-EVIDENCE-AND-LATENCY-GOVERNANCE.md).

## Outcome

Every Flint implementation agrees byte-for-byte on the repaired existing protocol and the new
browser-v2 contract before any browser socket becomes live. A new host and old receiver still use
v1 for existing features. Browser messages cannot be encoded, accepted, relayed, or accidentally
interpreted on the existing plaintext CastSession/ReceiverServer route.

This is a contract slice. Its vertical product result is an accurate Web capability state:
“protocol too old” or “secure browser endpoint not installed,” never an unsafe partial feature.

## Entry criteria

- Slice 01 has a truthful browser capability state and device evidence record.
- The team has reviewed docs/PROTOCOL.md and the existing Rust/C#/Kotlin parity gap: C# and Kotlin
  know media IDs 10–13 while Rust currently does not.
- No code sends a browser URL, text, preview, dialog, or Browser surface selection over CastSession.

## Scope

### In scope

- Repair Rust support for existing media IDs 10–13 before adding any v2 shape.
- Raise current protocol version to 2 while preserving minimum supported version 1.
- Canonical browser-v2 models, codecs, bounds, validation, version negotiation, and committed
  golden-vector manifest in Rust, C#, and Kotlin.
- Explicit ordinary-channel rejection of every typed browser message and Browser surface attempt.
- Windows unavailable messaging for protocol/security absence.
- Protocol documentation correction, including current CONTROL direction ambiguity and the fact that
  authorization is not encryption.

### Explicitly out of scope

- TLS implementation, certificate material, listener sockets, BrowserSession, WebView, user text,
  preview encoding, or normal browser controls.
- A compatibility fallback that sends browser traffic over v1 or the plaintext channel.
- “Round-trip only” tests. An implementation agreeing with itself does not prove interoperation.

## Immutable contract

### Version rules

- Current is 2; minimum supported remains 1.
- Negotiation stores the actual mutually selected version in each session. An outgoing v1 session
  writes version 1; it must not write Current by habit.
- v2 with v2 may use browser types only on the future authenticated TLS BrowserSession.
- v2 with v1 preserves media/mirror behaviour, reports Web unavailable, and never selects
  SurfaceMode.Browser.
- No overlapping version range closes with the existing explicit unsupported-version outcome.
- Known malformed values fail closed; existing policy for unknown future opaque types remains
  explicitly documented and tested.

### Browser message registry

| ID | Type | Direction on secure session | Contract |
|---:|---|---|---|
| 14 | BROWSER_CAPABILITY | receiver to host | Availability/reason, secure endpoint, WebView/API diagnostics, preview support, bounded preview limits; emitted only after secure authentication. |
| 15 | BROWSER_COMMAND | host to receiver | epoch, monotonic commandId, typed Open/Navigate/Back/Forward/Reload/Stop/Close/SetPreview/ClearData; URL only where valid and at most 4 KiB. |
| 16 | BROWSER_INPUT | host to receiver | epoch, monotonic sequence, typed pointer/scroll/semantic key/UTF-8 text; pointer input carries the current preview navigation/frame reference and fixed normalized coordinates; no Windows virtual key codes. |
| 17 | BROWSER_STATE | receiver to host | epoch, monotonic revision, last accepted command/input IDs for non-secret correlation, safe navigation state, safe URL/title, progress 0–100, history flags, viewport, preview state, bounded safe error; no DOM/cookies/headers/form values/source. |
| 18 | BROWSER_PREVIEW | receiver to host | epoch, navigationId, monotonic frameId, dimensions, JPEG, bytes at most 768 KiB; newest-only semantics. |
| 19 | BROWSER_DIALOG | receiver to host | one expiring dialog ID, top-level origin, type, bounded message/default; observation only. |
| 20 | BROWSER_DIALOG_REPLY | host to receiver | active matching dialog ID, explicit accept/cancel, bounded prompt text; secure session only. |

The only semantic key values are Up, Down, Left, Right, Select, Back, Tab, Shift-Tab, Escape,
PageUp, PageDown, Home, End, and Refresh. Every enum/union has a test for unknown/disallowed
values. Do not grow this set by leaking platform scan codes into the protocol.

### Ordering rules

- New Open/Navigate owns a new epoch/navigation ID.
- Command IDs, input sequences, state revisions, and preview frame IDs are strictly monotonic in
  their defined scope.
- Old/duplicate/out-of-order data is rejected or ignored with a bounded observable counter; it
  never alters browser state.
- Host session owns allocation of command/input counters. A view-model is not allowed to guess one.
- State/frame consumers accept only current epoch/newest revision/frame. Later slices must dispose
  stale decoded images immediately.
- The bounded last-accepted command/input IDs make host-observed control timing credible without
  exposing input text or subtracting unsynchronized host/TV clocks.
- A pointer based on preview is accepted only when its navigation/frame reference is current; the
  receiver rejects it rather than acting on a stale visual.
- The future browser writer will prioritize reliable control/state over a single preview slot; the
  message contract must make that priority testable now.

## Existing-code hazards that this slice fixes

| Current seam | Hazard | Required correction |
|---|---|---|
| flint-engine wire model | Rust represents IDs 1–9 while C#/Kotlin have 1–13. | Restore 10–13 before issuing any v2 ID. |
| CastSession | It creates outgoing frames without retaining the negotiated version. | Persist/use negotiated version for every outgoing frame. |
| Kotlin FrameSender | It hard-codes CURRENT although handshake calculates a negotiated version. | Thread negotiated version to sender and test a v1 peer. |
| ReceiverServer dispatch | Unknown message types can be silently ignored. | Explicitly reject/record browser types on ordinary/plain route. |
| SurfaceMode exhaustiveness | Adding Browser forces C#/Kotlin UI/service switches to compile. | Keep Browser invalid on v1/plain route; add neutral exhaustive handling now. |
| Golden test setup | Rust/C# current corpus is incomplete and Kotlin does not consume canonical bytes. | Make Rust generator canonical, add manifest and all three consumers. |

## Planned files

| Area | Planned change |
|---|---|
| flint-engine/src/wire | Media 10–13 parity, v2 model/codec, strict validation, canonical vector generator/manifest. |
| src/Flint.Protocol | Matching ProtocolVersion, wire models/value objects, codec, negotiation helpers. |
| protocol/src/main/kotlin | Matching Kotlin wire models, codec, validation, negotiated-version types. |
| src/Flint.Session and tests/Flint.Session.Tests | Correct outgoing negotiated version handling without introducing browser transport. |
| receiver network/service tests | Existing ReceiverServer/CastSession route rejects Browser frame/surface attempts before dispatch. |
| tests/Flint.Protocol.Tests and protocol/src/test | Canonical corpus consumers, completeness, malformed/frame-bound, version and rejection tests. |
| testdata/golden | Deliberately generated/reviewed vectors and manifest; Rust remains canonical generator. |
| docs/PROTOCOL.md | v2 schema, TLS-only boundary, direction, bounds, ordering, compatibility and auth/encryption truth. |

## Test-first implementation order

| Step | Red test to add first | Minimum production change | Required green proof |
|---:|---|---|---|
| 1 | Rust/C#/Kotlin completeness tests show IDs 10–13 are missing in Rust corpus/model. | Restore v1 media model/codec parity in Rust only. | Existing media vectors parse/re-encode identically in all three languages. |
| 2 | Golden manifest tests enumerate every current v1 shape and then fail for missing v2 names. | Add a manifest schema and test harness before v2 codecs. | Missing/extra named case fails in Rust, C#, and Kotlin. |
| 3 | One golden vector per browser type/union case fails to decode in each language. | Add v2 model and strict codec symmetrically, one message family at a time. | Cross-language decode plus re-encode is byte-identical to the committed Rust vector. |
| 4 | Boundary/negative vectors fail expected rejection. | Enforce length, enum, boolean, UTF-8, trailing-byte, impossible-combination and v1/v2 guards. | Property/fuzz corpus has stable error classification and no allocation/size escape. |
| 5 | Negotiation tests show a v1 peer receives a v2 frame or a v1 frame is stamped v2. | Persist actual negotiated version and gate typed browser values. | v2–v2, v2–v1, v1–v2, no-overlap, and reconnect paths pass. |
| 6 | Plain-channel rejection tests show ReceiverServer/CastSession could reach browser dispatch. | Add explicit type/surface rejection before any browser coordinator boundary. | Every browser ID and SurfaceMode.Browser is rejected on ordinary listener, unauthenticated route, and v1. |
| 7 | App capability/view-model tests show an active Web action for v1/no secure endpoint. | Present exact unavailable/security prerequisite state. | Headless/UI snapshot tests prove no command can be emitted. |
| 8 | Documentation consistency tests/search checks identify stale protocol claims. | Update docs/PROTOCOL.md and code comments. | Review confirms no documentation calls the ordinary channel encrypted/private. |

### Mandatory vector cases

The manifest must name, and every language must consume, all of the following:

- Every existing v1 message including repaired media IDs 10–13, each optional field state,
  empty/non-empty collection, numeric boundary, multi-byte UTF-8, and forward-compatible opaque
  type case.
- Every browser ID 14–20; every command/input/dialog union variant; zero/one/max-size valid payload;
  a normal Unicode text payload; and every safe state/error/preview capability variant.
- Rejection frames for truncated envelope/body, declared body over 16 MiB, invalid magic/version,
  unsupported v1 typed browser frame, unknown enum/union, invalid boolean, malformed UTF-8,
  oversized URL/text/detail/JPEG, impossible field combination, trailing byte, stale/invalid
  sequence representation, and malformed nested length.
- Version negotiation vectors for v1-only, v2-only, overlap, no overlap, and v2 browser eligibility.

Use deterministic binary bytes and a human-readable manifest. Never regenerate them as an incidental
side effect of normal test execution.

## Parser, compatibility, and security proof

Add property/fuzz tests with deterministic seeds for the Rust, C#, and Kotlin decoders. Bound input
before allocation, assert no panic/uncaught exception, and verify the parser either produces the
same semantic value as the corpus or a classified rejection. Preserve minimized regressions in the
corpus.

Run mutation testing for validation branches. Meaningful mutations that must be killed include
removing a maximum length check, accepting an unknown enum, allowing a browser message on v1,
incrementing a negotiated version to Current, accepting trailing bytes, or forwarding a browser
frame to the ordinary listener.

No test may replace the cross-language corpus with generated-at-runtime test bytes. Golden evidence
must prove the three independently maintained codecs agree.

## Full-suite checkpoint

Focused development commands are expected to resemble:

~~~powershell
Set-Location flint-engine
cargo test wire
cargo test --test golden

Set-Location ..
dotnet test Flint.slnx --no-build --filter FullyQualifiedName~Wire
.\gradlew.bat --no-daemon :protocol:check --console plain
~~~

When a reviewed protocol change needs new canonical bytes:

~~~powershell
Set-Location flint-engine
cargo test --test golden -- --ignored regenerate
~~~

Inspect the vector/manifest diff, then rerun all consumers. End the slice with:

~~~powershell
Set-Location ..
.\scripts\build.ps1
.\gradlew.bat --no-daemon :protocol:check :receiver:testDebugUnitTest :receiver:lintDebug --console plain
~~~

## Latency/resource checkpoint

Protocol correctness is the performance outcome in this slice:

- Every declared frame is length-checked before buffer allocation. Test the maximum boundary and
  one-byte-over failure for each bounded field.
- Record encode/decode allocations and throughput only as local microbenchmark observations, not
  user latency. Benchmarks need fixed payload classes (command, state, max preview) and a warm-up.
- Reject/ignore stale values without replaying WebView work. Counters are bounded and aggregated;
  they may not become an unbounded diagnostic log.
- A browser preview type is contractual only. Slice 02 cannot create a preview queue or dedicate
  memory to decoded images.

## Exit evidence

| Evidence | Required result |
|---|---|
| Existing parity | Rust has byte-identical support for media IDs 10–13; all existing media/mirror regression tests pass. |
| Browser contract | IDs 14–20, fields, limits, ordering, and version rules have reviewed canonical vectors. |
| Cross-language proof | Rust, C#, and Kotlin decode and re-encode the same bytes; manifest completeness fails correctly. |
| Negative proof | Malformed, oversized, unknown, stale, v1, ordinary-channel, and unauthenticated cases are rejected/ignored exactly as documented. |
| Coverage | New owned protocol branches reach 100% line/branch coverage; parser/trust-critical checks have fuzz and mutation proof. |
| Existing feature safety | A v1 peer still performs existing supported media/mirror work; Web is accurately unavailable. |
| Documentation | PROTOCOL and UI wording describe a future TLS-only browser boundary and do not call the legacy route private/encrypted. |

## Rollback and stop conditions

The protocol code remains backward compatible for v1 existing functionality. If any vector,
negotiation, or ordinary-channel rejection test fails, revert/disable only the v2 browser
capability; do not ship an incompatible partial wire format.

Stop rather than continue if the three language implementations cannot agree on one canonical byte
corpus, if ordinary control can dispatch a browser message, or if a proposed field needs unbounded
data. Resolve the contract before building TLS or WebView code.
