# ADR-0011 — test evidence and latency governance

- **Status:** Accepted
- **Implementation:** Planned
- **Date:** 2026-09-04
- **Deciders:** Flint engineering
- **Owners:** Slices 01–09
- **Related:** All browser ADRs and AGENTS.md

## Context

The browser crosses Rust, C#, Kotlin, Android platform glue, WebView, Avalonia, TLS/networking,
and constrained Fire TV hardware. “It works on my TV” and line coverage alone do not prove
interoperability, security, UX, or latency. The repository's current build script runs Rust and .NET
checks but not all Gradle protocol/receiver coverage paths, and its existing Coverlet reports do not
by themselves enforce a browser-specific full-coverage standard.

Latency is especially easy to overstate: Windows and TV monotonic clocks are not synchronized, a
preview cadence has inherent delay, and averages conceal stalls. The project constraints forbid
unmeasured latency claims and hardware-dependent default tests.

## Decision

Every implementation slice follows a test-first and evidence-first contract:

- Write a named failing test for an owned behaviour/security invariant before production code; make
  it green with the smallest change; refactor only with focused/module tests green; then run the
  complete hardware-free gate before advancing.
- New Flint-owned pure protocol, URL/trust/input/preview policy, reducer, sequencing, geometry,
  session, and view-model decision code must achieve 100% line and branch coverage. This is scoped
  to new owned code, not an invented claim of Chromium/Android framework coverage.
- Parsers/orderings/security boundaries additionally require deterministic property/fuzz corpora and
  meaningful mutation testing. Cross-language wire changes require reviewed Rust-canonical golden
  vectors and decode/re-encode proof in Rust/C#/Kotlin.
- Platform/WebView behaviour requires deterministic WebViewAssetLoader/Espresso-Web fixtures,
  Robolectric where faithful, snapshot/Headless UI proof, and opt-in real Fire TV evidence for
  provider/API-specific branches. Hardware tests skip cleanly by default.
- The full browser-quality gate includes Rust format/Clippy/test/coverage/mutation as configured;
  .NET build/test/scoped Coverlet threshold; Gradle protocol check, receiver unit/lint/coverage
  checks; vectors; fuzz/static policy checks; UI/accessibility/snapshots; docs/site link checks; and
  loopback TLS/chaos tests. CI must invoke it explicitly.
- Golden vectors and snapshots are generated only through deliberate commands, reviewed as diffs,
  and never auto-approved by a normal test run.
- Measurements use local monotonic spans and host-observed command/state correlation IDs. Physical
  end-to-end input uses documented high-frame-rate camera evidence rather than cross-device clock
  subtraction.
- docs/LATENCY_BUDGET.md distinguishes target, measured, comparable, non-comparable, and
  unavailable. It records p50/p95/p99/max, sample/warm-up count, raw redacted evidence, device/
  provider/host/network/thermal context, queue depths, drops, allocations/GC, memory, CPU, thermal,
  threads, and FDs where applicable.
- Comparable regression thresholds are derived from observed baseline/noise and fail builds/release
  evidence on control/state starvation, queue bound breach, resource creep, stale action acceptance,
  or threshold breach. They are never marketing claims.

## Consequences

### Positive

- Quality claims map to executable proof rather than aspirational wording.
- Three-language protocol compatibility and security boundaries are reviewed at the byte/behaviour
  level.
- Target hardware facts can invalidate a design before it ships.
- Optimization has a disciplined order: remove stale/backlogged work, reduce rate/resolution,
  reduce copies/allocations, then reopen design only with evidence.
- Failures become permanent regression tests rather than folklore.

### Trade-offs

- The feature requires coverage/tooling/CI work before release and cannot hide it behind existing
  generic test commands.
- Hardware runs take deliberate time and need explicit authorization, target selection, and
  evidence storage.
- Full owned-code coverage may require isolating platform adapters behind testable ports.
- A non-comparable measurement is an honest incomplete result, not a green release gate.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Treat existing build script as full suite | It omits Gradle protocol/receiver coverage paths needed by the feature. |
| Require a single global 100% repository metric immediately | Misrepresents legacy/platform/generated code and encourages meaningless exclusions. |
| Unit tests only | Cannot prove WebView, TLS interop, D-pad, provider/resource, or cross-language behaviour. |
| Hardware tests in every CI run | Makes default builds environment-dependent and violates clean skip requirement. |
| Average latency only | Hides stalls and queue starvation. |
| Derive one-way latency from Windows/TV timestamps | Their monotonic clocks are unsynchronized; result would be false precision. |
| Auto-update snapshots/vectors | Removes deliberate review of visual/wire contract changes. |

## Invariants and validation

- Each slice exit table is a release gate, not an optional checklist; Slice 09 aggregates the final
  evidence record.
- CI validates its own gates with deliberately broken test fixtures/coverage branches where
  practical, proving checks cannot be silently skipped.
- All new unit/domain branches have published scoped coverage reports and documented narrow
  exclusions; mutation/fuzz reports identify corpus/seed/version.
- Hardware scripts require selected serial and acknowledgement, print device-affecting actions, and
  never silently install/replace/remove receiver software.
- Packet/log/snapshot inspection confirms redaction of URL/text/cookies/page/pixels/pairing/
  private-key data.
- Benchmark/soak data is preserved as redacted raw evidence and summarized honestly in the latency
  document; target values never replace measurements.
- Every production defect observed during a physical run gets a failing regression test before its
  fix is considered complete.

## Revisit criteria

Changing coverage scope, removing physical evidence, lowering a comparable regression threshold, or
changing measurement methodology requires an ADR amendment/superseding decision with an explanation
of how the new rule retains or improves truthfulness and failure detection.

## References

- [Slice 01 — feasibility and quality gate](../SLICE-01-FEASIBILITY-AND-QUALITY-GATE.md)
- [Slice 09 — hardening and release evidence](../SLICE-09-HARDENING-RELEASE-EVIDENCE-AND-DOCUMENTATION.md)
- [Project constraints](../../../AGENTS.md)
- [Latency budget](../../LATENCY_BUDGET.md)
