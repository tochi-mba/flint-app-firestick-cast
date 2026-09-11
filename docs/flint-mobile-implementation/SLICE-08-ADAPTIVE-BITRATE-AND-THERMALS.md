# Slice 08 — adaptive bitrate and thermals

**Governing ADR:** [ADR-0026](adrs/ADR-0026-FLINT-MOBILE-CONSUMES-THE-PROTOCOL-IN-REPO.md).

## Outcome

The session degrades honestly instead of stuttering, and says what it is doing while it does it.

## Entry criteria

- Slice 07 holds a mirror open long enough to get warm.

## User-visible vertical behaviour

1. The live strip carries one word for how the link is coping: Headroom, Stable or Congested.
2. When the link cannot keep up, the picture gets softer and a line appears saying that Flint lowered
   the bitrate and why.
3. When the phone gets hot, the same thing happens for a different reason, and the line says which
   reason.
4. At the point Android is shedding load on its own, the session stops and says so rather than
   competing with the platform for a device that has decided.

## Scope

### In scope

- `BitrateController` from `:protocol`, wired to the `STATS` frames the receiver already sends twice
  a second. Its four fields map one-for-one onto `LinkSample`; `pendingSendBytes` is sender-local and
  comes from the writer's own queue.
- `setParameters(PARAMETER_KEY_VIDEO_BITRATE)` on the encoder, and
  `PARAMETER_KEY_REQUEST_SYNC_FRAME` driven by `BitrateDecision.requestKeyFrame`.
- `KeyFrameGovernor`: the documented fallback, made observational. A request goes out; if no intra
  frame appears within a bounded number of frames, the strategy changes to a bounded two-second
  interval for the rest of the session and does not change back.
- `ThermalPolicy`, driven by `PowerManager`'s thermal status, with ceilings expressed as fractions of
  this session's own maximum rather than as absolute numbers — so a session that started
  conservatively is never told it may climb.
- Audio: `AudioPlaybackCaptureConfiguration` on API 29 and above, AAC-LC into `AudioConfigMessage`
  and `AudioPacket`. Below 29, and for apps that opt out, the UI says so plainly rather than shipping
  silence that looks like a bug.

### Explicitly out of scope

- Any latency figure. None has been measured on a phone, and a number on the live strip would be read
  as one.

## Why the controller backs off fast and returns slowly

`BitrateController`'s own comment says it: an over-large stream on a SoftAP link degrades every other
hotspot client too, so backing off multiplicatively and recovering additively is the neighbourly
failure mode. The phone is the access point here, which makes that everybody else in the house.

## What the receiver does and does not report

`STATS` carries queue depth, decode latency and a cumulative dropped-frame count, all real. It carries
`roundTripTimeUs = 0` always, because the receiver never measures one. The controller is fed the real
three plus the sender's own pending bytes, and the round trip on the diagnostics row stays the phone's
own measurement from slice 04.

## Test-first implementation order

1. `ThermalPolicy` across every level, including the floor and the stop.
2. `KeyFrameGovernor`, including that it never returns to on-demand once it has fallen back.
3. The `STATS`-to-`LinkSample` mapping, against real frames from the loopback harness.
4. The degradation copy, asserted as appearing exactly when the state changes.

## Full-suite checkpoint

The default suite, plus a long emulator run that actually reaches a congested state.

## Exit evidence

- A session driven from Headroom to Congested and back, with the UI narrating it.
- A thermal step-down observed on a real phone, with the sentence it produced recorded.
- The fallback strategy demonstrated on a device that ignores a sync-frame request, or the absence of
  one recorded as not yet observed.

## Rollback and stop conditions

The controller is additive: without it the encoder runs at its initial bitrate, which is the
behaviour slice 07 shipped. Stop if the UI would need to show a millisecond figure to explain a
degradation — the word is the honest unit here until glass-to-glass has been measured.
