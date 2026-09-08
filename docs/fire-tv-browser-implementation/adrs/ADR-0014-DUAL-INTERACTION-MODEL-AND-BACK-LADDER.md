# ADR-0014 — dual D-pad interaction model and the Back ladder

- **Status:** Accepted
- **Implementation:** Implemented (software); feel not yet validated on hardware
- **Date:** 2026-09-06
- **Scope:** Fire TV browser experiment
- **Deciders:** Flint engineering
- **Owners:** TV browser UX workstream
- **Related:** ADR-0006, ADR-0007, ADR-0008, ADR-0013
- **Supersedes:** None
- **Superseded by:** None

## Context

Two questions decide whether a television browser is usable: what the D-pad means inside a page, and
what Back does. Neither had an answer.

The D-pad reached the `WebView` unmediated. Chromium's arrow handling is not spatial navigation, so
on most sites the arrows scrolled and nothing could be selected.

Back was worse. It fell through to the media key handler, which treats any non-idle surface as
something to tear down — so one press on the second page of a site ended the browsing session.

The obvious fix, "make the arrows move focus", fails on the sites people actually open: canvas UIs,
maps, video players, and anything with a broken tab order have no usable focus ring. The obvious
alternative, a cursor, is what Amazon's own Silk browser does — and its weakness is well known: one
constant speed is either too slow to cross a page or too fast to land on a link.

ADR-0006 forbids injected JavaScript, so neither mode may be implemented by reaching into the page.
Both are therefore built from input events alone: `MotionEvent` for the cursor, `KeyEvent` for focus.

## Decision

- Two explicit modes, **Cursor** (default) and **Focus**, switched by **holding Select** and
  announced by name when it changes. A remote that silently changes meaning is how a browser feels
  broken.
- **Cursor mode**: a tap nudges a fixed 24 px for aiming; a hold accelerates from 6 to 34 px per
  frame over 700 ms on an ease-out curve. Pressing on at an edge **scrolls the page** rather than
  pinning the pointer, which is what makes a page taller than the screen reachable at all.
- **Focus mode**: left/right are Tab and Shift-Tab against the page's own focus order; up/down
  scroll; Select is Enter.
- Media keys go to the page, so a remote's play/pause acts on an ordinary web video.
- Home, volume and power remain **system-owned** and are never consumed.
- **Back follows one ordered ladder**, each rung undoing the smallest thing just done: notice →
  dialog → fullscreen → overlay → leave-confirm → chrome → history → spare tab → confirm leave.
  Back never ends a session without asking.
- Long-pressing Back opens the tab switcher, which is the fastest route between two sites a remote
  has.
- The whole decision table is a pure function of a flat state, modelled on the existing
  `mirrorKeyOutcome`, which is this codebase's proven shape for remote handling.

## Consequences

### Positive

- Every site is operable, including those with no focus order at all.
- Aiming a link and crossing a page are both possible, which one speed cannot do.
- Back is predictable and cannot lose a session to a stray press.
- The full table is exhaustively tested rather than discovered on a television.
- No JavaScript is injected, so ADR-0006 is untouched.

### Trade-offs

- Two modes is a concept to learn, mitigated by the mode being named aloud when it changes.
- Cursor mode cannot snap to links, because knowing where links are would require reaching into the
  page.
- The acceleration constants are a judgement pinned by tests, and will need hardware validation.
- The ladder is nine rungs; every new overlay has to be placed in it deliberately.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Focus-only navigation | Fails on canvas UIs, maps and any site with a poor tab order — a large share of the web. |
| Cursor-only, one constant speed | Silk's weakness: too slow to cross a page or too fast to hit a link. |
| Automatic mode switching per page | The remote would mean different things on each site, and change under the viewer as the DOM loads. |
| Inject JavaScript to find focusable elements | Forbidden by ADR-0006, and a bridge into every page is a far larger risk than an imperfect cursor. |
| Leave Back to the media handler | The defect this ADR exists to fix. |
| Exit the browser on Back at the last page | Loses a session to one press on a remote where Back sits beside the D-pad. |

## Invariants and validation

- `BrowserTvInputModelTest` covers both modes, the chrome and overlay owners of the D-pad, media
  keys, system-owned keys, long-press behaviour, and **every rung of the Back ladder in order**.
- `BrowserCursorEngineTest` proves monotonic acceleration, a bounded top speed, that the cursor
  never leaves the viewport under any input sequence, that edges scroll only in the direction being
  pushed, and that release restores tap precision.
- An unrecognised key returns `PassThrough`; the surface never swallows a key it has no meaning for.

## Revisit criteria

Hardware evidence that the acceleration curve is wrong for a real remote's repeat rate, or a
measured finding that one mode is never chosen in practice. Adding a rung to the Back ladder
requires updating the ordered test, not appending to the end of the function.

## References

- [Master browser plan](../../FIRE_TV_BROWSER_PLAN.md)
- [Project constraints](../../../AGENTS.md)
- [Silk's navigation modes on Fire TV](https://www.amazon.com/gp/help/customer/display.html?nodeId=TZ6CJ7nVQJW26yiItl)
- [Fire TV design and UX guidelines](https://developer.amazon.com/docs/fire-tv/design-and-user-experience-guidelines.html)
