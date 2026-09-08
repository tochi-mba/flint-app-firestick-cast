# Slice 01 — feasibility, capability, and quality gate

**Governing ADRs:** [ADR-0001](adrs/ADR-0001-TV-RESIDENT-BROWSER-OWNERSHIP.md), [ADR-0002](adrs/ADR-0002-PLATFORM-AND-DISTRIBUTION-SCOPE.md), [ADR-0006](adrs/ADR-0006-WEBVIEW-SECURITY-PROFILE.md), [ADR-0009](adrs/ADR-0009-OPT-IN-BOUNDED-JPEG-PREVIEW.md), and [ADR-0011](adrs/ADR-0011-TEST-EVIDENCE-AND-LATENCY-GOVERNANCE.md).

## Outcome

Flint can tell the truth about whether a selected receiver is even a candidate for the experimental
TV-resident browser. The normal Windows experience shows a neutral, actionable unavailable state;
it does not show a fake browser, active address bar, pairing flow, or preview control. An explicitly
opt-in developer probe can produce reproducible evidence on a real Android-based Fire TV receiver.

This slice proves the platform before the project commits to a browser transport or UI. It makes no
claim that browsing is available.

## Entry criteria

- The master plan and the implementation-slice README have been reviewed.
- A developer has an explicitly selected Android-based Fire OS receiver. Vega OS is a hard
  unsupported result, not a condition to work around.
- The developer understands that real-device work may show an ADB authorization prompt and may
  install/launch a debug receiver only after the user is shown exactly what will happen.
- No new browser capability has been advertised as public/Appstore-ready.

## User-visible vertical behaviour

1. Selecting a device exposes a Web capability card/page state with one exact verdict and a remedy:
   UnsupportedPlatform, ProtocolTooOld, SecureEndpointUnavailable, WebViewSmokeTestPending,
   WebViewUnsupported, or Available.
2. Before later slices exist, Available means only “this receiver passed the evidence gate and is
   eligible for the next implementation slice”; its UI text explicitly says that secure browser
   control is not yet installed.
3. The receiver debug screen can run a one-shot compatibility probe only after an explicit
   developer action. It reports a bounded, redacted result and returns to idle.
4. A failed/unknown probe leaves normal Cast, Media, Screen, pairing, and receiver startup
   behaviour unchanged.

## Scope

### In scope

- A pure, cross-platform browser availability model and reducer in the existing Flint-owned
  capability layer.
- Windows capability presentation for the neutral experimental verdict, including a reason and
  remediation that a screen reader can read.
- A debug-only receiver BrowserCompatibilityProbe with a single WebView instance created on the UI
  thread, never in ReceiverService.
- A deterministic owned fixture served through WebViewAssetLoader under the appassets HTTPS origin,
  plus a controlled public HTTPS smoke route configured outside source where necessary.
- A signed evidence record format and an opt-in physical-device runner design.
- Browser telemetry vocabulary and redaction rules before any browser data is sent across a network.
- Quality-gate infrastructure: focused tests, coverage policy, fixture/snapshot approval rules, and
  an explicit browser hardware-test category that skips outside a selected device run.

### Explicitly out of scope

- Browser protocol-v2 messages, a BrowserSession, certificate pinning, a TLS listener, URL/text
  transport, preview, pointer injection, page dialogs, or a normal WebView surface.
- Publishing the receiver or presenting browser functionality as generally available.
- A hard-coded Fire TV model list, WebView-provider package name probe, or an API-26 callback
  assumption on the Fire OS 6/API-25 target.
- Measuring or promising end-to-end control latency. This slice establishes methodology and device
  baselines only.

## Design decisions locked here

| Decision | Required implementation rule | Testable consequence |
|---|---|---|
| Capability shape | First inspect the existing CapabilityReport invariant: it emits exactly one ModeVerdict per CastMode. Either add a deliberate WebBrowser CastMode with all exhaustive callers updated, or add a separate typed browser capability record. Do not bolt a nullable browser flag onto generic Android status. | Every selected device has exactly one browser verdict without disturbing existing mode verdicts. |
| Capability wording | A verdict is a fact about the selected device/build, not a marketing state. | Every verdict maps to one visible reason and remediation. |
| Platform rule | Android-based Fire OS is required; Vega and unknown/unverified platforms are unavailable. | No unknown platform takes an optimistic path. |
| Probe ownership | ReceiverActivity/UI owns probe WebView lifecycle; service receives only bounded observations. | Unit tests prove service state has no WebView/Activity reference. |
| Fixture origin | Owned fixture uses WebViewAssetLoader HTTPS appassets origin, never file URI shortcuts. | Instrumentation proves fixture navigation without file access. |
| Privacy | Logs/diagnostics hold outcome codes and bounded timing/resource values only. | URL, input text, cookies, headers, page body, preview pixels, and certificate material are absent from normal logs. |
| Hardware tests | Physical tests are opt-in, device-named, and cleanly skipped by default. | Normal CI has no ADB/TV dependency. |

## Planned files and ownership

| Area | Planned change |
|---|---|
| src/Flint.Core | Decide and implement the capability shape in CastMode, CapabilityAssessor, CapabilityReport, FireTvDevice, and related models. |
| tests/Flint.Core.Tests | Reducer table tests, invalid-input/property tests, model serialization/redaction tests. |
| src/Flint.App | Web navigation shell entry in an unavailable-only state; capability card/view-model/accessibility text in MainWindowViewModel and related views. |
| tests/Flint.App.Tests | Headless command, disabled-control, semantic, and snapshot tests. |
| receiver/src/debug and receiver/src/main | Debug-only BrowserCompatibilityProbe, UI-thread adapter, safe owned fixture configuration, bounded result model. Reuse ReceiverPreviewActivity only as a deterministic debug harness; do not turn it into product browser UI. |
| receiver/src/test and receiver/src/androidTest | Probe fake tests, Robolectric lifecycle tests, and WebViewAssetLoader fixture instrumentation. |
| scripts | Planned opt-in browser hardware runner and result-record helper; it must require serial plus acknowledgement. |
| docs/site | Browser section in LATENCY_BUDGET, experimental/policy wording, and a device evidence template. |

Do not add protocol fields merely to report this first local capability. Slice 02 owns the
wire-contract surface; Slice 03 sends authenticated capability over TLS.

## Test-first implementation order

Run the named test immediately after adding it and observe a real failure. Never add all tests and
all production code in one unreviewable change.

| Step | Red test to add first | Minimum production change | Green/refactor proof |
|---:|---|---|---|
| 1 | BrowserCapabilityReducerTests maps every raw device observation to exactly one verdict. | Immutable observation/value types and a pure exhaustive reducer. | Branch coverage is 100%; unknown/null/malformed observations resolve to unavailable. |
| 2 | BrowserCapabilityRemediationTests checks reason text, remediation, and no false Available state. | One centralized mapping used by UI/diagnostics. | Property test proves every enum value has a non-empty safe explanation. |
| 3 | BrowserCapabilityTelemetryTests rejects/redacts sensitive fields and enforces bounds. | Typed telemetry event with allow-listed fields only. | Mutation test kills removal of each redaction/bound check. |
| 4 | BrowserPageComingSoonTests and snapshots show neutral disabled state with accessible reason. | Add the Web shell destination and unavailable view-model; controls remain absent/disabled. | Avalonia Headless verifies focus order, names, keyboard navigation, 100/125/150% snapshots. |
| 5 | BrowserCompatibilityProbeTest proves one-shot state, cancellation, timeout, no service-owned UI reference, and bounded result. | Receiver probe coordinator with an injected WebView probe port/fake. | Receiver JVM suite verifies duplicate start, pause, failure, and destroy order. |
| 6 | BrowserOwnedFixtureInstrumentationTest loads title and deterministic marker through appassets HTTPS. | Add the owned test fixture and WebViewAssetLoader setup. | Espresso-Web waits on owned fixture only; no public network dependency. |
| 7 | BrowserProbeHardwareTest is marked explicit/ignored and verifies selected-device facts. | Add opt-in runner/record schema, not a hidden normal test. | No-device invocation skips; named-device invocation produces a reviewed evidence record. |

### Required cases for the pure reducer

The test table must cover the Cartesian product that matters, not a happy path:

- Android Fire OS known API, unknown API, receiver absent, receiver version too old, secure endpoint
  absent, probe pending, probe success, probe timed out, probe failed, preview unsupported, and
  explicit policy-developer-only state.
- Vega, desktop/emulator, missing manufacturer/API, contradictory observations, stale observation
  timestamp, and unsupported future enum data.
- A probe success must not bypass an incompatible platform, protocol, or later secure-endpoint
  requirement.
- Remediation text never asks the user to weaken TLS, enable cleartext, bypass ADB authorization, or
  install an unsupported receiver.

### Required receiver probe cases

- Probe starts only in the debug/developer entry point and exposes a neutral “running” state.
- A second start while one is active is rejected/coalesced without creating a second WebView.
- The WebView is created and invoked on the UI thread; test with an injected dispatcher/port.
- Owned fixture success, controlled HTTPS success/failure, JavaScript capability observation,
  navigation timeout, render error, cancellation, activity pause/resume, and destruction all emit
  one bounded final result.
- The result includes legal diagnostics available on API 25 (for example API level, user-agent
  string if permitted, engine behavioural result) but does not pretend
  WebView.getCurrentWebViewPackage exists there.
- Probe is destroyed/detached before result completion; no page, screenshot, cookies, or text is
  retained in state.

## Physical-device evidence protocol

This is a required gate, but it is not part of the default test run.

1. Ask the user to select the serial. Print it, model, API level, receiver package/version, and the
   exact install/launch action. Stop until acknowledgement is supplied.
2. Record Wi-Fi SSID only if a privacy review permits it; otherwise record a network topology label,
   not the identifier. Never capture credentials.
3. Run the owned fixture, then the controlled HTTPS route. Exercise JavaScript availability,
   D-pad/Select, Back, Home, pause/resume, Wi-Fi loss/restore if authorized, destroy/recreate, and
   the candidate preview capture method without transmitting an image.
4. Record pass/fail/error class, elapsed durations measured on the local device, CPU/memory/GC
   samples, and any visible focus/recovery defect.
5. Store raw telemetry in the approved local evidence location and summarize only redacted values in
   docs/LATENCY_BUDGET.md. Link a build SHA and date; do not write “low latency” from a single run.

The record must identify whether preview capture is viable, unavailable, or inconclusive. If it is
unavailable, later slices continue as state-only browser control; they do not substitute
MediaProjection, Accessibility, or screen recording.

## Full-suite checkpoint

After the focused red-green-refactor loop succeeds, run:

~~~powershell
.\scripts\build.ps1
.\gradlew.bat --no-daemon :protocol:check :receiver:testDebugUnitTest :receiver:lintDebug --console plain
python scripts/check-site.py
~~~

Run the physical suite only with the explicit serial/acknowledgement command introduced here. Its
absence on a developer machine is a skip, not a failure of the default suite. Its result is required
to exit this slice.

## Latency/resource checkpoint

This slice establishes a baseline rather than a pass/fail latency number:

- Define the command/state/preview trace vocabulary in docs/LATENCY_BUDGET.md and verify its
  redaction in tests.
- Collect enough warm and cold samples to distinguish startup from steady state. Record sample
  count, warm-up exclusions, p50/p95/p99/max, memory/GC/CPU method, and error count.
- Record only same-device durations and host-observed round trips; never subtract unsynchronized
  Windows and TV timestamps.
- Set future regression limits only after this data is reviewed. Mark all numbers as measured,
  target, or not-yet-measured.

## Exit evidence

| Evidence | Required result |
|---|---|
| Core/app/receiver tests | New owned decision code is 100% line/branch covered; snapshots and semantics reviewed. |
| Hardware-free suite | Root Rust/.NET, Gradle protocol/receiver/lint, and site check pass. |
| Physical target record | A selected Android Fire OS target has a dated fixture/HTTPS/D-pad/lifecycle evidence record, or a dated unavailable result with exact reason. |
| Capability UI | No user can activate a nonexistent browser; every state says why and what to do next. |
| Privacy review | Normal telemetry has no URL/text/cookie/page/preview/certificate secret fields. |
| Policy wording | Documentation and UI describe experimental/sideload scope truthfully. |

## Rollback and stop conditions

The entire slice is guarded by the new capability verdict. Remove/disable the developer probe and
the Web shell entry if it causes a regression; existing Cast/Media/Screen paths remain untouched.

Stop rather than progress if the target is Vega, the receiver cannot load the owned fixture, D-pad
focus cannot be made reliable, WebView creation/lifecycle leaks, policy forbids the intended scope,
or a measurement reveals an unbounded resource path. Record the fact and keep the feature
unavailable.
