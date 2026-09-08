# ADR-0020 — pane-local fullscreen in mosaic

- **Status:** Accepted
- **Implementation:** In progress
- **Date:** 2026-09-06
- **Scope:** Fire TV browser experiment
- **Deciders:** Flint engineering
- **Owners:** TV browser UX workstream
- **Related:** ADR-0013, ADR-0014, ADR-0019
- **Supersedes:** None for single-page browser (ADR-0013 remains for non-mosaic)
- **Superseded by:** ADR-0023

## Context

ADR-0013 puts HTML5 custom views in an Activity-wide fullscreen layer and hides the page. In mosaic
that would steal the whole glass from other panes. Viewers expect a video's fullscreen control to
fill **its pane**.

## Decision

- In mosaic, each pane owns a `PaneFullscreenController`. Custom views attach inside that pane's
  bounds.
- Other panes stay visible (subject to live/decode limits).
- Activity immersive mode is **off** by default for pane fullscreen; optional later "theater mode"
  may immersive only when every other pane is suspended.
- One fullscreen **per pane**; a second enter in the same pane is refused.
- Back ladder gains mosaic rungs: exit pane fullscreen → leave pane page focus → close mosaic /
  return to single browser, then the existing ADR-0014 rungs.

## Consequences

### Positive

- Fullscreen matches the multi-window mental model.
- Ordering remains unit-tested without a Stick.

### Trade-offs

- Two expanded video surfaces may fail on Stick decoders; capability probe may force "only one pane
  may expand" with a visible notice.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Keep Activity-wide fullscreen in mosaic | Defeats the grid. |
| Allow stacked fullscreen in one pane | Orphans callbacks (ADR-0013). |

## Invariants and validation

- `PaneFullscreenControllerTest` proves enter/refuse/exit/abandon ordering.
- UiAutomator asserts custom view bounds stay inside the focused pane when hardware is available.

## Revisit criteria

Hardware proof that Activity immersive theater mode is required for reliable playback.

## References

- [ADR-0013](ADR-0013-PAGE-REQUESTED-FULLSCREEN.md)
- [ADR-0019](ADR-0019-MOSAIC-BOUNDED-MULTI-LIVE.md)
