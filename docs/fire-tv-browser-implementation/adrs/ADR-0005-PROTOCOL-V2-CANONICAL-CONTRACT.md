# ADR-0005 — protocol v2 canonical contract

- **Status:** Accepted
- **Implementation:** Planned
- **Date:** 2026-09-04
- **Deciders:** Flint engineering
- **Owners:** Slices 02–09
- **Related:** ADR-0003, ADR-0008, ADR-0009, ADR-0011

## Context

Flint has independent Rust, C#, and Kotlin protocol implementations. They already differ: C#/Kotlin
recognize media IDs 10–13 while Rust has earlier parity. A browser feature cannot safely extend a
drifting protocol. Generic CONTROL semantics are too ambiguous for browser command/input/state,
and a round-trip test only proves one implementation agrees with itself.

Browser feedback also needs ordering/correlation rules to avoid stale state, preview, and input
acting on an obsolete navigation.

## Decision

Repair existing media parity first, then adopt protocol version 2 for browser contracts:

- Current protocol version becomes 2; minimum supported remains 1.
- Actual negotiated version is stored/used for every outgoing frame. A v1 peer continues existing
  media/mirror functions and sees Web unavailable.
- Browser types use stable IDs 14–20: capability, command, input, state, preview, dialog, and
  dialog reply. They are never overloaded onto generic CONTROL/media/caption fields.
- Browser command/input/state include epoch and monotonic identifiers. BrowserState carries bounded
  last-accepted command/input IDs for safe host correlation. Pointer input carries current
  preview-navigation/frame reference and fixed normalized coordinates.
- URL/text/detail/preview field sizes and every enum/union/boolean/UTF-8 shape are explicitly
  bounded/validated. No unknown or malformed typed browser value gets “best effort” handling.
- Browser types and SurfaceMode.Browser are invalid on v1 and rejected on the ordinary/plain
  listener before any browser dispatch.
- Rust remains the deliberate canonical golden-vector generator. C# and Kotlin consume the committed
  binary corpus, decode it, and re-encode identical bytes; a manifest proves completeness.

## Consequences

### Positive

- The three implementations have a concrete byte-level interoperability contract.
- v1 remains a safe compatibility mode rather than accidentally emitting Current/v2 frames.
- Stale state/frame/input handling is testable before WebView/UI work begins.
- Protocol review surface is explicit: vectors and manifest diff, not implicit serializer behaviour.

### Trade-offs

- Every schema refinement requires a deliberate three-language change and vector review.
- Session implementations must be corrected to use negotiated version rather than a global Current.
- Unsupported peers get a neutral unavailable Web experience rather than partial capability.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Extend generic CONTROL message | Ambiguous direction/semantics and risks plaintext browser dispatch. |
| Versionless optional browser fields | Makes unsupported/malformed behaviour ambiguous and impossible to gate cleanly. |
| Send browser types on v1 when both apps “know” them | Breaks version contract and old-peer safety. |
| Per-language round-trip tests only | Does not prove byte agreement across implementations. |
| Generate vectors during every normal test | Lets routine test execution silently rewrite a reviewable wire contract. |

## Invariants and validation

- Vectors cover IDs 1–20, optional/collection/numeric/Unicode boundaries, every browser union,
  version negotiation, opaque compatibility where allowed, and malformed rejection cases.
- Decoder fuzz/property/mutation tests kill missing bound/enum/version/trailing-byte checks.
- v1/v2/no-overlap and sender-version tests include CastSession and Kotlin sender paths.
- Plain/unauthenticated/v1 browser frames and Browser surface attempts are rejected or safely
  unavailable, never ignored into activation.
- Any protocol change updates docs/PROTOCOL.md, vectors, manifest, and all three language suites.

## Revisit criteria

A future protocol version may supersede this record only with an explicit migration/compatibility
plan, full canonical corpus, bounded data model, authenticated transport routing, and all-language
proof. Convenience fields or host-platform raw input cannot bypass this ADR.

## References

- [Slice 02 — v2 contract](../SLICE-02-PROTOCOL-V2-AND-PLAIN-CHANNEL-REJECTION.md)
- [Wire protocol documentation](../../PROTOCOL.md)
