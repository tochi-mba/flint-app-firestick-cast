# Slice 05 — secure remote controls and explicit text

**Governing ADRs:** [ADR-0003](adrs/ADR-0003-SEPARATE-PINNED-TLS-BROWSER-SESSION.md), [ADR-0005](adrs/ADR-0005-PROTOCOL-V2-CANONICAL-CONTRACT.md), [ADR-0007](adrs/ADR-0007-SURFACE-LIFECYCLE-AND-TV-FIRST-UX.md), [ADR-0008](adrs/ADR-0008-PORTABLE-INPUT-AND-STALE-GUARDS.md), and [ADR-0011](adrs/ADR-0011-TEST-EVIDENCE-AND-LATENCY-GOVERNANCE.md).

## Outcome

A user with a verified BrowserSession can control the receiver-owned WebView from Windows using
portable semantic navigation controls and can send deliberate composed Unicode text to the TV. The
same controlled form fixture can be operated with the TV remote or the Windows remote. The browser
never receives raw Windows virtual keys, global desktop keystrokes, JavaScript injection, or
plaintext input.

Preview, pointer/drag/scroll mapping, dialogs, data clearing, and advanced visual polish remain
unavailable. The TV remains canonical.

## Entry criteria

- Slice 04 can securely Open/Navigate/Close an HTTPS page, publish safe state, and prove the
  fixed WebView security profile.
- BrowserState v2 has reviewed correlation fields for last accepted command and input IDs, with
  vectors in all three languages. If those fields were not included in Slice 02, stop and reopen its
  golden-vector review before enabling input.
- The real TV D-pad/back evidence from Slice 04 is recorded. A host remote is an additive path, not
  a replacement for the TV path.

## User-visible vertical behaviour

1. Windows enables Back, Forward, Reload, Stop, Close, and a compact semantic remote only when the
   secure session and receiver state say the action is valid.
2. The semantic remote offers only portable keys: Up, Down, Left, Right, Select, Back, Tab,
   Shift-Tab, Escape, PageUp, PageDown, Home, End, and Refresh. It shows disabled reasons and has a
   keyboard-accessible equivalent; it does not install a global hook.
3. Windows provides a separate **Send text to TV** field. The user types normal Windows text there
   and presses the visible Send control. The address bar remains a local navigation field; it is
   never conflated with page text.
4. The receiver validates TLS/auth/session/epoch/monotonic sequence before forwarding semantic input
   or text to BrowserPort. Old/duplicate/invalid input is ignored and counted without touching the
   WebView.
5. BrowserPort delivers text through the native input/IME-compatible path to the currently focused
   WebView element. It never executes JavaScript or reads DOM content to simulate typing.
6. The host sees a bounded acknowledgement through BrowserState correlation fields and safe page
   state. Text contents never return in BrowserState, diagnostics, packet logs, or telemetry.
7. If the focused page cannot accept native text, receiver state says InputUnavailable/NoFocusedField
   without faking success. The user can still use the local TV remote.

## Scope

### In scope

- BrowserCommand actions Back, Forward, Reload, Stop, Close and their state-dependent availability.
- BrowserInput semantic-key and text variants over the pinned TLS BrowserSession only.
- BrowserInputRouter, text/input validation, sequence ownership, focus loss handling, native
  InputConnection/KeyEvent-compatible BrowserPort dispatch, and full Android KeyEvent preservation.
- Windows functional remote/text controls, clear user-facing explanations, safe temporary field
  lifecycle, and view-model/session ordering.
- BrowserState command/input acknowledgement/correlation, bounded failure counters, and deterministic
  controlled form fixture tests.
- Receiver/host state when secure session disconnects while input is queued.

### Explicitly out of scope

- Host pointer, drag, mouse wheel, gesture, touch emulation, preview-based interaction, external
  keyboard capture, raw OS scan/virtual key codes, clipboard scraping, accessibility-service input,
  or desktop-wide event hooks.
- Preview capture, JPEG transport, image decode, persistent screenshots, page dialog relay,
  clear-data, downloads/uploads, tabs, permissions, or WebView bridge APIs.
- Silent text truncation, retrying a text payload after uncertain delivery, retaining text in a
  long-lived view-model/telemetry buffer, or treating a browser password field specially through
  page inspection.

## Input contract

| Input kind | Allowed in this slice | Validation and dispatch |
|---|---:|---|
| Semantic key | Yes | Known enum only, current epoch, strictly next sequence, secure/session owner; dispatch a platform-neutral semantic action. |
| Text | Yes | Valid UTF-8, at most 4 KiB, no malformed/unpaired surrogate representation, current epoch/sequence, explicit user Send; dispatch native composed text only. |
| Pointer | No | Decode/validate contract remains covered, but return FeatureNotEnabled without calling BrowserPort. |
| Scroll | No | Decode/validate contract remains covered, but return FeatureNotEnabled without calling BrowserPort. |
| Raw key code | Never | Reject at protocol boundary; it has no browser wire representation. |
| JS/native bridge | Never | Static and runtime guard tests fail if one is added. |

The sender allocates sequences in BrowserSession, not a view-model. A send operation has three
distinct outcomes: rejected locally before serialization, accepted by receiver/reported via bounded
acknowledgement, or disconnected/unknown. The third outcome must not blindly replay text because
the user cannot know whether a form submission field received it.

## Full TV key-event routing

The Activity preserves the complete KeyEvent rather than reducing it to a keyCode. The ordering is:

1. A later native dialog, when present, owns the event first.
2. The visible, focused WebView gets a valid D-pad/Select/navigation event.
3. If WebView does not consume Back, BrowserCoordinator applies page-history/local-close hierarchy.
4. If no browser is active, existing player/mirror/idle paths receive their previous behaviour.

Do not mutate this ordering to make host input easier. Tests must prove existing non-browser key
behaviour is unchanged.

## Planned files and boundaries

| Area | Planned change |
|---|---|
| receiver/browser | BrowserInputRouter, text policy, semantic action mapper, focus/input observation, BrowserPort native dispatch methods. |
| ReceiverActivity and receiver UI | Full KeyEvent preservation and browser-first event route with no change to system-owned Home/microphone/volume. |
| ReceiverService/coordinator | Secure epoch/sequence validation, command enablement/state update, queued-input cancellation on disconnect/focus loss. |
| src/Flint.Session | BrowserSession monotonic command/input allocation, send serialization, state acknowledgement reducer. |
| src/Flint.App | BrowserPage remote/text controls, transient sensitive-field handling, exact disabled/error states. |
| test fixtures | Deterministic owned form page with visible text/value-change marker and focus navigation; no live-site typing test. |
| tests | C#/Kotlin/Rust vector amendments if necessary, pure input/property tests, receiver fake-port tests, loopback TLS, Android instrumentation, Avalonia Headless/snapshots, explicit Fire TV D-pad test. |

## Test-first implementation order

| Step | Red test to add first | Minimum production change | Required green proof |
|---:|---|---|---|
| 1 | BrowserInputModelTests reject unknown semantic value, malformed text, too-long input, invalid epoch/sequence. | Pure input/value validation and typed safe errors. | 100% line/branch, property corpus for Unicode/bounds, mutation kills for every validation guard. |
| 2 | BrowserSessionSequenceTests show a view-model can invent/reuse sequence or concurrent send interleaves frames. | Session-owned atomic/serialized sequence allocation and writer integration. | Loopback test proves strictly monotonic frames under concurrent UI actions. |
| 3 | BrowserInputRouterTests show stale/duplicate/focus-lost/unauthenticated input calls BrowserPort. | Router/reducer secure owner/epoch/sequence checks. | Fake-port table covers every allowed/denied input and disconnect cancellation. |
| 4 | SemanticKeyMapperTests fail missing key or accidental platform key mapping. | Explicit portable semantic-to-native mapping. | Every allowed semantic key and no raw code path is covered. |
| 5 | BrowserTextDispatchTests show text uses JS/evaluate path, alters contents, or succeeds without focus. | Native IME/InputConnection-compatible driver adapter. | Controlled fake verifies one composed payload, no script call, and safe no-focus outcome. |
| 6 | ReceiverKeyEventRoutingTests show Activity loses event metadata or Back bypasses page. | Browser-first full-KeyEvent route and explicit fallthrough. | Existing player/mirror tests remain green; browser Back hierarchy is exhaustive. |
| 7 | BrowserFormInstrumentationTests fail Tab/focus/text/history marker on owned fixture. | Fixture and Espresso-Web/UiAutomator assertions. | Unicode, non-Latin, emoji/grapheme boundary, focus loss, and error handling pass deterministically. |
| 8 | BrowserPageRemoteTests show controls enable before TLS/state or text persists/leaks. | Windows remote/text VM/view controls and secure-state binding. | Headless focus/name/keyboard tests, snapshots, redaction tests, and no-global-hook source guard pass. |
| 9 | BrowserRemoteHardwareTest measures host action to TV fixture change. | Explicit scenario with visual/action correlation recording. | TV D-pad and Windows remote both work; preview remains disabled. |

### Required input test matrix

- Every semantic key in every receiver state: no browser, secure-ready/no page, loading,
  ready/focusable page, no focused field, page history/no history, surface switch pending, dialog
  absent, disconnecting, and closed.
- First sequence, next sequence, duplicate, gap, old epoch, future epoch, sequence overflow
  representation, concurrent Windows commands, reconnect, and session replacement.
- Empty text, ASCII, CJK, combining marks, emoji, right-to-left text, a 4-KiB exact payload,
  one-byte-over, NUL/control input, malformed surrogate representation, text after focus loss,
  text while loading, and cancel/disconnect between enqueue and acknowledgement.
- Keyboard traversal and visible focus at the TV edge; Back when WebView consumes it, when history
  exists, when no history exists, and when the browser is no longer active.
- Host UI hides/rejects text on unavailable/untrusted/plain/v1/disconnected state and never writes
  it to error strings, snapshots, test logs, or telemetry fixtures.

## Full-suite checkpoint

Run focused tests during the loop, then:

~~~powershell
.\scripts\build.ps1
.\gradlew.bat --no-daemon :protocol:check :receiver:testDebugUnitTest :receiver:lintDebug --console plain
python scripts/check-site.py
~~~

Run Android instrumentation and the selected Fire TV D-pad/form scenario explicitly. Default
hardware-free tests must skip absent TV hardware cleanly.

## Latency/resource checkpoint

- Use the state acknowledgement IDs to measure host action-to-receiver acceptance and host
  action-to-fixture state round trip without inventing synchronized one-way time.
- Record local spans for queue wait, TLS write/read, decode, reducer, UI dispatch, native input
  dispatch, WebView callback, state write, host receive, and view-model application.
- For an owned fixture, make the page's visible marker change after a semantic key/text input.
  Measure PC action-to-TV marker with the documented high-frame-rate camera method; record
  preview-disabled only in this slice.
- Collect at least the approved number of warm samples and record p50/p95/p99/max, lost/unknown
  acknowledgement count, control queue depth, allocation/GC, heap/native heap, CPU, and thermal
  state. A disconnected/unknown text send is a reliability observation, never retried silently.
- Do not enqueue input behind a preview (none exists) or unbounded task. Any increasing queue,
  allocation-per-input steady path, or receiver UI-thread blockage stops the slice.

## Exit evidence

| Evidence | Required result |
|---|---|
| Secure input | Text and semantic input cross only BrowserSession after pin/auth; raw platform keys and plaintext are impossible. |
| Functional fixture | Windows and TV remote can focus/navigate/fill the controlled fixture; user sees safe acknowledgement/state. |
| Safety | Stale/duplicate/invalid/no-focus/disconnected inputs never reach WebView and never fabricate success. |
| Privacy | Text does not appear in logs, BrowserState, snapshots, telemetry, or retained state after send completes/aborts. |
| Receiver UX | Full KeyEvent and Back hierarchy preserve existing non-browser modes and visible TV focus. |
| Quality | New domain/session/VM branches are 100% line/branch with property/mutation/loopback/instrumentation proof. |
| Measurement | Dated input/control baseline compares TV remote and host semantic/text paths with preview disabled. |

## Rollback and stop conditions

Input controls are individually capability-gated. Disable remote/text while leaving secure
navigation and TV-local browsing intact if any input proof fails.

Stop if text requires JavaScript/page inspection, if a host key can escape the semantic allow-list,
if ambiguous delivery could replay text, if input leaks into state/logs, if WebView cannot receive
native composed input reliably on the target, or if TV D-pad behaviour regresses. Do not substitute
a global hook, accessibility service, or screen-control shortcut.
