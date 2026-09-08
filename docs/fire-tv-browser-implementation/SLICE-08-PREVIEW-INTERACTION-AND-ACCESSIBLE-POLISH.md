# Slice 08 — preview interaction and accessible control-surface polish

**Governing ADRs:** [ADR-0005](adrs/ADR-0005-PROTOCOL-V2-CANONICAL-CONTRACT.md), [ADR-0007](adrs/ADR-0007-SURFACE-LIFECYCLE-AND-TV-FIRST-UX.md), [ADR-0008](adrs/ADR-0008-PORTABLE-INPUT-AND-STALE-GUARDS.md), [ADR-0009](adrs/ADR-0009-OPT-IN-BOUNDED-JPEG-PREVIEW.md), and [ADR-0011](adrs/ADR-0011-TEST-EVIDENCE-AND-LATENCY-GOVERNANCE.md).

## Outcome

The Windows Web page becomes a polished, accessible control surface. When (and only when) the user
has opted into a current viable preview, they may click, drag, and scroll inside the actual displayed
preview image to send bounded native pointer/scroll input to the TV WebView. Every interaction
retains a semantic remote alternative; no one needs a mouse, touch screen, or preview to operate the
browser.

If preview is unsupported on the target, the slice still ships the complete accessible no-preview
layout and clearly labels pointer interaction unavailable. It must not make a fake clickable stage.

## Entry criteria

- Slice 07 passive preview passes its resource/control-priority measurement gate, or has completed
  with a truthful unsupported state.
- Slices 04–06 remote navigation/input/lifecycle are stable without pointer input.
- Slice 02 protocol vectors include pointer reference fields: epoch, current navigation/frame
  reference, action, and fixed normalized coordinates. Changes to this shape reopen vector review
  before UI work.
- A controlled receiver fixture can visibly acknowledge pointer/touch and scroll without exposing
  page DOM to the host.

## User-visible vertical behaviour

1. The page works fully with keyboard and semantic remote controls at every width. Wide layout uses
   status beside the stage; narrow layout stacks status, address, preview, and remote in that order.
2. The preview stage stays 16:9 with letterboxing. The visible image rectangle, not its containing
   control, is the only interactive region. Clicks in black bars/gutters do nothing.
3. When preview is off, stale, unavailable, disconnected, loading a different navigation, or
   hidden, the stage is noninteractive and says “TV display is canonical.” No pointer frame is sent.
4. A valid pointer down starts a primary single-pointer interaction. Move/up use the last active
   pointer; leaving the visible image clamps only during that active drag so the receiver receives a
   matching bounded Up instead of a stuck Down.
5. A pointer input includes the exact preview navigation/frame reference. The receiver accepts it
   only if the reference is still current for the active browser. A stale reference produces a safe
   StalePreviewInput result and no WebView input.
6. Scroll is deliberate, bounded, and relative. Touch pinch/multi-touch, hover, right click,
   clipboard, global input capture, and raw OS key codes are unsupported.
7. The Windows UI exposes names, roles, focus, disabled explanation, high-contrast/theme tokens,
   keyboard operation, error recovery, privacy state, and status without using colour alone.

## Scope

### In scope

- Pure PreviewInputMapper/geometry model, pointer/scroll wire value validation, current-frame guard,
  receiver native touch/scroll BrowserPort dispatch, and state acknowledgement.
- Windows preview-stage input state machine: passive/interactive/stale/dragging/error; actual image
  rectangle measurement; DPI/layout change invalidation; focus/capture cleanup.
- Complete BrowserPage visual layout and responsive states: unavailable, verify secure receiver,
  secure-ready, loading, ready/no-preview, ready/passive-preview, interactive-preview, blocked,
  page error, controller disconnected, data-clear/dialog pending, and unsupported preview.
- Avalonia Headless semantics/keyboard/navigation, snapshots at 100/125/150% scale, narrow/wide
  layouts, theme/contrast review, privacy disclosure, and no-colour-only signals.
- Receiver controlled pointer/scroll fixture instrumentation and Fire TV interaction/latency
  measurement.
- Guarded unsupported-preview fallback UI that leaves remote buttons fully usable.

### Explicitly out of scope

- Multi-touch/pinch zoom, free-form mouse hover/right-click, arbitrary remote desktop control,
  accessibility-service injection, synthetic system-wide touch, JavaScript pointer injection,
  MediaProjection/screen recording, video preview, tab switching, upload/download, permissions,
  or data persistence.
- Removing semantic remote controls because preview “looks easier.”
- Changing preview rate/resolution/queue bounds merely to accommodate interaction; those remain
  Slice 07's measured hard envelope.

## Coordinate and input contract

### Host geometry

The mapper receives only:

- current preview metadata: epoch, navigation ID, frame ID, source image width/height;
- actual rendered image rectangle in device-independent coordinates after aspect-ratio/letterbox
  layout;
- pointer position in the same local coordinate system, action, and active drag state;
- current BrowserSession/preview freshness state.

It does **not** use the control's outer bounds, Windows screen position, desktop DPI, or a guessed TV
resolution.

For an initial Down, coordinates must be finite and inside the actual image rectangle. Map:

    normalizedX = round(clamp((x - imageLeft) / imageWidth, 0, 1) * 65535)
    normalizedY = round(clamp((y - imageTop)  / imageHeight, 0, 1) * 65535)

The wire carries integers in 0–65535, never floating point. A zero/negative/non-finite image
rectangle or stale preview rejects input. During an active drag only, Move and Up clamp to the image
edge; all other out-of-rect input is ignored. Cancellation always sends/locally resolves a matching
bounded cancel path so the receiver cannot remain pressed.

### Receiver validation

The receiver validates secure session, epoch, strictly next input sequence, matching current
navigation/frame reference, primary-pointer state, action ordering, and normalized bounds before it
constructs a native MotionEvent-equivalent dispatch through BrowserPort. It does not infer
coordinates from a stale image or run script in the page.

Scroll carries bounded signed logical deltas, not raw host wheel hardware values. BrowserPort maps
those against the current receiver viewport in a documented deterministic way. A scroll with stale
reference/current-state failure is ignored safely.

## Planned files and boundaries

| Area | Planned change |
|---|---|
| src/Flint.Session | Pointer/scroll input builders, preview-current guard, acknowledgements, no raw host key path. |
| src/Flint.App | PreviewInputMapper, stage interaction state machine, responsive BrowserPage, accessibility/visual polish, fake-image snapshot fixtures. |
| receiver/browser | Pointer/scroll validation and native BrowserPort dispatch; no JavaScript/accessibility-service bridge. |
| receiver UI/activity | Touch/scroll event ownership and cancellation on surface/session/lifecycle changes. |
| protocol/vector tests | Pointer reference and normalized-coordinate exact vectors, negative bounds/action-order cases. |
| tests | Mapper/property/fuzz, session order, receiver fake port, controlled fixture instrumentation, Headless/snapshot/a11y, hardware interaction measurement. |

## Test-first implementation order

| Step | Red test to add first | Minimum production change | Required green proof |
|---:|---|---|---|
| 1 | PreviewInputMapperTests fail for exact corners, letterbox, DPI, resized stage, zero rect, NaN/infinity, and outside-region click. | Pure geometry/normalized-coordinate mapper. | 100% line/branch; property/fuzz proves emitted integers remain 0–65535 and no outer-bound mapping occurs. |
| 2 | PreviewDragStateTests show Move/Up without Down, lost capture, or out-of-rect drag leaves stuck state. | Small immutable drag state machine. | Table tests cover Down/Move/Up/Cancel, stage resize, preview replacement, disconnect, and surface change. |
| 3 | BrowserPointerProtocolTests show missing/stale navigation/frame reference or invalid action/order reaches driver. | Strict input model/receiver validator and vector additions if needed. | Rust/C#/Kotlin canonical vectors plus malformed/stale/property/mutation suite pass. |
| 4 | BrowserNativePointerDispatchTests show driver uses JS/accessibility or dispatches after close. | Native BrowserPort pointer/scroll adapter with UI-thread guard. | Recording fake covers every order/error; source guard forbids bridge/eval APIs. |
| 5 | BrowserPointerFixtureInstrumentationTest fails controlled tap/drag/scroll visible marker. | Owned fixture plus Espresso-Web/UiAutomator proof. | Reference matches current frame, stale action does nothing, and semantic buttons still work. |
| 6 | BrowserPageLayoutTests show wide/narrow order, preview gutter click, focus trap, or disabled explanation wrong. | Responsive page/layout and stage input binding. | Avalonia Headless verifies semantic roles, keyboard navigation, focus order, and no preview-required path. |
| 7 | BrowserPageSnapshotTests fail visual states/scales/themes. | Reviewable fake-image fixtures and approved snapshots. | 100/125/150%, narrow/wide, light/dark/high-contrast token review is complete. |
| 8 | BrowserInteractionHardwareTest measures preview interaction against remote controls. | Explicit Fire TV test scenario/trace record. | Pointer/scroll is accurate enough on fixture, no stale action lands, and control priority stays bounded. |

### Required geometry and interaction corpus

- Image 16:9 inside larger width/height containers, pillarbox, letterbox, exact edges/corners,
  fractional DIP values, 100/125/150% scale, layout reflow during drag, zero/negative dimensions,
  null/no frame, stale frame/epoch/navigation, non-finite pointer/rect, and values one unit inside
  versus outside every edge.
- Down inside/outside, Move/Up with and without active pointer, double Down, duplicate sequence,
  reversed action order, cancel, focus loss, preview toggle, browser close, disconnect, new
  navigation, page resize, host receive delay, and receiver current-frame mismatch.
- Small/large bounded scroll deltas, reversed horizontal/vertical axes, stale scroll, no preview,
  no viewport, and rapid repeated scroll under writer pressure.
- Every pointer/scroll condition with preview supported and unsupported. Unsupported means no input
  wire frame, not an invisible best-effort attempt.

## Accessibility and visual quality gate

The BrowserPage must be both attractive and operable without a preview. Before this slice exits:

- Every control has a stable accessible name, role, tooltip/help text where needed, visible keyboard
  focus, logical tab order, and an enabled/disabled explanation available to assistive technology.
- Address, Go, remote keys, Send text, preview toggle, Close, clear-data, dialog response, trust
  decision, and recovery controls all work from keyboard/Headless tests.
- Status uses text/icon/semantics as well as colour; errors identify which receiver/browser action
  failed and an actionable recovery. Sensitive inputs/URLs are not echoed in diagnostic strings.
- Narrow layout preserves the operational order: status, address/navigation, preview, remote/text,
  destructive/recovery actions. Wide layout retains a visible status panel and 16:9 stage.
- Snapshots use deterministic fake preview pixels and approved assets; no real website screenshot is
  committed. Scale/theme changes require human image review.
- Preview privacy disclosure is discoverable and revocable. Turning it off is never hidden behind
  an overflow menu.

## Full-suite checkpoint

Run focused mapper/interaction/UI tests, then:

~~~powershell
.\scripts\build.ps1
.\gradlew.bat --no-daemon :protocol:check :receiver:testDebugUnitTest :receiver:lintDebug --console plain
python scripts/check-site.py
~~~

Run controlled fixture instrumentation and the selected Fire TV interaction measurement separately.
Do not use a live website as a deterministic pointer-test target.

## Latency/resource checkpoint

- Compare semantic remote action, passive preview enabled action, and pointer/scroll action under
  equivalent target conditions. The TV marker/camera measurement remains the source for physical
  end-to-end input response; preview arrival is recorded separately.
- Trace host mapper event, session enqueue/write, receiver validation, native dispatch, fixture
  marker/state, and optional preview feedback with command/input IDs and current frame reference.
- Record p50/p95/p99/max, rejected stale input, drag cancels, queue high-water, mailbox replacement,
  CPU/GC/heap/native heap/thermal/FD/thread data. A pointer action that waits behind preview or
  accepts a stale frame is an automatic failure regardless of average latency.
- Any acceptable comparable regression threshold is inherited from Slice 07's reviewed baseline and
  measurement noise. Do not publish an unmeasured millisecond figure.
- If pointer mapping is inaccurate or expensive, disable only interactive preview and retain the
  proven passive/no-preview accessible remote.

## Exit evidence

| Evidence | Required result |
|---|---|
| Mapping correctness | Actual drawn image rectangle maps accurately to bounded normalized coordinates across DPI/letterbox/drag cases. |
| Stale safety | Current epoch/navigation/frame reference is enforced; stale preview cannot send a pointer/scroll action. |
| Native input | Receiver dispatch is native/UI-thread-only and contains no JS, bridge, accessibility-service, or raw system input path. |
| UX/accessibility | All operational states pass Headless, focus, semantic, keyboard, contrast/theme, and reviewed snapshot gates. |
| Fallback | Preview unavailable/off state remains polished and fully remote-operable. |
| Performance | Target fixture evidence shows pointer/scroll does not break control priority/resource bounds; failures are recorded. |

## Rollback and stop conditions

Interactive preview is a separate sub-capability from passive preview. Disable it while retaining
the passive preview or no-preview browser if any geometry, stale-frame, native-dispatch,
accessibility, or performance proof fails.

Stop if current-frame validation cannot make preview-based interaction safe, if pointer accuracy is
not credible on physical hardware, if input requires a forbidden bridge/service, if UI becomes less
accessible without a mouse, or if interaction creates queue/resource regressions. Do not broaden to
a remote desktop as a fallback.
