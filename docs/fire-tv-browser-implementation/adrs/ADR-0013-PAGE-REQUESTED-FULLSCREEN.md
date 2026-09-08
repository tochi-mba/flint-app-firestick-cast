# ADR-0013 — page-requested fullscreen

- **Status:** Accepted
- **Implementation:** Implemented (software); not yet exercised on hardware
- **Date:** 2026-09-06
- **Scope:** Fire TV browser experiment
- **Deciders:** Flint engineering
- **Owners:** TV browser UX workstream
- **Related:** ADR-0006, ADR-0007, ADR-0014
- **Supersedes:** The "no full-screen HTML5 video" non-goal in [the master plan](../../FIRE_TV_BROWSER_PLAN.md)
- **Superseded by:** None

## Context

A `WebView` asks for fullscreen by handing the embedder a view plus a callback to invoke when the
embedder is done with it. Nothing implemented that pair, and the `AndroidView` factory returned the
bare `WebView` — so there was no container a fullscreen view could be added to even if something
had listened.

The visible result was the loudest defect in the feature: on a television, whose entire purpose is
watching things, every fullscreen button on every video site appeared to work and then did nothing.

The v1 plan listed this as a non-goal on the grounds that WebView video on Fire TV is unreliable.
That reasoning covers *whether a given site plays*, which remains an honest unknown. It does not
justify ignoring the request, which is a defect rather than a scope decision.

## Decision

- `onShowCustomView` / `onHideCustomView` are implemented against a real container. The surface
  hands Compose a `BrowserTabHost` holding the page and a fullscreen layer above it.
- Entering runs in a fixed order: attach the view, hide the system bars, hold the screen awake.
  Leaving runs the exact reverse, so the page never reappears under a still-immersive window.
- The screen is held awake while fullscreen. A playing video is not user activity to Fire OS, so
  without this the display dims part-way through anything longer than the timeout.
- The page beneath is made invisible while fullscreen. On a stick, compositing a page nobody can see
  is a second live layer for nothing.
- **One fullscreen at a time.** A second request is refused and answered immediately, so the page
  can restore its inline player. Accepting it would orphan the first view along with a callback
  nobody can invoke — a permanently black screen with no way back.
- The page's callback is invoked **at most once**, whichever of Back, `onHideCustomView` or teardown
  arrives first. Some WebView builds treat a second invocation as a crash.
- Surface teardown **abandons** rather than exits: the renderer that owns the callback is going away,
  but the system bars and the wake hold belong to this window and still have to come back.
- Back leaves fullscreen before anything else it could mean (ADR-0014's ladder).
- `mediaPlaybackRequiresUserGesture` stays `true`. Fullscreen is not autoplay.

## Consequences

### Positive

- The fullscreen control on a video site does what it says.
- A film does not dim halfway through.
- The ordering rules are a tested policy rather than something discovered on a device.
- No claim is made about which sites play; that stays an observation matrix.

### Trade-offs

- The receiver now holds a wake lock during fullscreen, which it did not before.
- A page that enters fullscreen and loses its view without a callback needs the teardown path to
  restore the window, which is one more state to keep correct.
- Fullscreen still cannot make a site work that WebView cannot play.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Leave fullscreen unimplemented | A control that visibly does nothing is a defect, not a scope boundary. |
| Accept a second request and stack views | Orphans the first callback; the screen goes black with no route out. |
| Keep the page composited behind the video | A second live layer on a device that cannot spare one. |
| Skip the wake lock | The display dims mid-film, which reads as the browser crashing. |
| Force fullscreen for any playing video | Takes a decision from the viewer that the page is entitled to make. |

## Invariants and validation

- `BrowserFullscreenControllerTest` asserts the exact enter and exit call order, refusal of a second
  request, single invocation of the page callback, the no-op exit, and that abandon restores the
  window without telling the page.
- The policy is generic over the view type, so the ordering is proven with no device present.
- Hardware validation — entering and leaving fullscreen on a real video site — remains an open
  release gate and is not claimed here.

## Revisit criteria

Evidence that a Fire OS build needs a different enter/exit ordering, or a measured case where
holding the screen awake is harmful. A site that does not play is not a reason to revisit this
decision; it is an entry in the compatibility matrix.

## References

- [Master browser plan](../../FIRE_TV_BROWSER_PLAN.md)
- [Project constraints](../../../AGENTS.md)
- [Fire TV FAQ on WebView media](https://developer.amazon.com/docs/fire-tv/faq-general.html)
