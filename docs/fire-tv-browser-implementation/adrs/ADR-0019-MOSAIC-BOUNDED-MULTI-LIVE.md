# ADR-0019 — mosaic layout with bounded multi-live renderers

- **Status:** Accepted
- **Implementation:** In progress
- **Date:** 2026-09-06
- **Scope:** Fire TV browser experiment
- **Deciders:** Flint engineering
- **Owners:** TV browser UX workstream
- **Related:** ADR-0007, ADR-0012, ADR-0013, ADR-0020, ADR-0021
- **Supersedes:** None (extends ADR-0012's live policy into a multi-pane glass layout; does not raise MAX_LIVE)
- **Superseded by:** ADR-0023

## Context

Viewers want several pages visible at once (a video grid). Tabs today keep at most two live
WebViews and put only one on glass ([ADR-0012](ADR-0012-USER-INITIATED-BOUNDED-TABS.md)). A card
switcher is not simultaneous playback. Raising live renderers without measured RAM is an OOM risk
on 1–2 GB sticks. One live WebView costs roughly 46–53 MB PSS on AFTMM
([docs/LATENCY_BUDGET.md](../../LATENCY_BUDGET.md)); two- and four-live mosaic figures remain
**targets until measured**.

## Decision

- Introduce **mosaic mode**: a user-initiated grid of up to **4 panes**.
- While mosaic is open, at most **2 panes are live** (same number as ADR-0012). Visible non-live
  panes show a Suspended card — never a fake playing player.
- Prefer **refuse** over silently freezing a pane that is in pane-local fullscreen.
- Adding panes is always user-initiated; page `window.open` remains banned (ADR-0006).
- Raising `MAX_LIVE` above 2 requires a superseding ADR with measured PSS and decode evidence.

## Consequences

### Positive

- Simultaneous two-up viewing without pretending four live players work.
- Eviction policy is testable in `MosaicRegistry` without hardware.

### Trade-offs

- A 2×2 grid will suspend at least two panes until Phase 4 evidence allows more live slots.
- Mosaic competes with mirror/player for exclusivity on the Activity (ADR-0007).

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Unlimited live panes | OOM on Stick-class devices. |
| Raise MAX_LIVE to 4 immediately | Two-live PSS still unmeasured; violates AGENTS.md honesty. |
| Host-side Chromium grid | Contradicts ADR-0001. |

## Invariants and validation

- `MosaicRegistryTest` keeps live count ≤ 2 and pane count ≤ 4.
- Capability probe exposes `maxLiveWebViews` / `maxPanes`; UI must not offer controls beyond the probe.
- Hardware PSS for 2/3/4 live WebViews recorded in LATENCY_BUDGET before any raise.

## Revisit criteria

Measured PSS plateau for N live WebViews on target SKUs, plus concurrent decode observations.

## References

- [ADR-0012](ADR-0012-USER-INITIATED-BOUNDED-TABS.md)
- [LATENCY_BUDGET.md](../../LATENCY_BUDGET.md)
- [AGENTS.md](../../../AGENTS.md)
