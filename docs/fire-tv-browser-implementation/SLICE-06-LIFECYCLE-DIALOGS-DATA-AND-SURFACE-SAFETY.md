# Slice 06 — lifecycle, dialogs, data, and surface safety

**Governing ADRs:** [ADR-0003](adrs/ADR-0003-SEPARATE-PINNED-TLS-BROWSER-SESSION.md), [ADR-0006](adrs/ADR-0006-WEBVIEW-SECURITY-PROFILE.md), [ADR-0007](adrs/ADR-0007-SURFACE-LIFECYCLE-AND-TV-FIRST-UX.md), [ADR-0010](adrs/ADR-0010-NATIVE-DIALOGS-DATA-CLEAR-AND-RECOVERY.md), and [ADR-0011](adrs/ADR-0011-TEST-EVIDENCE-AND-LATENCY-GOVERNANCE.md).

## Outcome

The browser is reliable enough to use before visual feedback is added. It handles page dialogs, data
clearing, receiver activity lifecycle, controller loss/reconnect, WebView/provider failure, and
browser/media/mirror transitions without trapping the TV screen, retaining Activity/WebView objects,
or leaking browser data. The TV remains locally operable if the Windows controller disconnects;
remote controls pause immediately and resume only after a fresh pinned authenticated session.

This slice deliberately hardens the useful navigation/input feature before preview adds memory,
capture, scheduling, and latency pressure.

## Entry criteria

- Slice 05 remote semantic/text input works through secure BrowserSession with real target evidence.
- The BrowserPort boundary is complete enough to fake every lifecycle/data/dialog effect.
- The selected device's Slice 01 report identifies API/provider limitations; API-25 branches remain
  distinct from API-26-or-newer renderer callbacks.

## User-visible vertical behaviour

1. JavaScript alert, confirm, and prompt appears as a TV-local native dialog in the 5% safe region.
   It visibly identifies the top-level origin, has a safe Cancel default, expires, and is fully
   D-pad operable.
2. Windows can observe the active dialog over TLS and may reply only to its current ID; the TV and
   host race safely, and the first valid reply wins. A dialog never turns into permission for SSL,
   file, popup, upload, external intent, or web permission.
3. A user can request **Clear TV browser data**. Windows shows a clear description, but the receiver
   requires a visible TV-local confirmation before deletion. Cancel/expiry/delete failure is explicit.
4. On a controller disconnect, remote input/text immediately stops. The TV retains a clearly marked
   local browser for a bounded reconnect grace window; on timeout, explicit Close, surface switch,
   or receiver destruction it cleans up deterministically and returns to idle.
5. Pause/resume, activity recreation, network change, WebView exception, and renderer stop result
   in a bounded safe state. The UI never shows a blank trapped screen or silently claims recovery.
6. Switching to Media or Screen destroys/detaches browser in the documented order; switching back
   constructs a fresh WebView only through the coordinator/UI boundary.

## Scope

### In scope

- One-active native page dialog model/bridge with expiring IDs, safe local display, TLS observation,
  reply validation, and cancel-default resolution.
- Data-clear request, receiver-local destructive confirmation, defined WebView storage scope,
  asynchronous completion/error state, and separation from receiver identity/pins.
- Browser lifecycle reducer/effects for activity pause/resume/recreate, network listener restart,
  authenticated host disconnect/reconnect, close, surface switch, and driver/render errors.
- Deterministic WebView detach/destroy/cleanup order and bounded pending-effect cancellation.
- Browser/player/mirror exclusivity in every state transition, visual state, and wire/session path.
- Target-device no-preview soak and resource/FD/thread/memory evidence.
- Windows/TV errors, recovery text, snapshots, accessibility, and diagnostics that distinguish
  controller loss, renderer failure, blocked URL, and data-clear result.

### Explicitly out of scope

- Preview capture/transport/decode, pointer/drag/scroll mapping, video feedback, automatic
  screenshot persistence, multi-tab support, downloads/uploads, permissions, external intents,
  third-party-cookie compatibility toggle, or reusing browser dialogs as web-permission consent.
- Pretending API-25 has API-26 renderer-death callbacks. On API 25, supported recovery is limited to
  observed driver/lifecycle errors and a documented user retry/recreate path.
- Clearing Windows BrowserTrustStore, receiver Keystore identity, host pairing record, other Android
  apps, DRM, or system browser data when the user asks to clear Flint browser data.

## Dialog contract

| Property | Rule |
|---|---|
| Origin | Use a bounded top-level origin derived from safe current navigation; never expose full page source/URL query/form values. |
| Types | Alert, confirm, prompt only. Before-unload uses cancel/default denial unless a separately reviewed native variant is added. |
| Queue | Exactly one active dialog. Later dialogs are rejected/cancelled while one is active; no unbounded queue. |
| Resolution | TV local D-pad reply or matching secure host reply may resolve it; first valid reply wins atomically. |
| Expiry | Uses an injected monotonic clock; expiry/cancel/disconnect/surface close resolve safely as Cancel. |
| Prompt text | UTF-8 bounded, no logs/telemetry/history. It is delivered only to the page's dialog callback after valid resolution. |
| Web permissions | Never reuse this dialog. SSL errors, popups, file choosers, downloads, external intents, geolocation/media/web permission stay denied. |
| Host observation | BROWSER_DIALOG/BROWSER_DIALOG_REPLY only after TLS/pin/auth/current epoch; old ID reply is ignored. |

## Data-clear contract

The receiver-local confirmation explains exactly this scope: Flint receiver WebView cookies, DOM
storage, HTTP cache, WebView history, and form/autofill data that the platform makes available to
the receiver app. It expressly does not clear the Windows trust pin, receiver TLS identity, another
app's data, Fire TV account, DRM license, or operating-system browser state.

The flow is:

1. Windows asks to clear data and shows a non-destructive pending state; it sends the already
   contract-defined secure ClearData command.
2. Receiver validates secure ownership/current epoch, opens a TV-local confirmation, and emits a
   bounded pending state. No deletion occurs until the TV answer is affirmative.
3. On confirm, BrowserPort stops/serializes page activity as needed and clears only the defined
   stores. Completion callbacks determine success/failure; a timeout becomes safe error.
4. Receiver creates a fresh safe BrowserState. It does not reload a prior page automatically without
   an explicit Navigate/Open. Windows shows the result and requires user action to browse again.
5. On cancel/disconnect/close/expiry, nothing is cleared. The command cannot be replayed after a
   reconnect because the confirmation belongs to the active epoch/session.

## Lifecycle and cleanup contract

### Surface transition

For Close, mirror/media selection, terminal browser error, grace timeout, activity destruction, or
service shutdown, execute exactly this ordered effect list and test it with a recording fake:

1. reject/cancel new browser commands and input for the active epoch;
2. dismiss/resolve the active page/data dialog safely;
3. stop loading and cancel pending driver/capture work;
4. publish transitional closing state so host/TV do not act on stale controls;
5. detach WebView from parent on the UI thread;
6. destroy WebView and dispose browser-owned buffers/bitmaps;
7. clear coordinator references/session epoch and release surface ownership;
8. publish one final idle/closed/error state and only then permit target surface activation.

### Disconnect policy

- On authenticated host disconnect: stop remote writer/input immediately, drop future preview work
  (none exists), show “Controller disconnected — TV controls remain available,” and start one
  bounded 30-second monotonic grace timer.
- A reconnect during the grace period must re-pin/re-authenticate and acquire a new BrowserSession;
  it receives current bounded safe state but cannot replay pending input/dialog/data-clear work.
- Grace expiry closes browser with the sequence above. A local user may explicitly Close sooner.
- A network interface change closes/restarts BrowserTlsServer via Slice 03 rules. The current TV
  browser obeys the same grace/close rule; it never stays remotely controllable over a stale socket.

The 30-second grace is a product-policy bound, not a performance target. It must be a named
constant with fake-clock tests, not a sleep in production tests.

## Planned files and boundaries

| Area | Planned change |
|---|---|
| receiver/browser | Dialog controller, data-clear effects/port APIs, lifecycle reducer, fakeable monotonic clock, cleanup coordinator. |
| receiver WebView driver | WebChrome dialog bridge, storage/history/cache clear adapter, activity/renderer-error observations, exact destroy order. |
| ReceiverService/UI/Activity | Surface ownership, disconnect grace, lifecycle events, TV confirmation/error/overlay chrome, no retained WebView. |
| receiver net | Session-end/network-restart signals cancel browser work; no browser frame accepted after close. |
| src/Flint.Session | Dialog/state ordering, reconnect state, reject stale reply/clear action. |
| src/Flint.App | Data-clear request/pending/result UI, dialog observation/reply, controller-disconnected/recovery states. |
| tests | Pure reducer/dialog/data policy, fake driver, secure loopback, Robolectric/lifecycle, instrumentation, TV/Windows snapshots and hardware soak. |

## Test-first implementation order

| Step | Red test to add first | Minimum production change | Required green proof |
|---:|---|---|---|
| 1 | BrowserDialogReducerTests show duplicate/old/expired reply or surface close can resolve the wrong callback. | Immutable active-dialog model, atomic resolution, injected clock. | Every dialog type/answer/source/race/expiry path has 100% branch coverage and mutation kills. |
| 2 | BrowserDialogTransportTests show plain/stale/unauthenticated host reply reaches page. | Secure current-ID/epoch/session dispatch. | Loopback tests prove host and TV first-wins race; stale reply has no side effect. |
| 3 | BrowserDataClearPolicyTests show scope ambiguity, remote command immediate delete, or failed clear reports success. | Request/TV-confirm/async-completion reducer and BrowserPort data-clear API. | Cancel/expiry/disconnect/error/complete tests prove no unauthorized or replayed deletion. |
| 4 | BrowserWebViewClearInstrumentationTest fails cookie/storage/cache/history/form reset fixture. | Driver clear implementation with callbacks and fresh state. | Deterministic fixture proves only defined receiver WebView data changes; trust identity remains. |
| 5 | BrowserLifecycleReducerTests show transition races retain browser ownership or route command after close. | Lifecycle/surface state machine and cancelable effects. | Property tests cover all event permutations, stale callback and new epoch. |
| 6 | BrowserDestroyOrderTests show detach/destroy/publish order wrong or service retains UI. | Recording driver and exact cleanup sequence. | Robolectric and fake tests prove parent removal, destroy once, no retained Activity/WebView. |
| 7 | BrowserDisconnectGraceTests show input/dialog/clear replay after disconnect or timer leak. | Fake-clock grace/session replacement handling. | Reconnect/timeout/network-switch/close paths are deterministic and bounded. |
| 8 | BrowserRendererRecoveryTests make unsupported API-25 callback claim. | Feature-gated driver observations and fallback safe error/recreate. | API-26 debug renderer crash test is isolated; API-25 is physical/manual evidence only. |
| 9 | BrowserLifecycleUiTests show no focus/error/confirmation state or unclear destructive wording. | TV and Windows state views/snapshots/accessibility semantics. | Every dialog/data/lifecycle state is snapshotted and D-pad/focus tested. |
| 10 | BrowserNoPreviewSoakTest is explicit and records 30-minute target session. | Hardware runner scenario and resource trace collection. | No unexplained monotonic heap/FD/thread/CPU growth; defects become regression tests. |

### Required lifecycle/property matrix

Test every event against idle, secure-ready, creating, loading, ready, dialog active, clear pending,
controller grace, closing, error, mirror active, and player active:

- activity pause/resume/recreate/destroy;
- secure host disconnect/reconnect/replacement; network lost/restored/interface changed;
- page error, driver exception, renderer observation where supported, user Back/Close;
- data-clear request/TV accept/cancel/expiry/callback success/failure;
- dialog local accept/cancel, host accept/cancel, race, stale ID, timeout;
- new navigation while old callback arrives; surface switch while input/dialog/data operation is
  pending; duplicate terminal event; service shutdown.

The expected result must be explicit for each pair: ignored, safe cancel, safe error, or exact
ordered cleanup. No test may only assert that “nothing crashed.”

## Full-suite checkpoint

Run focused domain/driver/UI tests, then:

~~~powershell
.\scripts\build.ps1
.\gradlew.bat --no-daemon :protocol:check :receiver:testDebugUnitTest :receiver:lintDebug --console plain
python scripts/check-site.py
~~~

Run instrumentation, API-26-only debug renderer evidence where applicable, and selected Fire TV
D-pad/data/lifecycle scenario explicitly. The 30-minute soak is a named hardware release gate,
never a normal CI sleep.

## Latency/resource checkpoint

- Preview is still absent. The goal is stable control latency and zero resource creep.
- During a no-preview 30-minute warm session, sample heap/native heap, GC count/pause, thread count,
  file descriptors, CPU/thermal state, listener/session count, and command queue depth at a fixed
  documented cadence. Record raw samples and p50/p95/p99/max where meaningful.
- Measure page/dialog/data-clear/close lifecycle spans using local monotonic timers and
  command/state IDs. Compare action-to-TV fixture marker with Slice 05's baseline under equivalent
  conditions.
- Treat growing resource trend, duplicate cleanup, late callback after close, queue breach, or
  controller input accepted during grace as a build/release failure even if average latency improves.
- If the target cannot sustain the no-preview browser without stable resources, do not add preview.

## Exit evidence

| Evidence | Required result |
|---|---|
| Dialog safety | One expiring TV-local dialog, safe Cancel default, origin bounds, first-wins secure reply, and no permission reuse are proven. |
| Data privacy | Clear-data scope is precise, TV-confirmed, asynchronous, non-replayable, and does not erase trust/identity outside its scope. |
| Lifecycle | All terminal/surface/network/controller paths run exact cleanup and leave a usable idle or local-TV state. |
| UI/TV UX | D-pad focus, overscan, confirmations, errors, close, and disconnected overlay are visible and accessible. |
| Resource evidence | 30-minute no-preview target soak shows bounded resource behaviour or documents an unavailable stop. |
| Quality | New owned reducers/policy/state paths have full line/branch/property/mutation proof; platform paths have reviewed instrumentation/hardware evidence. |

## Rollback and stop conditions

Dialogs, data clear, and reconnect grace are capability subfeatures. Disable them behind safe
defaults (cancel dialogs, hide data-clear command, close on disconnect) if a defect appears, while
preserving secure navigation only when still safe.

Stop if page dialogs require a non-native bridge, clear-data scope cannot be verified, a lifecycle
path traps/retains a WebView, controller disconnect leaves remote input active, renderer recovery is
claimed without API support, or the no-preview soak shows resource creep. Do not mask this with a
preview-quality reduction; preview has not begun.
