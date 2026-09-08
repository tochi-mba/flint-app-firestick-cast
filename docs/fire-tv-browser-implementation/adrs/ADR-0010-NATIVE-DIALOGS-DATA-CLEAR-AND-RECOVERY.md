# ADR-0010 — native dialogs, data clearing, and bounded recovery

- **Status:** Accepted
- **Implementation:** Planned
- **Date:** 2026-09-04
- **Deciders:** Flint engineering
- **Owners:** Slices 06 and 09
- **Related:** ADR-0003, ADR-0006, ADR-0007, ADR-0008

## Context

Web pages may request alerts, confirms, prompts, storage deletion, or lifecycle-sensitive activity
changes. Letting page dialogs silently use default platform behaviour can create inaccessible,
unbounded, or host-invisible states on Fire TV. Reusing a page dialog as consent for SSL errors,
permissions, uploads, popups, or external intents would turn an informational browser prompt into an
unsafe privilege escalation route.

Browser data clearing is destructive and its scope is easy to overstate. Controller/network loss,
surface transitions, and WebView/provider errors also require an explicit bounded recovery policy so
the receiver never retains a hidden browser or acts on old remote commands.

## Decision

- Render JavaScript alert/confirm/prompt as one TV-local native dialog at a time, with a bounded
  top-level origin, bounded message/default, visible D-pad focus, Cancel as safe default, and an
  injected monotonic expiry.
- The host may observe/reply only through current authenticated TLS dialog messages; it does not
  need direct page access. TV and host race atomically: the first valid current response wins;
  stale/duplicate/expired replies are ignored. Prompt text never enters telemetry/history.
- Browser dialogs never grant web permission, certificate exception, popup, file chooser/download,
  external intent, geolocation/media capture, or unsafe URL access. Those remain denied by
  ADR-0006.
- Clear-data requires a secure host request **and** a visible receiver-local confirmation. It clears
  only the Flint receiver WebView stores documented as available: cookies, DOM storage, HTTP cache,
  history, and form/autofill data. It never clears Android Keystore identity, Windows trust pins,
  Fire TV account data, other-app data, or system browser/DRM data.
- Data clear is asynchronous, cancellable, non-replayable across session/epoch change, and results
  in a fresh safe browser state. It never silently reloads an old page.
- Browser/media/mirror are exclusive. On close, error, surface change, grace expiry, activity
  destruction, or service shutdown, run the exact cancellation/stop-load/detach/destroy/dispose/
  final-state sequence.
- On authenticated host loss, stop remote input immediately and allow a named bounded 30-second
  local-TV grace interval. A reconnect must pin/authenticate again and cannot replay pending input,
  dialogs, or destructive actions. API-25 recovery does not claim unavailable API-26 callbacks.

## Consequences

### Positive

- TV users retain control and clear focus for every page/browser decision.
- No remote/browser action can accidentally expand WebView/OS privilege.
- Data deletion is honest, explicitly confirmed, and auditable.
- Cleanup/recovery becomes a deterministic state-machine problem rather than an ad hoc lifecycle
  workaround.
- A controller failure does not strand the TV or leave input live indefinitely.

### Trade-offs

- Dialog/data/lifecycle paths have many race cases and require fake-clock/port testing.
- Clear-data requires two surfaces (Windows request plus TV confirmation), which is intentional
  friction for a destructive action.
- Browser may close after grace expiry rather than preserving a potentially stale controller state.
- Some website dialogs/features are deliberately unsupported or cancelled.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Let WebView show default dialogs without receiver model | D-pad/focus/expiry/host state become unreliable and untestable. |
| Let host immediately clear all data | Destructive action lacks physical-TV confirmation and scope is opaque. |
| Use browser dialog for SSL/permission consent | Violates fixed security profile and risks privilege escalation. |
| Keep active browser forever on disconnect | Retains invisible resource/input state with no bounded ownership. |
| Retain WebView in service to survive Activity recreation | Violates lifecycle ownership and increases leaks. |
| Retry stale dialog/data actions after reconnect | May act on a changed page/session without current user intent. |

## Invariants and validation

- Pure dialog/data/lifecycle reducers use fake monotonic clocks and exhaust all state/event
  permutations; they have full owned-code branch coverage plus mutation tests.
- Secure-loopback tests prove old/plain/unauthenticated dialog replies and clear commands do not
  reach BrowserPort.
- Driver/Robolectric tests prove dialog resolution, storage clear scope, parent detach/destroy,
  exactly-once cleanup, activity recreation, and no service-held WebView/Activity reference.
- Instrumentation uses controlled fixtures for alert/confirm/prompt/data markers and tests every
  denied permission/SSL/popup/chooser route separately.
- UiAutomator/physical Fire TV tests prove focus, Back, Home, pause/resume, disconnect overlay,
  surface switching, and recovery.
- A no-preview target soak records memory/native heap/GC/FD/thread/CPU trends and turns every defect
  into a regression test before release.

## Revisit criteria

A broader deletion scope, non-local confirmation path, extra dialog type, different grace policy,
or concurrent browser surface requires a new ADR with a privacy/lifecycle threat model, full
state-machine tests, UI evidence, and target soak results.

## References

- [Slice 06 — lifecycle, dialogs, data, and surface safety](../SLICE-06-LIFECYCLE-DIALOGS-DATA-AND-SURFACE-SAFETY.md)
- [ADR-0006 — WebView security profile](ADR-0006-WEBVIEW-SECURITY-PROFILE.md)
- [ADR-0007 — surface lifecycle](ADR-0007-SURFACE-LIFECYCLE-AND-TV-FIRST-UX.md)
