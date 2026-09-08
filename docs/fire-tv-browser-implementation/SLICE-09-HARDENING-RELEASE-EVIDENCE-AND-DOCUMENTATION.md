# Slice 09 — hardening, release evidence, and documentation

**Governing ADRs:** the complete [ADR index](adrs/README.md).

## Outcome

Flint has an evidence-backed go/no-go decision for the Fire TV-resident browser experiment. The
decision is based on reproducible automated suites, physical-device soak/latency evidence, security
review, compatibility observations, coverage enforcement, rollback practice, and truthful user
documentation. A green build alone is not a release decision.

Absent written Amazon approval, the outcome may be **developer/sideload experiment only** even if all
technical gates pass. That is a valid successful result.

## Entry criteria

- Every prior slice either passes with its exit evidence or is explicitly unavailable with a safe
  state-only fallback. No open “we will test this later” security/performance item remains.
- The device matrix identifies actual Android Fire OS targets and explicitly excludes Vega.
- Browser architecture and ADRs are reviewed; any late design change has its own ADR/vector/snapshot
  review before this slice starts.
- The current branch contains no unreviewed golden vectors, auto-approved snapshots, diagnostic
  captures, certificate/private key material, user text, browsing pixels, or credentials.

## Release scopes

| Scope | What may be claimed | Mandatory evidence |
|---|---|---|
| Unsupported | Browser unavailable on this receiver/model/version. | Exact capability reason/remedy, hardware-free regression suite. |
| State-only experiment | Secure navigation/remote works; preview or interaction unavailable. | Slices 01–06 evidence, clear feature matrix, no fake preview controls. |
| Preview experiment | Passive/interactive preview passed target gates on named models only. | Slices 07/08 measured target records and privacy/queue proof. |
| Developer/sideload release | Technically accepted feature delivered through user-authorized receiver installation. | All technical/security/docs gates, but no Appstore claim. |
| Public Appstore release | Only after a separate written Amazon approval and review. | All above plus recorded policy approval; no assumption that embedded WebView avoids browser policy. |

The release notes must state the exact scope rather than “browser support” as a blanket claim.

## Scope

### In scope

- Make full quality gates deterministic and enforce the new-code coverage contract rather than
  merely generating reports.
- Run clean-machine hardware-free validation, cross-language vector review, fuzz/mutation/static
  policy checks, full UI/snapshot/accessibility suite, secure transport chaos tests, and physical
  receiver acceptance.
- Run named target-device soak/latency/resource sessions with preview OFF and, where available,
  passive/interactive preview ON.
- Maintain a compatibility observation matrix for representative public sites without publishing
  blanket compatibility assertions.
- Audit packet/log/data retention/privacy/security semantics and documented installation/removal/
  recovery/rollback paths.
- Update docs, site, UI copy, capability matrix, ADR index, CI scripts, release notes, and support
  material.
- Produce a concise go/no-go record signed by the responsible reviewer(s).

### Explicitly out of scope

- Using CI/emulator results as a substitute for real Fire TV evidence.
- Quietly relaxing a test threshold, allowing a snapshot/vector automatically, excluding new domain
  code from coverage, skipping Gradle because the root script is green, or calling unmeasured
  targets measured latency.
- Claiming general website/video/DRM compatibility, Appstore distribution, encryption of the legacy
  channel, or browser support for Vega/untested devices.
- “Fixing” a release problem by switching to an unreviewed browser engine, proxy, cloud relay,
  capture service, raw input hook, or broader permissions.

## Quality enforcement work

The repository's current checks are valuable but incomplete for this feature. This slice must close
the following gaps with code/scripts/CI configuration and tests that prove the gates execute:

| Current condition | Required final gate |
|---|---|
| scripts/build.ps1 runs Rust + .NET but not Gradle. | Browser quality command or build script invokes Rust/.NET plus protocol check and receiver unit/lint checks; CI uses the same deterministic command. |
| CI runs receiver JVM tests but does not necessarily run protocol check. | CI explicitly invokes protocol check so Kotlin protocol JaCoCo gate and browser vectors run. |
| .NET emits Coverlet reports without a threshold. | Add scoped threshold enforcement for browser-owned C#/protocol/session/app classes; explicit narrow exclusions only for generated/platform glue. |
| Rust has no claimed browser coverage gate. | Pin a reproducible Rust coverage/report and mutation workflow for browser-owned codec/domain code; do not claim it until it runs in CI/record. |
| Receiver browser pure code has no project-wide threshold. | Add receiver/JVM coverage reporting or a scoped equivalent proving 100% line/branch for new pure browser packages. |
| Android/Chromium cannot honestly be 100% covered. | Keep narrow exclusions documented; require instrumentation plus named Fire TV evidence for platform/provider branches. |
| Vectors/snapshots can be regenerated deliberately. | Generation stays manual/explicit; CI checks completeness/approval and rejects unreviewed changes. |

The build may enforce coverage by changed/browser-owned module rather than fabricating 100% over
legacy code. Every exclusion needs a reason, owner, test substitute, and expiry/review condition.

## Security and static-policy audit

Before release, create tests/scripts that fail if production browser code introduces a forbidden path.
The guard must distinguish source comments/test fakes from executable production calls and must be
reviewed with the security owner.

| Invariant | Audit evidence |
|---|---|
| No native JavaScript bridge/evaluation | Static source guard plus code review verifies no addJavascriptInterface, evaluateJavascript, WebMessagePort, WebMessageListener, or injected script in production browser path. |
| No unsafe content escape | Instrumentation proves scheme/file/content/mixed-content/SSL/popup/chooser/download/permission/external-intent denial; source guard checks default callbacks do not delegate. |
| No plaintext browser data | Loopback and target capture show URL/text/dialog/preview only on BrowserSession TLS; legacy listener rejects browser frames. |
| No permissive trust override | Tests and source audit prove full pin mismatch has no click-through/trust-all callback; pair code is not sent pre-pin. |
| No secret/pixel retention | Log/telemetry/snapshot/cache/clipboard/crash-report audit and tests show no input, cookie, page body, preview image, private key, or pairing code retention. |
| No unsafe input injection | Protocol/static/instrumentation tests prove only semantic/native input; no global hook, accessibility-service, raw virtual key, or JS route. |
| Bounded resource paths | Virtual-clock/queue/soak tests show capacity limits, cancellation, disposal, and no legacy per-preview send path. |

## Required test matrix

### Clean hardware-free run

Run on a fresh Windows machine without a Fire TV/GPU and retain the command log:

~~~powershell
.\scripts\build.ps1
.\gradlew.bat --no-daemon :protocol:check :receiver:testDebugUnitTest :receiver:lintDebug --console plain
python scripts/check-site.py
~~~

Then run the browser-specific deterministic command added by this slice. It must aggregate:
Rust format/Clippy/tests/coverage/mutation as configured; .NET build/tests/coverage threshold;
Kotlin protocol coverage/unit/lint; receiver pure/JVM tests; cross-language vectors; deterministic
fuzz corpus; Headless/accessibility/snapshots; loopback TLS/chaos tests; static policy guards; and
documentation/link checks.

A no-hardware run must mark TV tests skipped, not failed or silently omitted.

### Target-device acceptance

For each named supported model/API/provider, use the explicit serial/acknowledgement runner. At a
minimum run:

1. installation/launch/removal path with user-visible authorization;
2. capability probe/fixture/controlled HTTPS;
3. first pin pairing, reconnect, mismatch rejection, endpoint/network restart, host crash;
4. navigation, D-pad, semantic remote, Unicode form text, dialog, clear data, pause/resume,
   browser/media/mirror transition, and receiver close/reopen;
5. preview OFF 30-minute warm soak; passive preview ON 30-minute soak if supported; interactive
   preview ON 30-minute soak if supported;
6. Wi-Fi loss/recovery if user authorizes it, plus slow reader/writer pressure where safe;
7. final cleanup/removal/recovery verification.

Do not bundle hardware commands in CI or run them against an unspecified ADB target.

### Compatibility observations

Use a small recorded matrix, not an implicit promise:

| Class | Example observation | Required wording |
|---|---|---|
| Static HTTPS | Title/history/basic navigation works or fails on named target. | “Observed on [model/build/date], not a general guarantee.” |
| Form/first-party cookie | Controlled or public non-sensitive form behaviour. | No credentials retained in evidence. |
| Redirect/JS-heavy page | Navigation/runtime compatibility. | Failure reports safe class, not page content. |
| Popup/upload/permission | Deliberate denial. | Documented unsupported by design. |
| Video/DRM | Expected unsupported/variable state. | Never market as streaming-browser support. |
| Preview | Passive/interactive viable or unavailable on exact target. | Never imply receiver rendering occurs on Windows. |

## TDD and verification order

| Step | Red test/evidence first | Minimum change | Exit proof |
|---:|---|---|---|
| 1 | BuildGateTests or script tests demonstrate Gradle/coverage/static checks can be omitted. | Add one deterministic browser quality entry point and CI call. | Deliberately broken fixture/check proves each gate fails CI locally. |
| 2 | Coverage configuration tests/report review show new browser classes can be excluded/under threshold. | Add scoped thresholds/reports and narrow documented exclusions. | Clean run fails when synthetic uncovered branch is introduced. |
| 3 | Static-policy guard tests show forbidden API/log/retention pattern escapes. | Add guarded source/behaviour checks. | Positive fixtures fail; production path passes with reviewed allow-list. |
| 4 | Chaos tests show slow writer, malformed peer, reconnect race, or cancellation can regress unnoticed. | Add deterministic fake clock/peer scenarios. | All paths leave bounded state and no leaked worker/session. |
| 5 | Documentation/capability matrix tests show stale/overclaiming text. | Update docs/site/UI/release templates/ADRs. | Link/text consistency review passes; no public/low-latency/universal claim unsupported by evidence. |
| 6 | Hardware script record has missing context/raw sample/cleanup. | Add schema validation/report tool. | Selected device record is complete or explicitly unavailable. |
| 7 | Go/no-go template leaves an unowned risk. | Add final reviewer checklist and decision file. | Every gate is pass/fail/not-applicable with owner, date, SHA, and linked evidence. |

## Latency/resource release gate

Use the measured baseline method established in earlier slices. The release record must include:

- exact device model, Fire OS/API, receiver/WebView provider evidence, host hardware/build, display
  mode, network topology/quality label, thermal state, commit SHA, test fixture, sample count,
  warm-up policy, and comparable/non-comparable classification;
- p50/p95/p99/max for host action-to-TVT marker, host action-to-state acknowledgement, receiver
  local command/input processing spans, and preview capture/encode/mailbox/decode/present spans;
- queue high-water marks, replacement/drop counts, encoded bytes/frame, memory/native heap, GC
  count/pause, CPU/thermal, threads, file descriptors, error/reconnect count;
- separate preview OFF, passive ON, and interactive ON comparisons where supported;
- raw redacted telemetry location and a human-readable summary in docs/LATENCY_BUDGET.md.

A session fails immediately on a queue-capacity breach, control/state starvation, stale preview action
accepted, unbounded resource trend, leaked secret/pixel, or a comparable control-path regression
beyond the previously reviewed noise-aware threshold. Do not average away a stall. If conditions are
not comparable, record that fact and rerun; it is not a pass.

## Documentation deliverables

Update and cross-link:

- README.md, AGENTS.md, docs/ENGINEERING_NOTES.md, docs/INSTALL.md, docs/PROTOCOL.md,
  docs/LATENCY_BUDGET.md, the site, diagnostics, receiver/Windows UI strings, release notes, and
  the supported-device/capability matrix;
- this implementation directory and its ADR index;
- exact protocol version and secure-browser endpoint wording;
- experimental/sideload/Appstore policy status;
- browser network ownership (TV direct internet), TLS scope, preview consent/persistence,
  unsupported URLs/features/DRM, tested models, known limitations, install/remove/recovery process,
  and rollback/disable instructions.

Every claim about security, device support, compatibility, or latency must link to observed evidence
or say “target,” “experimental,” “unavailable,” or “not measured” as appropriate.

## Go/no-go record

The final record has one row per gate:

| Gate | Result | Evidence | Owner/date/SHA | Release effect |
|---|---|---|---|---|
| Protocol/vector compatibility | Pass/Fail | Canonical corpus + consumers | Required | Fail blocks all browser release. |
| TLS/trust/plain rejection | Pass/Fail | Loopback + target capture | Required | Fail blocks all browser release. |
| WebView security/TV UX | Pass/Fail | Instrumentation + hardware | Required | Fail blocks all browser release. |
| Input/lifecycle/data privacy | Pass/Fail | Test/soak evidence | Required | Fail blocks all browser release. |
| Preview | Pass/Fail/Unavailable | Target measurements | Required | Failure removes preview only if state-only browser remains safe. |
| Coverage/static policy | Pass/Fail | CI artifacts | Required | Fail blocks selected scope. |
| Docs/policy/distribution | Pass/Fail | Review/approval evidence | Required | Lack of Appstore approval limits scope to developer/sideload. |
| Rollback rehearsal | Pass/Fail | Disable/remove/recover record | Required | Fail blocks release. |

## Exit evidence

| Evidence | Required result |
|---|---|
| Deterministic quality | Fresh no-hardware run executes and passes every configured Rust/.NET/Kotlin/receiver/vector/fuzz/static/UI/docs gate. |
| Full coverage contract | All new Flint-owned pure/domain/protocol/session/VM branches meet scoped 100% line/branch plus property/mutation proof; exclusions are honest. |
| Hardware proof | Named receiver models have complete explicit run/soak/latency records or are clearly unsupported. |
| Security/privacy | TLS/pin/input/WebView/retention audits pass; no legacy-channel or secret/pixel leak remains. |
| UX | Windows and TV state/accessibility/D-pad/recovery evidence is reviewed at all supported scopes. |
| Documentation | Product/docs/site/ADRs/release notes reflect only measured/supported/approved scope. |
| Policy | Public Appstore work is blocked without written approval; developer/sideload label is visible. |
| Rollback | Browser, preview, and interactive-preview kill switches/recovery/removal have been rehearsed. |

## Rollback and stop conditions

Release components independently: secure browser, remote input, passive preview, interactive preview.
Disable the smallest unsafe component and present an accurate unavailable reason. Never leave an
active-looking control that cannot complete.

Stop/go-no-go is Fail when any security, lifecycle, D-pad, bounded-resource, policy-truthfulness,
or core control gate fails. A preview-only failure can produce a state-only experiment only after
the full no-preview safety suite remains green. No failure authorizes a change in architecture,
distribution claim, or privilege without a new ADR and review.
