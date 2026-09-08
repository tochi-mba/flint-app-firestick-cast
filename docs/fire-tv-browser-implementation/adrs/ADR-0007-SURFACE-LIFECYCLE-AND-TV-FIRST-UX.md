# ADR-0007 — surface lifecycle and TV-first UX

- **Status:** Accepted
- **Implementation:** Planned
- **Date:** 2026-09-04
- **Deciders:** Flint engineering
- **Owners:** Slices 04–09
- **Related:** ADR-0001, ADR-0006, ADR-0010

## Context

A WebView has Android UI-thread and lifecycle ownership requirements. Flint's ReceiverService also
owns network/session/media state, while receiver surfaces already include player and mirror. Letting
the service retain a WebView/Activity, or allowing multiple surfaces to compete, risks leaks, blank
screens, stale input, and hard-to-recover Fire TV UI.

Fire TV is D-pad-first and has overscan constraints. A Windows remote/preview cannot replace visible
TV focus and predictable Back behaviour.

## Decision

- ReceiverActivity and ReceiverBrowserSurface own AndroidView/WebView creation, attachment,
  visibility, full KeyEvent delivery, detach, and destroy on the UI thread.
- ReceiverService/BrowserCoordinator own only immutable browser/session/surface state and port
  effects; they never retain a WebView, Activity, or Context reference.
- Browser is mutually exclusive with mirror and player. Every surface transition runs the ordered
  cancel-dialog/stop-load/detach/destroy/dispose/final-state sequence before a new owner activates.
- TV interaction precedence is native dialog, focused WebView element, page history Back, local
  browser close/idle. Home/microphone/system volume remain system-owned.
- All local chrome/dialogs use visible focus and 5% overscan-safe layout. Windows remote is additive.
- On authenticated controller disconnect, remote input stops immediately while the TV shows a local
  disconnected overlay and allows a bounded 30-second reconnect grace. Expiry performs clean close;
  reconnect reauthenticates and cannot replay pending input/dialog/data actions.
- API-25 claims only documented lifecycle/driver-error recovery. API-26 renderer callbacks are
  feature-gated and separately evidenced.

## Consequences

### Positive

- Browser resource ownership and cleanup are testable with fakes/recording drivers.
- Existing media/mirror lifecycles have a clear exclusion boundary.
- TV use remains viable without a Windows mouse/preview.
- Disconnect/restart produces a visible safe state instead of a hidden orphaned WebView.

### Trade-offs

- Cross-cutting service/activity/UI state changes are required.
- Transition tests are combinatorial and must use fake clocks/ports rather than sleeps.
- A short controller reconnect grace is policy complexity but avoids immediate disruptive close.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Keep WebView in ReceiverService | Violates Android lifecycle ownership and risks retaining Activity/UI resources. |
| Allow browser over mirror/player simultaneously | Creates ambiguous rendering/input/resource ownership. |
| Send only keyCode from Activity | Loses event semantics and prevents browser-first/fallthrough routing. |
| Make Windows preview the primary focus/navigation model | Breaks D-pad-first Fire TV UX and makes browser depend on feedback transport. |
| Keep browser permanently active after host disconnect | Leaves unbounded controller-less state and complicates safety/recovery. |

## Invariants and validation

- Unit/property tests cover every surface/lifecycle/disconnect transition and stale callback.
- Recording BrowserPort/Robolectric tests prove exact detach/destroy order and no service-held UI
  reference.
- Compose snapshots plus UiAutomator real-TV evidence verify focus, overscan, Back, Home,
  pause/resume, dialog, error, and close paths.
- Receiver startup/network-restart/shutdown tests include browser listener/session/buffer cleanup.
- Existing mirror/player tests run unchanged after Browser surface exhaustiveness changes.

## Revisit criteria

Changing ownership, allowing concurrent surfaces, or changing the disconnect grace requires a new
ADR with Android lifecycle proof, D-pad/recovery evidence, and resource/latency comparison. A UI
shortcut does not justify violating service/UI separation.

## References

- [Slice 06 — lifecycle and surface safety](../SLICE-06-LIFECYCLE-DIALOGS-DATA-AND-SURFACE-SAFETY.md)
- [Fire TV design guidance](https://developer.amazon.com/docs/fire-tv/design-and-user-experience-guidelines.html)
