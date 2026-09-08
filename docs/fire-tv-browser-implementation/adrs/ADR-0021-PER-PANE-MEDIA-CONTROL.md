# ADR-0021 — per-pane media control (play / pause / mute)

- **Status:** Accepted
- **Implementation:** In progress
- **Date:** 2026-09-06
- **Scope:** Fire TV browser experiment
- **Deciders:** Flint engineering
- **Owners:** TV browser UX workstream
- **Related:** ADR-0014, ADR-0019
- **Supersedes:** None
- **Superseded by:** ADR-0023

## Context

System volume and mute are Fire OS–owned and affect the whole Stick. A mosaic needs per-pane mute
and play/pause without implying per-pane volume faders that do not exist.

## Decision

- **Play / pause** for a pane dispatches media/Select keys (or an equivalent driver call) **only**
  into that pane's WebView.
- **Mute / unmute** sets that pane's HTML5 media muted state (and persists `MosaicPane.muted`). It
  must not call `AudioManager` stream mute.
- **Volume** remains system-owned globally. Mosaic UI must not show per-pane volume sliders.
- Default policy when two live panes both have audio: only the **focused** pane starts unmuted;
  others prefer muted until the viewer unmutes (reduces ducking chaos).
- Site-dependent failure is honest: if a page ignores Select, say so rather than fake success.

## Consequences

### Positive

- Viewers can silence one stream without muting the TV.
- Matches AGENTS.md honesty for unavailable capabilities.

### Trade-offs

- DRM / custom players may ignore mute injection; capability is best-effort.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| System mute per pane | Impossible — one stream. |
| Injected page JS bridge | Violates ADR-0006 WebView security profile unless a reviewed, minimal path is added later. Prefer WebView media APIs / targeted key dispatch first. |

## Invariants and validation

- `MosaicInputModelTest` / `MosaicRegistryTest` cover mute flags and MediaPlayPause effects.
- Driver mute uses WebView APIs without logging page content.

## Revisit criteria

A reviewed, ADR-0006-compatible mute path if key dispatch proves insufficient on hardware.

## References

- [ADR-0006](ADR-0006-WEBVIEW-SECURITY-PROFILE.md)
- [ADR-0019](ADR-0019-MOSAIC-BOUNDED-MULTI-LIVE.md)
