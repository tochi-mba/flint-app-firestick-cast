# ADR-0009 — opt-in bounded JPEG preview

- **Status:** Accepted
- **Implementation:** Planned
- **Date:** 2026-09-04
- **Deciders:** Flint engineering
- **Owners:** Slices 01, 03, 07–09
- **Related:** ADR-0001, ADR-0003, ADR-0005, ADR-0008, ADR-0011

## Context

The product benefits from the Windows app showing what the TV WebView is currently doing, but the
TV—not Windows—is the browser renderer and canonical display. A reverse visual channel introduces
capture, bitmap, JPEG, network, decode, UI, privacy, and scheduling work on constrained Fire OS
hardware. Reusing the video mirror pipeline would create a second streaming system with latency and
resource costs disproportionate to browser control feedback.

Preview pixels may include sensitive page content. They must be treated as transient user data, not
as diagnostics or an asset cache.

## Decision

Implement preview only after physical device capture evidence, and only as an optional bounded
feedback channel:

- preview defaults OFF and requires a clear per-session Windows privacy consent; the TV display is
  explicitly labelled canonical;
- it consists of current receiver WebView JPEG images over the authenticated pinned BrowserSession,
  never the ordinary CastSession or a separate H.264/MediaProjection/accessibility screen path;
- v1 operating bounds are: at most 960x540/518,400 pixels, 768 KiB wire bytes, 5 capture attempts
  per second while interactive, 1 per second idle, one in-flight capture candidate, one encoder
  worker/borrowed-buffer path, one receiver outbound mailbox, one host decode slot, and one displayed
  frame;
- every slot is newest-only: replacement drops/disposes the older value. No stage can queue an
  unbounded backlog;
- reliable pairing/control/state/close frames have absolute priority over at most one preview write;
- frame IDs, epoch, and navigation ID protect host display and later pointer input from stale images;
- preview pixels are memory-only. They are never written to disk, logs, telemetry, clipboard,
  snapshots, recents, crash reports, or test fixtures. Snapshots use synthetic/fake images;
- failure/unavailability turns preview off with a clear reason while browsing and semantic remote
  controls continue. No runtime fallback capture mechanism is used.

## Consequences

### Positive

- The main browser/control path stays TV-local and avoids reverse-video latency/resource complexity.
- Feedback is useful without making a preview a prerequisite for control or D-pad usability.
- Bounded mailboxes provide a strong guarantee that stale visual work cannot crowd out controls.
- The privacy model is clear, revocable, and testable.
- Capture capability may be unavailable without invalidating the rest of the feature.

### Trade-offs

- Preview can be visibly less fluid than a desktop remote display and must not be marketed as one.
- Capture/JPEG/decode work needs real-device measurement and may fail on API/provider/hardware.
- Interaction based on preview must wait for a separate geometry/stale-frame safety slice.
- A selected device can legitimately have state-only browser support.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Reuse H.264 mirroring/reverse video | A second low-latency streaming pipeline is high-risk on target memory/CPU and complicates browser ownership. |
| Capture with MediaProjection or Accessibility | Adds permissions/privacy/platform risk and is not needed if WebView draw capture is unavailable. |
| Persist frames for diagnostics | Leaks sensitive browsing pixels and creates retention/cleanup obligations. |
| Unlimited frame queue to maximize smoothness | Causes stale feedback, control starvation, memory growth, and latency collapse. |
| Always-on preview | Violates privacy expectation and wastes constrained receiver resources. |
| Render preview page in Windows WebView | Violates TV-resident browser ownership and could expose DOM/credentials on host. |

## Invariants and validation

- Virtual-clock tests prove consent/session/epoch/rate gates and capacity-one replacement.
- Pool ownership tests prove exactly-once bitmap/buffer return/disposal under success, exception,
  stale epoch, cancellation, and close.
- Loopback slow-peer tests prove control/state priority and no per-message coroutine/task behaviour.
- Host validation/fuzz tests reject malformed JPEG, declared/wire/pixel bounds, decompression-bomb
  patterns, stale epoch/navigation/frame, and dispose stale images.
- UI tests prove default OFF, privacy wording, no preview pixels in snapshot fixtures, passive
  hit-testing until ADR-0008's conditions are met, and precise unavailable errors.
- Physical target evidence records capture/encode/mailbox/decode/present spans, drop counts,
  allocations/GC, memory, CPU, thermal state, and control-path comparison with preview OFF.

## Revisit criteria

A different preview codec/cadence/resolution/capture method requires a new ADR backed by target
evidence showing no loss of privacy, capacity, control priority, cleanup, or independently measured
control responsiveness. A desire for smoother video is not enough to remove the hard bounds.

## References

- [Slice 07 — passive preview and latency tuning](../SLICE-07-OPT-IN-PASSIVE-PREVIEW-AND-LATENCY-TUNING.md)
- [Slice 08 — interactive preview](../SLICE-08-PREVIEW-INTERACTION-AND-ACCESSIBLE-POLISH.md)
- [ADR-0001 — TV-resident browser ownership](ADR-0001-TV-RESIDENT-BROWSER-OWNERSHIP.md)
