# ADR-0012 — user-initiated, memory-bounded tabs on the receiver

- **Status:** Accepted
- **Implementation:** Implemented (software); RAM budget not yet measured on hardware
- **Date:** 2026-09-06
- **Scope:** Fire TV browser experiment
- **Deciders:** Flint engineering
- **Owners:** TV browser UX workstream
- **Related:** ADR-0006, ADR-0007, ADR-0017
- **Supersedes:** The "No tabs" non-goal in [the master plan](../../FIRE_TV_BROWSER_PLAN.md)
- **Superseded by:** None

## Context

The v1 plan listed tabs as an explicit non-goal, and the receiver was built accordingly: one
`WebView`, one page state, one epoch, and a command reducer that answers a second `OPEN` with
`SURFACE_BUSY`.

That was defensible while the television was a remote display for a desktop. It is not defensible
for a browser someone uses from a sofa. Comparing two pages, keeping a video open while looking
something up, or returning to a search result after following a link are ordinary things, and
without tabs each of them costs the page you were on.

The constraint that shapes the answer is memory. A Fire TV stick has one to two gigabytes for the
whole system. Eight live `WebView`s is not a tight budget, it is an out-of-memory kill — and the
renderer that dies takes the page with it.

ADR-0006 also bans popups. That ban was written against page-initiated `window.open`, which is a
site taking a decision away from the viewer. A tab the viewer asks for is the opposite.

## Decision

- Tabs are **user-initiated only**. `setSupportMultipleWindows(false)` and
  `javaScriptCanOpenWindowsAutomatically = false` stay exactly as ADR-0006 requires, and
  `onCreateWindow` continues to refuse — now with a visible reason rather than silence.
- At most **8 tabs**, and at most **2 live renderers**: the foreground tab plus the most recently
  used one, which is what makes flipping between two sites instant.
- A tab pushed out of the live set is **frozen**, not closed: its navigation state is saved and its
  renderer released. Restoring rebuilds from that state.
- Saved state is capped at **32 KiB per tab**. Android's binder ceiling is a megabyte across the
  process and a long history serialises to more than people expect. A tab whose state will not fit
  degrades to reloading its address and says so, rather than restoring a wrong page.
- Tab identifiers are monotonic and **never reused**, so a late effect cannot land on a different
  page than the one it meant.
- Ordering and eviction live in a pure `BrowserTabRegistry` that emits effects; the Android
  `BrowserTabHost` only carries them out.

## Consequences

### Positive

- The television browser supports the workflow people actually have, without a desktop.
- The memory bound is a stated policy with a test, not an emergent property of how many pages
  someone happened to open.
- Freeze/restore ordering is provable without hardware, which is where renderer bugs are otherwise
  found last.
- ADR-0006's popup ban is unchanged and now produces user-visible feedback.

### Trade-offs

- Restoring a frozen tab is not instant, and the card says "Suspended" rather than hiding it.
- A tab whose state exceeds the cap loses scroll position and form contents on restore.
- Command, state and input reducers gain a tab dimension, which is a wider surface to keep correct.
- Until protocol ids 21–22 are negotiated, tabs are a television-local concept and the host sees
  only the active page.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Keep the single-page model | Leaves the television browser unable to do what browsing is for. |
| Unlimited live tabs | Out-of-memory kills on a 1 GB stick; the failure is a dead renderer, not a slow one. |
| Allow page-initiated windows to create tabs | Hands the decision to the site, which is exactly what ADR-0006 refuses. |
| Freeze every background tab, keeping only one live | Makes switching between two sites cost a reload every time, which is the commonest switch there is. |
| Persist frozen state to disk | Browsing state on disk is a privacy commitment this feature has not made; ADR-0010 keeps it memory-only. |

## Invariants and validation

- `BrowserTabRegistryTest` proves the 8-tab cap, the never-reused ids, close-successor selection,
  and — across a long mixed open/select/close sequence — that the live count never exceeds two.
- The refusal at the cap is an effect (`TabEffect.Refused`), so a full strip cannot fail silently.
- `BrowserTabHost` detaches before destroying; destroying an attached `WebView` leaves a dead child
  painting black over the page.
- Exceeding the state cap raises `onStateTooLarge`, which the surface turns into a visible notice.

## Revisit criteria

Measured RAM on target hardware that shows two live renderers is unsafe (reduce to one) or that
more is comfortably affordable (raise the cap). A convenience argument is not sufficient, and
neither is an unmeasured claim that a newer stick has more memory.

## References

- [Master browser plan](../../FIRE_TV_BROWSER_PLAN.md)
- [Implementation slices](../README.md)
- [Project constraints](../../../AGENTS.md)
- [Managing WebView objects](https://developer.android.com/develop/ui/views/layout/webapps/managing-webview)
