# Slice 07 — screen mirror

**Governing ADRs:** [ADR-0024](adrs/ADR-0024-PHONE-BINDS-EVERY-SOCKET-TO-THE-SELECTED-INTERFACE.md)
and [ADR-0025](adrs/ADR-0025-SECOND-SCREEN-IS-APP-OWNED-PRESENTATION-CONTENT.md).

## Outcome

The phone mirrors its own screen to the television, with consent asked for properly and the session
surviving a rotation.

## Entry criteria

- Slice 06 proved the encode-and-send path against a surface that needed no consent. Everything new
  here is platform ceremony rather than pipeline.

## User-visible vertical behaviour

1. Starting a mirror explains what Android is about to ask before Android asks it, including that it
   will ask again every time.
2. The television shows the phone's screen, and the phone shows a live strip with one unmistakable
   Stop.
3. Locking the phone leaves the notification, which is the only control that exists at that point.
4. Rotating the phone follows on the television, without the session dropping.

## Scope

### In scope

- `MediaProjectionManager.createScreenCaptureIntent()` every session; the result is not persistable
  and is never treated as if it were.
- On API 34 and above, a `mediaProjection`-typed foreground service running *before*
  `getMediaProjection()` is called.
- A `MediaProjection.Callback` registered every time, because omitting it throws on API 34+.
- On API 35 and above, consent that is per-capture and re-prompts; a stopped projection is never
  reused.
- Rotation: re-send `VIDEO_CONFIG` with the new dimensions and codec-specific data, and request a key
  frame immediately afterwards.
- The ongoing notification: one low-importance channel, the frameless spark as the small icon, the
  mode as the title, the television as the text, and a single Stop action.

### Explicitly out of scope

- Audio. `AudioPlaybackCaptureConfiguration` is slice 08's, and it captures only from apps that allow
  it — which the UI will have to say rather than shipping silence that looks like a bug.

## The manifest line that must differ

`:receiver` declares `foregroundServiceType="mediaPlayback"`. The phone declares
`mediaProjection|connectedDevice` and starts with whichever the mode actually needs. Declaring a
second screen as a projection would be a claim about what the app is doing that is not true, and on
API 34 starting a `mediaProjection` service with no projection active throws.

## Why rotation is a hazard and not a detail

Every `VIDEO_CONFIG` is a hard decoder reset on the receiver: it releases the codec, clears its whole
pending queue and re-arms its key-frame gate. The very next packet after one must be an intra frame,
or it is counted as dropped and discarded — and the television shows the last frame it managed to
decode until something else happens to produce a key frame. Rotation is the ordinary way to hit this,
because rotation re-sends the config. `VideoConfigPolicy` exists to state the rule at the call site.

## No allocation on the frame path, counted

`WireCodec.writeTo` builds a payload array per frame, and `BinaryData` copies on the way in and again
on the way out. At sixty frames a second that is megabytes a second of garbage produced by the one
loop that must not touch a general-purpose allocator: a collection pause between two frames is a
stutter, arriving exactly when the encoder is busiest.

`FrameWriter` in `:protocol` is the answer: a borrowed-buffer path whose output is asserted
byte-identical to the codec's. It is a second way of writing one format, not a second format. The
steady-state loop uses direct `ByteBuffer`s, one reused `MediaCodec.BufferInfo`, one writer thread,
and a bounded latest-only channel. The allocation count is measured rather than assumed.

## Test-first implementation order

1. The foreground-service ordering, under Robolectric, on API 34, 35 and 36.
2. The rotation path, asserting a `VIDEO_CONFIG` followed by a key frame.
3. The allocation count on the steady-state loop.
4. The encoder round trip, again, through the projection surface rather than the virtual display.

## Full-suite checkpoint

The default suite, plus the emulator job.

## Exit evidence

- Consent and service ordering correct on all three API levels, with the Robolectric tests naming
  them.
- A rotation on a real phone followed on a real television.
- A measured allocation count on the frame path, recorded.

## Rollback and stop conditions

Mirror is independent of second screen by construction, so reverting it leaves slice 06 working.
Stop if the consent flow has to be worked around on any device: a mirror that starts without the
person having said yes is not a feature.
