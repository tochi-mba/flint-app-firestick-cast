# Fire TV browser implementation slices

This directory turns the master [Fire TV browser plan](../FIRE_TV_BROWSER_PLAN.md) into small,
vertical, test-first delivery slices. A slice is complete only when a user-observable behaviour,
its automated proof, its measurement record, and its rollback boundary are all present. It is not
complete when production code merely compiles.

The feature remains a **sideload/developer experiment** until the policy, device, security, and
release gates in the master plan have been satisfied. These files deliberately do not describe a
public Appstore release as an implementation outcome.

**Governing decisions:** [Fire TV browser ADR index](adrs/README.md). Read the relevant ADRs before
starting a slice; the index defines the document-precedence order.

## Non-negotiable architecture

- The Android receiver owns WebView, navigation, cookies, page rendering, and all website network
  traffic. The Windows app is a remote and an optional bounded preview consumer; it is never a
  browser, proxy, DOM relay, or cloud middlebox.
- No browser URL, text, dialog response, or preview byte crosses the existing plaintext CastSession.
  Browser traffic starts only after a separate TLS BrowserSession has completed certificate pinning,
  pairing-code authentication, protocol-v2 negotiation, and receiver capability verification.
- The browser surface is mutually exclusive with mirror and player surfaces. ReceiverService owns
  surface/session state; ReceiverActivity owns the AndroidView/WebView lifecycle. A Service must
  never retain a WebView or Activity.
- Android WebView receives no JavaScript/native bridge, injected JavaScript, arbitrary scheme,
  popup, chooser, download, external intent, or web-permission escape hatch.
- The TV stays completely usable with its D-pad and 5% overscan-safe chrome. Host pointer preview
  input is optional convenience, never the only navigation path.
- A preview is opt-in, memory-only, newest-frame-only JPEG feedback. It is not a second streaming
  pipeline and must never delay a command, state update, or TV rendering work.

## Slice order and delivery graph

| Slice | Name | First usable outcome | Cannot advance until |
|---:|---|---|---|
| 01 | Baseline capability and evidence | Flint reports a truthful browser verdict and can record real target-device evidence. | The actual receiver is identified as Android-based Fire OS and the controlled fixture result is recorded. |
| 02 | Protocol v2 contract | Rust, C#, and Kotlin agree byte-for-byte on all existing and browser protocol shapes. | Existing v1 media parity is repaired and the golden corpus is complete. |
| 03 | TLS BrowserSession | A host and receiver can establish one pinned, authenticated browser-only session without plaintext fallback. | Certificate, trust, pairing, queue, and downgrade tests are green. |
| 04 | Secure TV-local navigation | The receiver safely renders a real HTTPS fixture/page, while Windows can issue only deliberate address navigation. | WebView security and first browser state end-to-end are proven. |
| 05 | Secure remote controls and text | Windows can send portable navigation keys and explicit composed text over the pinned session. | Ordering, IME input, error, and disconnect behaviour are proven. |
| 06 | Lifecycle, dialogs, and data safety | The usable browser handles dialogs, destructive data clearing, surface switches, and lifecycle failure safely. | Soak, cleanup, privacy, and recovery tests pass without preview. |
| 07 | Opt-in passive preview | Windows may view a bounded, memory-only receiver preview without starving controls. | Queue, resource, staleness, and physical-device measurements meet the recorded gate. |
| 08 | Preview interaction and accessibility | Preview click/scroll mapping and the polished accessible Windows interface are proven without making preview mandatory. | Geometry, input ordering, UI snapshot, and accessibility gates pass. |
| 09 | Hardening and release evidence | The experiment has an evidence-backed go/no-go decision and truthful documentation. | Full automated suite, long-run device session, and release checklist are signed off. |

The dependency graph is intentionally linear:

    01 evidence -> 02 wire contract -> 03 secure transport -> 04 TV navigation
        -> 05 remote controls -> 06 lifecycle safety -> 07 passive preview
        -> 08 preview interaction/accessibility -> 09 hardening

Do not merge slices to "save time." In particular, no WebView UI or host text field may precede
Slice 03, and no preview implementation may precede Slice 05's physical-device evidence.

## Definition of done for every slice

Every slice document names additional evidence, but all slices share this definition of done:

1. **Red first.** Add the named failing unit, property, integration, UI, or hardware test before
   the production behaviour. Capture the red result in the PR/implementation notes.
2. **Minimum green.** Implement only enough code to satisfy the new test. Keep platform APIs behind
   small adapters so owned policy is testable without a TV.
3. **Refactor green.** Refactor only after the slice test and its module test suite are green.
   Characterize any discovered platform quirk with a regression test before retaining a workaround.
4. **Full suite.** Run the current repository-wide suite after every slice. Hardware-only tests
   must skip cleanly in the default run; their explicit hardware result is a separate release gate.
5. **Coverage is proof, not theatre.** New Flint-owned decision code must reach 100% line and
   branch coverage. For parsers, state ordering, trust, URL policy, geometry, and queue policy,
   add property/fuzz tests and meaningful mutation checks. Do not claim coverage of Chromium or
   Android framework code that Flint does not own.
6. **Review generated evidence.** Golden vectors and image snapshots are generated only by a
   deliberate command, inspected as diffs, and then committed. Tests must never auto-approve them.
7. **Measure, do not guess.** Record target-device observations in docs/LATENCY_BUDGET.md (or its
   browser section) with device model, Fire OS/API level, receiver build, host build, network
   topology, test route, sample count, percentile method, and failures. A target is not a measured
   result.
8. **Rollback is real.** Each slice has a capability/feature gate that leaves Cast, Media, and
   Screen behaviour untouched when disabled. A failed gate yields a neutral unavailable state,
   never a half-working browser control.

## Standard test commands

The repository already has these hardware-free checks:

~~~powershell
.\scripts\build.ps1
.\gradlew.bat --no-daemon :receiver:testDebugUnitTest --console plain
~~~

The first command covers Rust and .NET; it does **not** run Gradle. A slice that changes Kotlin,
Android, or the Kotlin protocol must additionally run the relevant Gradle tasks. The standard
post-Slice-01 Android command should be:

~~~powershell
.\gradlew.bat --no-daemon :protocol:check :receiver:testDebugUnitTest :receiver:lintDebug --console plain
~~~

Validate task names against the checked-in Gradle build before making this a CI requirement. If a
module has a variant-specific task instead, update this document and the slice command together;
never silently omit Kotlin/Android tests because the root build is green.

Slice 02 also uses the intentionally gated vector generator when a reviewed wire contract changes:

~~~powershell
Set-Location flint-engine
cargo test --test golden -- --ignored regenerate
~~~

Each slice adds a focused command/filter to make red-green-refactor quick. The exact test names are
specified in its document. At the end of every slice, run both repository-wide commands above,
then run the explicit hardware suite only when the slice asks for it.

Before Slice 01 exits, add an explicit, opt-in physical-device runner rather than hiding ADB work
inside a normal unit test. Its final interface must require a serial and an acknowledgement flag,
for example:

~~~powershell
.\scripts\test-fire-tv-browser.ps1 -Serial <adb-serial> -AcknowledgePhysicalDevice
~~~

This is a planned command, not a claim that the script exists today. It must print every
device-affecting action, never install/uninstall/replace the receiver implicitly, and leave
the receiver state visible to the user.

## TDD test layers

| Layer | Purpose | Default suite? | Evidence that counts |
|---|---|---:|---|
| Pure unit tests | URL policy, models, reducers, state ordering, geometry, rate/size policy, trust decisions. | Yes | Exact branch and mutation coverage. |
| Property/fuzz tests | Frame parsing, Unicode/URL normalization, monotonic IDs, bounds, queue replacement. | Yes | Seeded corpus plus minimized regressions. |
| Golden vectors | Cross-language byte compatibility between Rust, C#, and Kotlin. | Yes | A complete, reviewed canonical corpus. |
| Loopback integration | TLS handshake, pinning, auth, framing, bounded writer, reconnect, cancellation. | Yes | No real receiver needed. |
| Receiver JVM/Robolectric | Android adapter settings, lifecycle ownership, UI model, deterministic surface snapshots. | Yes | Fakes make outcomes deterministic. |
| Android instrumentation | Real WebView fixture/security behaviour through Espresso-Web and controlled appassets. | Separately invoked | API/provider result recorded. |
| Avalonia Headless | View-model commands, focus, semantics, keyboard access, snapshots at scale. | Yes | Reviewed visual/accessibility results. |
| Physical Fire TV | D-pad, API-25 provider quirks, WebView rendering, resource/latency measurement. | Explicit only | Device-model-specific signed record. |

## Latency and resource discipline

The project must be fast, but it must be honest about speed:

- Do not infer an end-to-end one-way latency from Windows and TV timestamps; their monotonic clocks
  are not synchronized. Use same-host elapsed times, host-observed command-to-state round trips
  keyed by command ID, and a separately measured RTT.
- Record percentiles from raw samples; include p50, p95, p99, maximum, sample count, warm-up count,
  and the measurement interval. Averages alone hide stalls.
- Separate the paths in telemetry:
  host command enqueue -> TLS write; receiver read -> command validation; UI-thread driver apply;
  WebView callback -> receiver state write; host read -> UI state application. Preview adds capture,
  JPEG encode, queue replacement, read, decode, and render scheduling timestamps.
- Use a monotonic correlation ID rather than page title or URL as the trace key. Redact URLs, text,
  cookies, headers, certificates, and preview pixels from normal logs.
- The initial preview operating envelope is a hard resource bound, not a claimed performance number:
  one candidate slot, one outbound preview slot, JPEG only, maximum 960x540, maximum 768 KiB,
  maximum 5 fps while interactive, maximum 1 fps idle, and zero persistence.
- Control, state, pairing, and close messages always pre-empt preview work. A full preview slot is
  replaced/disposed, not queued. Backlog length above one is a defect.
- Reuse buffers/bitmaps/encoder output where APIs permit. Count allocations and GC during the
  physical run; do not allow a new allocation-per-frame steady path.
- Every optimization must retain a regression test and a before/after evidence record. Do not
  introduce a video mirror, unbounded coroutine-per-send, polling loop, browser-side JavaScript,
  or global input hook as a shortcut.

## Shared documentation and evidence

| Artifact | Owner slice | Required content |
|---|---:|---|
| docs/FIRE_TV_BROWSER_PLAN.md | All | Architectural source of truth; update only through deliberate design review. |
| docs/fire-tv-browser-implementation/adrs | All | Accepted decision rationale, alternatives, consequences, and change triggers; never a substitute for AGENTS.md or the wire specification. |
| docs/PROTOCOL.md | 02, 03 | Versioning, browser message schema, TLS boundary, direction, sizes, stale-data rules. |
| docs/LATENCY_BUDGET.md | 01 onward | Raw measurement method, device facts, targets distinctly labelled, regression thresholds. |
| docs/ENGINEERING_NOTES.md and docs/INSTALL.md | 01, 08 | Sideload/experimental scope, device support, installation/removal truth. |
| testdata/golden | 02 | Canonical, reviewed Rust-generated binary frames and complete manifest. |
| approved receiver/App snapshots | 05, 06 | Only reviewed visual changes; never automatic approval. |
| Browser test run record | Every hardware slice | Date, build SHA, device, result, defects, raw/attached telemetry location. |

## How to use these files

Work only on the next incomplete slice. At its start, copy its checklist into the implementation PR
or issue and link the failing tests. At its finish, fill its exit-evidence table with commands,
commit SHA, and measurement record. If any exit condition fails, stop and repair it before opening
the next slice.

The files are intentionally implementation-grade. They prescribe behaviour and proof, but they do
not authorize skipping the platform/policy gates or relaxing security to make a demo work.
