# Slice 07 — opt-in passive preview and latency tuning

**Governing ADRs:** [ADR-0001](adrs/ADR-0001-TV-RESIDENT-BROWSER-OWNERSHIP.md), [ADR-0003](adrs/ADR-0003-SEPARATE-PINNED-TLS-BROWSER-SESSION.md), [ADR-0005](adrs/ADR-0005-PROTOCOL-V2-CANONICAL-CONTRACT.md), [ADR-0009](adrs/ADR-0009-OPT-IN-BOUNDED-JPEG-PREVIEW.md), and [ADR-0011](adrs/ADR-0011-TEST-EVIDENCE-AND-LATENCY-GOVERNANCE.md).

## Outcome

A user may explicitly enable a **passive** bounded preview of the receiver WebView in the Windows
browser page. The TV remains the canonical display and all browser controls remain usable without
the preview. The preview is memory-only, latest-frame-only JPEG feedback over BrowserSession; it
never shares the legacy receiver send path, never persists pixels, and never delays command/state
traffic.

If Slice 01's real device evidence says WebView capture is unavailable or too expensive, this slice
completes by implementing the truthful unsupported-preview state and its tests. It must not replace
the design with MediaProjection, Accessibility capture, a screen recorder, or an unbounded video
mirror.

## Entry criteria

- Slices 01–06 are green, including the no-preview 30-minute soak and control/input baseline.
- The selected target has a dated capture-candidate result marked viable. “Inconclusive” requires
  further evidence before implementation; “unavailable” selects the state-only outcome.
- BrowserTlsServer has the dedicated bounded writer from Slice 03, and no preview byte can reach
  ReceiverServer.send or the ordinary CastSession.
- The product has a reviewed one-session preview privacy disclosure with default OFF state.

## User-visible vertical behaviour

1. The Windows Web page labels the TV as canonical and initially shows **Show TV preview** with a
   concise privacy disclosure: enabling sends recent receiver browser images to this paired Windows
   device during this session only; frames are not saved by Flint.
2. After consent, the Windows page shows a letterboxed 16:9 passive image and current preview
   health (on/off, frame rate, dropped/replaced count, unsupported reason). It must not accept
   clicks, drags, scrolls, or keyboard page input in this slice.
3. Turning preview off, closing browser, losing secure session, changing surface, hiding the page,
   erroring the WebView, or beginning data clear immediately stops capture, clears queues, disposes
   displayed/stale images, and returns to the canonical-TV overlay.
4. The receiver publishes a preview only when its secure BrowserSession is current, the browser
   epoch/navigation is current, privacy is enabled, and scheduler/resource gates allow it.
5. A slow host/decoder only sees newer images or no images. It never creates a growing backlog,
   blocks TV render, or causes control/state starvation.
6. If preview violates a hard safety/resource gate, it turns itself off with a precise safe error;
   browsing and remote controls continue without it.

## Hard operating envelope

These are hard engineering bounds, not an advertised latency result:

| Resource | Bound |
|---|---|
| Opt-in/persistence | Off by default; one-session consent; memory only; no disk/cache/telemetry pixels. |
| Frame representation | JPEG only for v1 preview; no H.264/video feedback path. |
| Maximum image | 960x540 logical output and no more than 518,400 pixels. |
| Maximum wire payload | 768 KiB including only the defined frame bytes; reject one byte over before decode. |
| Scheduler | At most 5 capture attempts per second while interactive; at most 1 while idle. |
| Receiver candidates | One capture in progress and one candidate slot. A busy slot causes a skip, not a wait/queue. |
| Encode/output | One encoder worker/borrowed buffer path and one latest-only outbound preview cell. |
| Host decode | One latest-only decode job and one displayed frame. A newer valid frame supersedes/disposes older work. |
| Ordering | Epoch + navigation ID + strictly increasing frame ID required; stale/out-of-order frame is discarded. |
| Priority | TLS control/state/pairing/close frames drain before at most one preview frame. |
| Quality control | Start with a reviewed fixed JPEG-quality setting. If encoded bytes exceed cap, drop and count the frame; do not repeatedly recompress on the capture tick. |

No implementation may relax one bound to compensate for another without a new measured design
review. A lower frame rate/resolution is the first safe response to a device limit; adding queues,
additional workers, or a mirror protocol is not.

## Receiver pipeline

The receiver pipeline has explicit ownership and no unbounded handoff:

    UI-thread WebView draw/capture
        -> capacity-one capture candidate (or drop)
        -> single bounded JPEG worker/pool
        -> capacity-one BrowserTlsServer preview mailbox (replace old)
        -> TLS writer after all reliable control/state

Rules:

- The UI thread starts no new capture while one capture/hand-off is pending. It checks session,
  epoch, consent, rate, and page/surface state before draw.
- Capture uses the platform-approved mechanism proven in Slice 01. Do not add a fallback capture
  mechanism at runtime.
- Bitmap/buffer ownership is explicit. The worker returns/disposes every candidate exactly once on
  success, cap rejection, cancellation, stale epoch, or exception.
- JPEG output validates magic/type/dimensions/size before entering the mailbox. A full mailbox is
  replaced atomically; replacement is counted and old bytes are released.
- BrowserTlsServer writer prioritizes reliable frames and reads at most one preview candidate after
  a control/state drain. It never launches one coroutine/task per preview message.
- Stop/cancel joins or invalidates outstanding work without waiting unboundedly on UI/network
  thread. Completion from an old epoch is discarded.

## Windows pipeline

    BrowserSession read loop
        -> envelope/type/epoch/frame/size validation
        -> capacity-one background decode slot
        -> final UI-thread image swap
        -> immediate stale-image disposal

Rules:

- Validate declared wire length, JPEG format, width, height, pixel count, frame ID, epoch, and
  navigation ID before image decoding. Reject decompression-bomb-shaped input before allocation.
- Decode never runs on the TLS receive loop or Avalonia UI thread. UI receives only a final current
  image swap and disposes the old image immediately.
- Do not cache/serialize/copy preview images for diagnostics, crash reports, clipboard, recents,
  snapshots, or tests. UI snapshots use fake generated image fixtures, not real browsing pixels.
- Preview control has a visible status and a non-visual semantic label. The image is passive until
  Slice 08 and should be hit-test disabled or otherwise unable to produce browser input.

## Planned files and boundaries

| Area | Planned change |
|---|---|
| receiver/browser | PreviewPolicy/scheduler, capture adapter, buffer/bitmap ownership, JPEG worker, counters, stop/cleanup integration. |
| receiver secure transport | Dedicated preview mailbox in BrowserTlsServer writer; reliable control/state priority tests; no legacy-server change. |
| receiver UI/state | Consent/state/error/metric model; no pixels in long-lived state; TV tells user preview is a host-side opt-in. |
| src/Flint.Session | Preview envelope validation, epoch/frame reducer, bounded background decoder coordinator, disposal. |
| src/Flint.App | Passive preview stage, consent/privacy text, status/unsupported state, disabled pointer interaction, snapshot fake. |
| tests | Virtual-clock scheduler, pool/mailbox, malformed frame/decode limit, ordering/disposal, UI/Headless snapshots, loopback priority, instrumentation/hardware capture measurement. |

## Test-first implementation order

| Step | Red test to add first | Minimum production change | Required green proof |
|---:|---|---|---|
| 1 | BrowserPreviewPolicyTests show capture can occur without consent/current session, exceed rate, or survive close. | Pure consent/session/epoch/interactive-idle virtual-clock scheduler. | 100% line/branch and property tests cover all state/time boundaries. |
| 2 | BrowserPreviewCandidateTests show a busy/stale/cancelled candidate leaks or queues. | Capacity-one candidate owner/pool contract. | Recording pool proves exactly-once return/dispose under all races; mutation tests kill capacity check removal. |
| 3 | BrowserJpegBoundTests show dimensions/bytes/type/quality overflow can enter writer. | Fixed bounds/validation and drop counter. | Exact max/one-over, invalid JPEG, encode exception, and no-recompress-on-tick tests pass. |
| 4 | BrowserPreviewMailboxTests show preview can starve state/control or create multiple sends. | Dedicated capacity-one latest-only mailbox and writer scheduling. | Virtual peer demonstrates control/state ordering under slow writer/preview flood; no per-message coroutine/task. |
| 5 | BrowserPreviewFrameValidationTests show host can allocate/decode stale/oversized/malformed image. | Host pre-decode validator and current-frame reducer. | Fuzz/decompression-bomb corpus, epoch/navigation/frame ordering, and size/pixel caps all pass. |
| 6 | BrowserPreviewDecodeTests show decode runs on receive/UI thread or stale image survives swap. | Capacity-one background decode coordinator and disposal adapter. | Thread-affinity and exactly-once disposal fakes prove correct behaviour. |
| 7 | BrowserPreviewPageTests show default-on, privacy text missing, image accepts input, or no unsupported state. | Passive UI/view-model/semantic state and fake image snapshots. | Avalonia Headless accessibility/focus/snapshot tests at scale pass without real pixels. |
| 8 | BrowserCaptureInstrumentationTest fails nonblank/size/rate/cancel behaviour on owned fixture. | Target capture adapter only if Slice 01 viability proof exists. | Instrumentation checks a controlled page marker, not arbitrary live content. |
| 9 | BrowserPreviewHardwareMeasurementTest runs preview off/on comparable trials. | Opt-in hardware script/trace exporter. | Target evidence proves bounded queues and records control/preview resource deltas. |

### Required overload and ordering matrix

- Preview off/on, active/idle, loading/ready/dialog/data clear/close/disconnect/grace/surface switch,
  new navigation/old navigation callback, rate boundary, frame ID wrap representation, and worker
  cancellation.
- Slow TLS writer, stalled host reader, stalled host decoder, repeated connect/disconnect, receiver
  low-memory simulation, JPEG cap failure, malformed remote preview frame, and UI page hidden.
- Control command and state before/after every preview scenario. Assert control/state order and
  bounded delay independently of whether any preview is delivered.
- One frame, two frames, thousands of offered capture ticks, invalidated epoch after capture,
  replacement before encode, replacement after encode, and clean disposal at every handoff.

## Full-suite checkpoint

Run focused scheduler/queue/decoder tests, then:

~~~powershell
.\scripts\build.ps1
.\gradlew.bat --no-daemon :protocol:check :receiver:testDebugUnitTest :receiver:lintDebug --console plain
python scripts/check-site.py
~~~

Run receiver instrumentation and Fire TV capture/preview measurement only with explicit user
approval and selected serial. Default CI validates fakes/fixtures/limits and cleanly skips TV-only
behaviour.

## Latency and resource checkpoint

Preview is a latency risk, so compare it to the no-preview baseline rather than admiring its image:

1. Use the same receiver, host, build SHA, display mode, fixture, network conditions, thermal
   state, warm-up policy, and sample count as Slice 05/06 where practical. Mark non-comparable
   runs as non-comparable rather than pass.
2. Record per-frame local spans: capture request, draw completion, worker start/finish, mailbox
   replacement/write, host read, validation, decode start/finish, and UI presentation. Do not
   subtract TV and Windows timestamps.
3. Record host command-to-TV fixture marker and host command-to-state acknowledgement with preview
   OFF and ON. Camera-based PC-action-to-TV measurement is the source for physical end-to-end
   comparison; preview frame arrival is a separate feedback metric.
4. Record p50/p95/p99/max, sampled frame rate, encoded bytes, frame/candidate/mailbox/decode
   drops, queue high-water marks, allocations/GC, heap/native heap, CPU, thermal state, thread/FD
   count, and visible rendering defects.
5. Establish a reviewed comparable-regression rule from Slice 01 baselines and measurement noise.
   It must fail on any queue-capacity breach, control/state starvation, monotonic resource growth,
   or measurable control-path regression beyond the approved noise margin. This is a regression
   gate, not a public latency promise.

Optimise in this fixed order: eliminate stale work/backlog; reduce cadence; reduce resolution or
fixed JPEG quality; remove copies/allocator churn; then reconsider architecture only with a new
design review. Never add B-frames, an unbounded reverse video path, or work to the legacy
per-send-coroutine transport.

## Exit evidence

| Evidence | Required result |
|---|---|
| Privacy | Preview is default-off, consented, memory-only, nonpersistent, labelled, and disposable. |
| Bounds | Every receiver/host queue is capacity one where specified; byte/pixel/rate limits reject/replace rather than accumulate. |
| Priority | Control/state/pairing/close traffic demonstrably wins under preview overload. |
| Safety | Malformed/stale/oversized frames never decode or reach UI; images never become browser input in this slice. |
| Physical proof | Target capture viable path and preview off/on resource/control comparison are recorded, or preview is truthfully unavailable. |
| Quality | New policy/scheduler/decoder/ordering code has full owned-code coverage plus property/mutation/fuzz evidence. |

## Rollback and stop conditions

Preview is independently disableable at capability, session, and page level. Turning it off stops
capture and clears memory without closing the browser.

Stop and ship state-only browser control if capture cannot meet fixed bounds, causes control
starvation, makes resource trends grow, leaks pixels, lacks deterministic cleanup, or cannot be
measured reliably on target. Do not substitute a more invasive capture mechanism or silently lower
privacy protections.
