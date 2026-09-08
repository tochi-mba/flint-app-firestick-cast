# Fire TV browser — implementation progress

Two passes are recorded here. The first delivered the browser as a *transport*: TLS session, URL
policy, WebView driver, input routing, dialogs, preview plumbing. The second turned it into
something usable from a sofa with only a remote, and gave the desktop a cockpit to match.

Governing plan: `C:\Users\tochi\.claude\plans\so-i-impleme-ted-parallel-hare.md`.
Governing decisions: [ADR index](adrs/README.md).

> The statement below covers those original two passes. The later independent-page workspace and
> profile VPN expansion is still under active verification; see
> [WORKSPACE-AUDIT-2026-09-07.md](WORKSPACE-AUDIT-2026-09-07.md). It must not be described as
> complete until its software gates and listed physical Fire TV checks pass.

## Where it stands

**Everything in the plan is implemented and green in software**, and it has now been
**run on a real Fire TV Stick 4K** (API 25, Chromium 118): the pinned TLS
session, page load, and TV-local cursor all work end to end. That run found two defects — a CLI that
could open the browser only once per receiver launch, and an idle session dying after 120 s — both
fixed with tests. Details in `docs/LATENCY_BUDGET.md`.

What remains is **measurement**: that run proved the feature works, not how fast it is.

| Suite | Result |
|---|---|
| Rust (`cargo test --all-targets`) | 210 passed, 29 ignored (hardware) |
| Rust golden + `browser_wire` | 11 + 5 passed |
| Kotlin `:protocol:test` | green |
| Kotlin `:receiver:testDebugUnitTest` | 382 passed |
| Kotlin `:receiver:lintDebug` | clean |
| .NET (`dotnet test Flint.slnx`) | 1,034 passed across 8 assemblies |
| Golden corpus | 75 committed vectors, byte-identical across Rust / C# / Kotlin |

## Pass 2 — what changed

### Defects fixed

| Defect | Cause |
|---|---|
| **Fullscreen did nothing** | `onShowCustomView`/`onHideCustomView` were never implemented, and the surface handed Compose a bare `WebView` with nowhere to put a fullscreen view |
| **Back destroyed the session** | Back reached the media key handler, which tears down any non-idle surface |
| **History buttons were inert** | `BACK/FORWARD/RELOAD/STOP` arrived on the wire and were logged and dropped; `BrowserPort.goBack` and friends were unreachable |
| **Nothing could be searched** | `BrowserUrlPolicy` rejects a bare word twice over, and no layer above it decided address-or-search |
| **Blocked links were silent** | Downloads, uploads, popups, permissions and non-https links were refused with no feedback at all |
| **Every load error read the same** | All main-frame errors collapsed to `DRIVER_FAILURE` |
| **Preview state was a lie** | Outbound state hardcoded `previewState = DISABLED` while sending frames |
| **TLS tests failed only in the full suite** | Robolectric's reflective instrumentation needs JDK 17 module opens; without `java.net`, JSSE could not resolve a peer and a correct handshake failed |

### Built

**Television.** Omnibar (history, merged reload/stop, security chip with the registrable domain
emphasised, progress), full-screen omnibox with a wrap-around D-pad keyboard and one-shot shift,
address-or-search resolution above the unchanged URL policy, first-run engine choice, an
accelerating cursor with edge auto-scroll, a focus mode on long-press Select, a nine-rung Back
ladder, tabs (8 max, 2 live, rest frozen to saved state), tab strip and switcher, new-tab page,
menu and library sheets, find bar, error page, notices, zoom / user-agent / dark mode, favicons, a
bounded bookmark and history store, and a design-token layer with one focus visual.

**Protocol.** Ids 21–27 for tabs, view settings, favicons and the library — additive, never new
fields, since every browser decoder asserts no trailing bytes. Capability is *discovered* from
received snapshots rather than declared, because a receiver that does not know a type id closes the
session rather than ignoring it.

**Windows.** Cockpit view with tabs, find, view controls, an opt-in preview pane, native dialog
answering, and a destructive clear-data confirmation. Each panel reports itself unavailable until
the receiver proves the family, rather than showing controls that would end the session.

### Structure

`ReceiverService` 1099 → 877 lines via `ReceiverBrowserController`; `BrowserPageViewModel` split
across five cockpit view models. No file exceeds 1,000 lines.

## Not done — and why

| Item | Status |
|---|---|
| Timing evidence | The feature now **runs on a real Fire TV Stick 4K** (see `docs/LATENCY_BUDGET.md`), but nothing was instrumented or sampled. Every span in the budget is still a target |
| Tab RAM budget | One live renderer measured at ~46–53 MB on a 2 GB stick, so two extrapolates to ~160–175 MB — inside the 220 MB gate, but **the two-tab figure is arithmetic, not a measurement** |
| Second Select press | Activating a focused control needs two Select presses; the first after a focus move does not fire. Reproduced on hardware, **not yet diagnosed** — and the reason the multi-tab memory reading is incomplete |
| Preview frame reference | The receiver still compares pointer input against the navigation id, because the Windows touchpad sends that in both reference fields. Changing one side alone would reject every pointer event that works today; the two move together when the preview pane is exercised on hardware |
| Appstore | Sideload/developer only, pending written Amazon approval |

## Commands

~~~powershell
.\scripts\build.ps1                    # Rust + .NET, the canonical gate
$env:JAVA_HOME = "<jdk17>"
.\gradlew.bat --no-daemon :protocol:test :receiver:testDebugUnitTest :receiver:lintDebug --console plain

# Deliberate, reviewed regeneration only:
cd flint-engine; cargo test --test golden -- --ignored regenerate
$env:FLINT_UPDATE_SNAPSHOTS = 1        # then review every changed PNG as a diff
~~~
