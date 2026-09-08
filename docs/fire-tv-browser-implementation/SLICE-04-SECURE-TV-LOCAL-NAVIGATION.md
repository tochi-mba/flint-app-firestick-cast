# Slice 04 — secure TV-local navigation

**Governing ADRs:** [ADR-0001](adrs/ADR-0001-TV-RESIDENT-BROWSER-OWNERSHIP.md), [ADR-0003](adrs/ADR-0003-SEPARATE-PINNED-TLS-BROWSER-SESSION.md), [ADR-0005](adrs/ADR-0005-PROTOCOL-V2-CANONICAL-CONTRACT.md), [ADR-0006](adrs/ADR-0006-WEBVIEW-SECURITY-PROFILE.md), [ADR-0007](adrs/ADR-0007-SURFACE-LIFECYCLE-AND-TV-FIRST-UX.md), and [ADR-0011](adrs/ADR-0011-TEST-EVIDENCE-AND-LATENCY-GOVERNANCE.md).

## Outcome

A verified Windows host can deliberately open or navigate an HTTPS address on the **TV-resident**
browser. The receiver WebView renders the owned fixture and a controlled public HTTPS page; the TV
remote remains usable for visible focus and history/back navigation. Windows receives bounded safe
navigation state (canonical safe URL, title, progress, history flags, and safe errors) and exposes a
minimal address-plus-Go control only after the secure session is ready.

There is no PC-to-TV page text, semantic remote, pointer, preview, dialog relay, clear-data action,
or Windows-rendered browser. The TV display is canonical.

## Entry criteria

- Slice 03 secure BrowserSession, pinning, pairing, one-client limit, and no-plain-fallback tests
  are green on a real target.
- Slice 02 v2 contract includes reviewed vectors for command/state fields and limits.
- The physical device record from Slice 01 permits a WebView feature trial. If it says unavailable,
  this slice ends with the same unavailable state and documents the reason.

## Vertical user journey

1. The user selects an eligible receiver and completes secure verification.
2. Windows enables one address field and a Go action. Enter/Go is a deliberate Navigate command,
   never desktop keystroke forwarding.
3. BrowserSession sends Open/Navigate only through TLS; ReceiverService verifies session ownership,
   epoch, command ID, and surface transition before asking a BrowserPort to act.
4. ReceiverActivity renders the WebView through an AndroidView on the UI thread. The TV loads an
   owned fixture first and can navigate to an allowed HTTPS address.
5. The receiver emits bounded BrowserState messages. Windows shows “Loaded by TV,” title/progress,
   safe address, history flags, loading/block/error state, and Close.
6. A blocked/invalid URL, certificate error, popup/permission request, lifecycle failure, or surface
   conflict returns a safe error. It never opens an external app, grants permission, renders
   untrusted file/content data, or falls back to the plain socket.
7. From the TV remote, the focused WebView can receive ordinary page focus/D-pad navigation. Back
   follows the documented hierarchy: dismiss local dialog (none in this slice), page history if
   present, then local close choice/idle. The host semantic-key remote is still unavailable.

## Scope

### In scope

- Pure receiver browser models, URL policy, command reducer, state reducer, effects/observations,
  BrowserPort boundary, and BrowserCoordinator with epoch/session ownership.
- Secure dispatch of only Open, Navigate, and Close. Other syntactically valid v2 browser commands
  return the bounded FeatureNotEnabled state until their owning slice.
- SurfaceMode.Browser plus strict mutual exclusion with player/mirror.
- Receiver AndroidView WebView driver, UI ownership, secure WebView profile, deterministic owned
  fixture, safe navigation state, minimal TV browser chrome, and cleanup.
- Minimal Windows navigation/status/close controls and no-preview screen state.
- Controlled Android instrumentation, Robolectric, receiver snapshots, and real Fire TV D-pad
  evidence.
- Receiver-local same-clock measurements for command acceptance, UI dispatch, page callback, state
  send; host-observed Go-to-state round trip keyed by epoch/navigation ID.

### Explicitly out of scope

- Host semantic remote, page text input, pointer/scroll mapping, preview image capture/transport,
  native JavaScript dialog bridge, clear browser data, tabs, downloads, upload/file chooser,
  external intent, permission prompt, web permission, or DRM promise.
- Any WebView native bridge: addJavascriptInterface, JavaScript evaluation, WebMessagePort,
  WebMessageListener, injected scripts, DOM scraping, or remote debugging outside an explicitly
  debug-only build.
- HTTP, file, content, data, javascript, intent, market, tel, mailto, custom scheme navigation,
  relative/scheme-less URL coercion, user-info URLs, mixed content, or multiple windows.
- Changing android:usesCleartextTraffic blindly. Existing media paths require an independent audit;
  this browser's URL policy is HTTPS-only immediately.

## Browser domain contract

### Models and ownership

Use immutable models/effects/observations with one owner per responsibility:

| Component | Owns | Must not own |
|---|---|---|
| BrowserUrlPolicy | Parse, validate, canonicalize, safe display form, rejection code. | WebView, network connection, mutable global state. |
| BrowserCommandReducer | Epoch/command-ID validation and effects for Open/Navigate/Close. | Android APIs, UI objects, TLS socket. |
| BrowserStateMachine | Browser lifecycle/loading/history/error state and monotonic revision. | Raw WebView/Activity reference. |
| BrowserCoordinator | Secure session ownership, exclusive surface transition, port effect execution, final publication. | Retained WebView/Activity. |
| BrowserPort / BrowserWebViewDriver | UI-thread-only WebView operations and callbacks. | Protocol parsing, session authentication, business policy mutation. |
| ReceiverActivity / ReceiverBrowserSurface | AndroidView attach/detach, visible focus, activity lifecycle. | Listener socket, TLS identity, long-lived Service UI reference. |
| BrowserSession | Command ID allocation, TLS framing, host state ordering. | URL-policy decisions or local WebView. |

A new Open/Navigate establishes a new epoch/navigation ID. The coordinator rejects stale command
epochs/IDs before calling BrowserPort. It publishes a monotonically revised safe BrowserState.
Duplicate/stale callback observations cannot reopen a prior page or overwrite newer state.

### URL policy

The policy is pure and has no “best effort” correction. It accepts only an absolute HTTPS URI with
a real host and a validated length at most 4 KiB. It:

- trims only allowed outer presentation whitespace; rejects embedded control characters,
  malformed UTF-8, relative/scheme-less forms, empty host, invalid port, user-info, and malformed
  percent escaping;
- normalizes/canonicalizes only in a documented deterministic way; preserves a safe display value
  without credentials because credentials are rejected;
- permits the WebViewAssetLoader appassets HTTPS fixture only in debug/instrumentation configuration,
  never through ordinary host navigation;
- rejects HTTP and all non-web/local/application schemes without trying a fallback;
- validates redirect/override targets with exactly the same policy;
- emits bounded safe reason codes such as InvalidAddress, BlockedScheme, BlockedLocalResource,
  CertificateRejected, PopupDenied, PermissionDenied, and RendererStopped.

### Fixed WebView security profile

The first real public navigation installs this profile as an indivisible unit. A test must fail if
any setting/callback is omitted:

| Surface | Required setting/behaviour |
|---|---|
| JavaScript | Enabled only because modern HTTPS sites need it; no native bridge or injected code exists. |
| Storage/cookies | DOM storage may be enabled; third-party cookies are disabled by default. No compatibility exception is added in this slice. |
| File/content | File and content access disabled; file-URL cross-origin access disabled; tests never use file URI fixtures. |
| Mixed content | Never allow. |
| Navigation | URL policy runs on initial load and every override/redirect; external intents are denied. |
| TLS errors | Cancel unconditionally; a website certificate error is never a user-consent prompt. |
| Windows/popups | Multiple windows and creation requests denied. |
| Permissions | Web permissions, geolocation, media capture, file chooser/upload, download, and custom chooser requests denied. |
| Debugging | Disabled in release; debug-only enablement cannot expose production pairing/browser data. |
| Renderer API | Optional AndroidX APIs are feature-gated. API-25 code must not claim API-26 renderer-death/Safe Browsing callbacks. |

## Planned files and boundaries

| Area | Planned change |
|---|---|
| receiver/browser | BrowserUrlPolicy, models, reducer/state machine, BrowserPort, BrowserWebViewDriver, security profile, coordinator, controlled fixture. |
| receiver service/UI/activity | Browser-specific immutable UI state, exclusive surface transition, AndroidView surface, lifecycle/focus/Back routing. Service never retains WebView. |
| receiver net | Secure BrowserSession dispatch to coordinator; ordinary listener stays rejecting. |
| receiver build/manifest | AndroidX WebKit/Espresso-Web dependencies and narrowly scoped config; cleartext manifest unchanged pending audit. |
| protocol/session tests | Secure command/state sequencing and FeatureNotEnabled response; no schema expansion without revisiting Slice 02 vectors. |
| src/Flint.Session | State reducer/order handling and only Open/Navigate/Close send facade. |
| src/Flint.App | BrowserPage address/Go/status/Close, exact disabled reasons, no text/preview controls. |
| tests | Core URL/property tests; receiver fake-driver, Robolectric, snapshot, Espresso-Web, Windows VM/Headless/snapshot, loopback secure tests. |

## Test-first implementation order

| Step | Red test to add first | Minimum production change | Required green proof |
|---:|---|---|---|
| 1 | BrowserUrlPolicyTests enumerate allowed/blocked inputs. | Pure parser/policy and bounded safe error model. | 100% line/branch, property corpus, fuzz seeds, and mutation kills for scheme/host/user-info/length checks. |
| 2 | BrowserReducerTests show stale epoch, duplicate command, surface conflict, and disabled action could reach a port effect. | Immutable command/state/effect reducer with FeatureNotEnabled outcome. | State transition/property tests prove no stale effect and monotonic revision. |
| 3 | BrowserCoordinatorTests with a fake BrowserPort show unauthenticated/plain/stale command could navigate. | Secure-only dispatch and session/epoch ownership checks. | Receiver service tests cover disconnect, close, mirror/player conflict, error, and final idle state. |
| 4 | BrowserWebViewDriverTests show each requested effect has wrong thread/order or retains view after close. | UI-thread driver and explicit attach/stop/detach/destroy sequence. | Robolectric proves driver calls and no Activity/WebView reference is stored in service state. |
| 5 | BrowserSecurityProfileTests fail one setting/callback at a time. | Fixed profile/callback implementation. | Static/source guard plus unit tests prove no bridge/evaluate path and every denial is explicit. |
| 6 | BrowserFixtureInstrumentationTest fails initial load/title/progress/history/redirect behaviour. | WebViewAssetLoader fixture and Espresso-Web page assertions. | Deterministic test has no public network dependency. |
| 7 | BrowserUnsafeWebViewInstrumentationTests show an unsafe action could proceed. | Override/error/ChromeClient denial routes. | HTTP/unsafe URI/mixed content/SSL/popup/permission/chooser/intent denial tests all pass. |
| 8 | ReceiverBrowserSurfaceSnapshotTests and D-pad tests show blank/no-focus/back trap. | Minimal overscan-safe loading/ready/error/close chrome and event path. | Approved deterministic snapshots plus UiAutomator real-TV D-pad/Back/Home/pause/resume evidence. |
| 9 | BrowserPageNavigationTests show Go enabled too early or state error leaks unsafe detail. | Windows minimal page/view-model and BrowserSession facade. | Headless command/focus/disabled-state tests and snapshots pass. |
| 10 | BrowserNavigationHardwareTest is explicit and measures target fixture/HTTPS route. | Hardware runner scenario and measurement record. | TV is canonical and recovery works after real navigation failure. |

### Required URL/property corpus

Include valid baseline URLs and all adversarial forms:

- casing, leading/trailing whitespace, Unicode/IDN, IPv4/IPv6 where allowed by the chosen policy,
  encoded delimiters, explicit/default ports, fragments, queries, redirects, and long-but-valid URL;
- empty, relative, scheme-less, unsupported scheme, HTTP, file, content, data, javascript, intent,
  market, tel, mailto, custom app, malformed percent, control character, whitespace in host,
  user-info, empty/invalid host, invalid port, malformed Unicode, too-long input, and malformed URI;
- callback/redirect URL that is safe on first request but unsafe on next hop;
- safe error output length/characters and proof it never echoes user text/credentials.

## Full-suite checkpoint

Run focused tests after each TDD step, then the complete hardware-free gate:

~~~powershell
.\scripts\build.ps1
.\gradlew.bat --no-daemon :protocol:check :receiver:testDebugUnitTest :receiver:lintDebug --console plain
python scripts/check-site.py
~~~

Run Espresso-Web/instrumentation and Fire TV D-pad/navigation checks explicitly against the selected
serial. They must use the owned fixture first. A public HTTPS smoke page is an observation, not a
deterministic automated dependency.

## Latency/resource checkpoint

- Define a navigation correlation using epoch/navigation ID. Measure host Go-to-first matching
  BrowserState round trip and receiver local spans separately; never subtract host/TV clocks.
- Capture the receiver timeline: TLS read, codec decode, reducer acceptance, UI-thread dispatch,
  WebView callback, state encode/write. Capture host receipt and view-model application separately.
- Record cold and warm fixture runs plus controlled HTTPS observation. Include p50/p95/p99/max,
  sample count, error count, receiver heap/native heap/GC, CPU/thermal if available, and task/thread
  count.
- Do not add preview capture or bitmap allocation. Any WebView navigation path that causes
  unbounded memory, stale callbacks, or command backlog is a stop condition.
- Establish a comparable baseline for later remote-input and preview regressions. The values are
  evidence, not published latency claims.

## Exit evidence

| Evidence | Required result |
|---|---|
| Security | Every initial/redirect URL and every WebView escape hatch listed above is proven denied; no native bridge exists. |
| Functional | A verified host can Open/Navigate/Close through TLS, TV WebView renders owned fixture and controlled HTTPS, and safe state returns. |
| Receiver UX | Real Fire TV evidence shows visible D-pad focus, usable Back hierarchy, overscan-safe chrome, pause/resume, and recovery to idle. |
| Isolation | Browser is mutually exclusive with mirror/player; plain listener cannot reach coordinator; service has no retained WebView. |
| Windows UX | Address/Go/status only enable after secure-ready; no text/pointer/preview control appears. |
| Quality | New owned URL/domain/ordering code has 100% line/branch, fuzz/property/mutation proof; instrumentation/snapshots are reviewed. |
| Measurement | Dated target record contains cold/warm navigation/resource baselines and failures. |

## Rollback and stop conditions

The secure session remains useful as a capability/verifier even if navigation is disabled. Gate
SurfaceMode.Browser and the address controls behind the secure capability so legacy features do not
change.

Stop if any unsafe WebView path cannot be denied, D-pad focus/back cannot be made reliable,
WebView lifecycle leaves a retained reference, unsafe page detail crosses state/logs, API-25 provider
behaviour invalidates the controlled fixture, or navigation creates an unbounded resource path.
Document the failure and keep browser navigation unavailable.
