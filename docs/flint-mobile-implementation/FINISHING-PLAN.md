# Flint Mobile feature completion plan

This document is the execution plan for taking the current Android phone host from a compiled, mostly wired implementation to a hardware-proven product with useful **Second Screen**, **Mirror**, and **Media** modes.

It is intentionally based on the code that exists now rather than on the older slice documents alone. Several pieces that those documents described as future work are already present; several important gaps are also now visible only because the app has run on a real phone.

## Current evidence

The current phone build has now produced two useful real-device results:

- the private-display check passes: the phone creates a display only Flint can see, draws a known frame into it, and reads that frame back intact;
- the encoder probe reports hardware H.264 and H.265 encoders.

Those are important, but they prove two separate prerequisites rather than the complete cast pipeline. The private-display probe currently renders into an `ImageReader`, not into `ScreenEncoder`, and the encoder probe enumerates hardware codecs rather than encoding and decoding a known frame. The next milestone therefore has to prove **pixels all the way through the same path the product uses**.

## What is already implemented

The following should be treated as existing infrastructure, not rebuilt:

- hotspot-aware discovery, pairing, token reuse and a persistent `CastConnection`;
- H.264/H.265 surface-input encoding through `ScreenEncoder`;
- direct `ByteBuffer` video writes through the allocation-conscious `FrameWriter` path;
- receiver-side `VIDEO_CONFIG` / video packet handling and `MirrorVideoDecoder`;
- `SurfaceMode.MIRROR` and `SurfaceMode.PRESENTATION` receiver surfaces;
- second-screen private `VirtualDisplay` creation and a Compose `Presentation` host;
- MediaProjection consent flow and the API-34 foreground-service ordering for mirror start;
- receiver `STATS` feeding the existing `BitrateController`;
- on-demand key-frame requests and detection that a vendor encoder ignored one;
- receiver-side pushed-media assembly, ExoPlayer playback and `PLAYBACK_STATE` reporting;
- protocol messages for media push, media load/clear, playback state and transport controls;
- pure `MediaHandoff` helpers for chunk sizing, MIME validation and title sanitisation;
- a signed rolling mobile release workflow.

The goal is to finish and harden those paths rather than create parallel ones.

---

# Delivery order

The order below is deliberate. Each phase leaves a useful, independently testable product state and avoids building UI on top of an unproven media path.

## Phase 0 — prove the real video pipeline on hardware

**Goal:** establish that the exact surface -> encoder -> wire -> decoder path can produce correct pixels before adding more product behaviour.

### 0.1 Add an encoder pixel round-trip test

Create an instrumentation test/hardware harness that:

1. creates a known test pattern;
2. draws it into `ScreenEncoder.inputSurface` through a private `VirtualDisplay`;
3. collects `VIDEO_CONFIG` plus encoded frames;
4. decodes them with a second `MediaCodec`;
5. reads the decoded pixels;
6. asserts the pattern survived rather than merely asserting that frames existed.

This is the most important missing test. A valid H.264 stream can still decode to blank/green frames, so structural assertions are not sufficient.

### 0.2 Add a receiver loopback variant

Once the local round trip passes, run the same known pattern through a real `CastConnection` into the receiver and confirm:

- receiver gets `VIDEO_CONFIG` first;
- first useful frame is an intra frame;
- decoded dimensions match the config;
- the receiver reports `mirrorFrameReceived=true`;
- the rendered TV frame is visibly the known pattern.

### 0.3 Record actual device evidence

For every physical test record:

- phone model / Android version;
- Fire TV model / Fire OS version;
- codec selected;
- output dimensions and frame rate;
- whether on-demand sync-frame requests are honoured;
- no claimed latency number unless measured glass-to-glass.

**Exit gate:** no feature work below is called complete until one physical phone and one Fire TV have shown the known pattern end to end.

---

# Phase 1 — finish Second Screen first

Second Screen should remain the first product mode because it avoids MediaProjection consent and proves the streaming pipeline with a Flint-owned source.

## 1.1 Fix the output geometry

`startSecondScreen()` currently derives its encoded dimensions from the phone screen. That is correct for mirror and wrong for a TV-oriented Flint surface: a portrait phone can therefore create a portrait second screen.

Introduce a presentation output policy separate from mirror sizing:

- Second Screen uses a landscape canvas, initially 1920x1080 bounded by `EncoderPolicy` and the selected encoder's supported size/rate.
- Mirror continues to follow the phone's current orientation and aspect ratio.
- Do not pretend the receiver advertised a resolution until the protocol actually carries one.

Keep the two policies separate so changing phone orientation never rotates the app-owned Second Screen canvas.

## 1.2 Replace the static placeholder with a scene model

`SecondScreenContent` currently proves rendering but only displays a Flint holding screen. Turn it into a small scene host rather than growing one giant composable.

Add an Android-free model, for example:

```text
SecondScreenScene
  Dashboard
  NowPlaying
  Photo
```

The first shippable scene should be `Dashboard`: connected TV, link state, current activity and a clear statement that this is Flint-owned content. `NowPlaying` can then reuse media playback state, and `Photo` can be added only once image selection is actually implemented.

Do **not** describe or implement this as Android desktop extension. A private `VirtualDisplay` can show only Flint's own content.

## 1.3 Add phone-side cockpit controls

While Second Screen is live, the phone should expose:

- current scene;
- output resolution;
- link health and bitrate;
- change scene where meaningful;
- Stop.

Keep controls on the phone. The TV's existing mirror/presentation overlay remains a secondary emergency control, not a second full UI.

## 1.4 Make lifecycle failure explicit

Cover:

- activity background/foreground;
- TV/session disconnect;
- Presentation dismissal;
- encoder failure;
- display release;
- service stop after partial startup failure.

`OutputCoordinator.stop()` remains the single cleanup boundary.

## 1.5 Tests

Add:

- Robolectric tests for `ComposePresentation` owner/lifecycle wiring;
- a test that Second Screen chooses landscape presentation dimensions independently of phone orientation;
- scene-model unit tests;
- UI semantics tests for the live cockpit;
- physical known-pattern test from Phase 0 through `SecondScreenHost`.

**Exit gate:** tapping Start Second Screen on the real phone shows a stable Flint-owned landscape surface on the Fire TV for at least 30 minutes, survives app background/foreground, and stops cleanly.

---

# Phase 2 — finish Mirror

Most of the initial mirror path is present. The main missing product work is configuration-change handling, reconfiguration safety and real-hardware proof.

## 2.1 Implement rotation instead of only declaring support for it

The manifest tells Android that `MobileActivity` handles configuration changes, but the activity currently has no `onConfigurationChanged()` implementation that reconfigures a live mirror.

Add:

```text
MobileActivity.onConfigurationChanged
  -> controller.onDisplayConfigurationChanged(...)
  -> OutputCoordinator.reconfigureMirror(...)
```

The current screen dimensions stored in `PhoneCapabilities` are captured at app creation, so live display metrics also need to become updateable rather than frozen constructor values.

## 2.2 Reconfigure the existing projection safely

On Android 14+ a MediaProjection consent/session must not be treated as reusable for creating repeated captures. Rotation must therefore keep the same projection and virtual display rather than asking for another consent token.

Safe reconfiguration order:

1. calculate the new bounded mirror dimensions;
2. detach the old encoder surface from the existing `VirtualDisplay`;
3. stop the old encoder;
4. start a new encoder at the new dimensions;
5. resize the existing `VirtualDisplay`;
6. attach the new encoder input surface;
7. let the new encoder emit a fresh `VIDEO_CONFIG`;
8. immediately require/request a key frame.

The receiver already treats `VIDEO_CONFIG` as a decoder reset, so the first frame after it must be independently decodable.

## 2.3 Make codec selection explicit

The current path takes `advertisedCodecs.firstOrNull()` from a set. Replace that with deterministic policy:

1. prefer H.264 unless there is a measured reason to prefer H.265;
2. verify the receiver negotiated/supports it;
3. fall back to the other hardware codec only if encoder creation/configuration fails before streaming starts;
4. surface the chosen codec in diagnostics.

This also avoids a set iteration order silently deciding what gets sent.

## 2.4 Correct key-frame fallback

Today `ScreenEncoder` can detect an encoder that ignores `PARAMETER_KEY_REQUEST_SYNC_FRAME`, and the UI reports the degradation, but `OutputCoordinator` does not yet perform the documented fallback.

Implement a one-time restart per session:

- mark strategy as bounded 2-second GOP;
- restart the encoder with that strategy;
- preserve the same projection / display source;
- emit new `VIDEO_CONFIG`;
- never switch back to infinite/on-demand GOP during that session.

Prevent restart loops with an explicit session-level state flag.

## 2.5 Stop/error behaviour

Mirror must stop when:

- Android revokes MediaProjection from the system UI;
- the cast connection closes;
- encoder fails;
- foreground service fails;
- user presses Stop in app or notification.

Every path must release in one order: source surface -> projection/display -> encoder -> service -> UI state.

## 2.6 Tests

Add:

- API 34/35/36 service/projection ordering tests;
- rotation test asserting exactly one active projection and one virtual display;
- `VIDEO_CONFIG` then key-frame ordering test;
- deterministic codec selection tests;
- ignored-key-frame fallback restart tests;
- repeated start/stop stress test;
- real phone portrait -> landscape -> portrait session on a real TV.

**Exit gate:** a real mirror runs for 30 minutes, rotates both directions without disconnecting, can be stopped from the notification while the phone is locked, and leaves no foreground service behind.

---

# Phase 3 — resilience: bitrate, thermals and backpressure

The receiver statistics path already changes encoder bitrate. Finish the parts that currently exist only as policy/docs.

## 3.1 Complete adaptive bitrate behaviour

Keep `BitrateController` as the authority. Add explicit session diagnostics for:

- current target bitrate;
- receiver queue depth;
- sender pending bytes;
- drop-count delta;
- last bitrate decision/reason.

Do not expose unmeasured latency as a product metric.

## 3.2 Implement thermal policy

Add a `ThermalCoordinator` backed by `PowerManager.OnThermalStatusChangedListener`.

Policy should:

- cap bitrate progressively as thermal status rises;
- optionally cap frame rate/resolution only through an explicit encoder restart policy;
- stop the cast at the severe platform-defined threshold chosen by the policy;
- expose a separate thermal degradation reason from network congestion.

The bitrate ceiling must be a fraction of the session's starting ceiling so thermal logic can never accidentally increase a conservative session.

## 3.3 Make socket failure visible to the output path

`send()` and `sendVideoPacket()` return booleans. The live output path should not continue encoding indefinitely after writes fail.

- count consecutive write failures;
- terminate output/session after a small bounded threshold;
- keep the first concrete transport failure for user-visible diagnostics;
- ensure `pendingSendBytes` is decremented in `finally` even when a frame write throws.

## 3.4 Long-run tests

Add a soak harness that can inject:

- queue growth;
- packet loss/drop reports;
- stalled receiver reads;
- thermal state transitions;
- disconnect/reconnect.

**Exit gate:** under congestion the picture degrades rather than repeatedly freezing, and no encoder/service survives a dead socket.

---

# Phase 4 — mirror audio

Audio should be an additive sub-feature of Mirror, not a prerequisite for video.

## 4.1 Implement playback capture on API 29+

Use `AudioPlaybackCaptureConfiguration` + `AudioRecord` and encode AAC-LC into the existing:

- `AudioConfigMessage`;
- `AudioPacket`;
- `CastConnection.sendAudioPacket()`;
- receiver `MirrorAudioDecoder`.

## 4.2 Be truthful when audio cannot be captured

Some apps opt out of playback capture. Flint must distinguish:

- platform too old;
- playback capture available but source app opted out / produced no capturable audio;
- audio encoder failure;
- audio is working.

Video continues even when audio is unavailable unless the user explicitly chooses otherwise.

## 4.3 Sync and lifecycle

Use the same monotonic presentation-time origin for audio and video. Stop audio before releasing projection/session resources.

**Exit gate:** a capturable app produces synchronized TV audio/video, and an opt-out app mirrors video with an honest no-audio explanation.

---

# Phase 5 — implement Media Handoff end to end

The receiver side and wire format are already substantially ready. The missing work is almost entirely on the phone.

## 5.1 System file picker

Use `ActivityResultContracts.OpenDocument()` / Storage Access Framework for video selection.

Rules:

- request `video/*`;
- read only through `ContentResolver`;
- never require a filesystem path;
- never copy the whole video into app storage;
- obtain display name, MIME type and length from metadata when available;
- unknown length must remain a supported state.

## 5.2 Introduce `MediaCoordinator`

Do not put file I/O into `MobileController`.

Suggested state:

```text
MediaState
  Idle
  Preparing(metadata)
  Sending(bytesSent, totalBytes?)
  Buffering(metadata)
  Playing(playback)
  Paused(playback)
  Ended(playback)
  Failed(message)
```

Responsibilities:

- own the selected content URI only for the active operation;
- stream 512 KiB chunks using `MediaHandoff.CHUNK_BYTES`;
- send `MediaDataMessage` in strict order;
- mark exactly one final chunk;
- after the final bytes, send `MediaHandoff.playPushedFile(...)` with blank URL;
- process `PlaybackStateMessage` from `SessionCoordinator`;
- expose play/pause/seek/stop commands.

## 5.3 Handle zero-length and final-chunk semantics explicitly

`MediaHandoff.chunkCount(0)` is currently zero, while the receiver only knows a pushed item is complete when a `MediaDataMessage` has `isFinal=true`.

Choose and test one protocol behaviour before wiring the picker:

- either reject zero-byte media before sending; or
- send an explicit empty-final representation if the wire permits it.

Do not leave this as an accidental edge case.

## 5.4 Backpressure and responsiveness

Pushing a large file must not block the main thread and should not starve control commands.

- perform ContentResolver reads on IO dispatcher;
- reuse one 512 KiB buffer;
- bound outstanding work rather than reading the whole file ahead;
- update progress after each successful chunk;
- allow cancellation between chunks;
- if the cast socket fails, stop reading immediately.

Because media transfer is not the live frame path, a per-chunk payload copy is acceptable, but a whole-file allocation is not.

## 5.5 Playback controls

Use existing `ControlMessage(TransportControl(...))` for:

- PLAY;
- PAUSE;
- SEEK_TO;
- STOP.

Drive the scrubber from receiver `PlaybackStateMessage`, not from a phone-side guessed timer.

## 5.6 Media UI

Replace the current "not in this build yet" card with:

- **Choose video** when connected and media verdict is offerable;
- selected filename and original MIME/size where known;
- send progress;
- buffering state;
- play/pause button;
- scrubber with receiver-reported position/duration;
- Stop / Clear;
- receiver error detail verbatim where safe.

Do not expose the URI or storage path in UI, logs or protocol messages.

## 5.7 Receiver hardening

Before shipping, add limits around pushed-media reception:

- one active pushed file per session;
- bounded maximum accepted file size or a documented free-space check;
- delete partial file on disconnect/error/new transfer;
- delete completed temporary media when cleared/session ends;
- refuse malformed ordering rather than appending ambiguous data;
- hash-based integration test that received bytes equal source bytes.

## 5.8 Tests

Add:

- ContentResolver fake/fixture tests;
- exact chunk-boundary tests: 1 byte, 512 KiB, 512 KiB + 1, multi-chunk;
- cancellation mid-transfer;
- disconnect mid-transfer;
- hash equality through loopback receiver;
- title path stripping for `/` and `\\`;
- MIME >255-byte refusal;
- playback-state reducer tests;
- real MP4 playback on Fire TV with play/pause/seek/stop.

**Exit gate:** select a local video on the phone, send it without creating an app-storage copy, play it at original encoded quality on Fire TV, control it from the phone, and clean up the temporary receiver file afterwards.

---

# Phase 6 — integrate Second Screen and Media without conflating them

These two features should share state, not transport semantics.

- Media Handoff plays the original file directly through ExoPlayer on the receiver. It should **not** be routed through ScreenEncoder.
- Second Screen is a live encoded Flint-owned UI surface. It can display a `NowPlaying` view driven by the same playback state, but it must not retranscode the video just to show it.
- If media playback starts while Second Screen is active, explicitly transition receiver surface to `PLAYER` and stop/pause the presentation stream rather than sending two competing visual surfaces.
- When playback is cleared, offer returning to the previous Second Screen scene rather than silently restarting it.

Create a small `SurfaceCoordinator`/state reducer so these transitions have one authority:

```text
Idle
Presentation(scene)
Mirror
Media(item)
```

Illegal combinations become impossible by construction.

---

# Phase 7 — receiver copy and peer identity

The receiver still contains PC-specific copy in several surfaces. Before mobile is considered finished:

- make the peer type/name supplied by the handshake drive copy;
- replace fallback `Your PC` / `Unknown PC` text for phone sessions;
- update receiver snapshots intentionally;
- keep Windows wording unchanged for Windows sessions.

This is cosmetic only after the transport works, but it is required before calling the phone experience finished.

---

# Phase 8 — hardware test matrix and release gate

## Minimum physical matrix

At minimum test:

- Android 10/11 class device if available;
- Android 13;
- Android 14+ because MediaProjection rules changed materially;
- one Samsung device because vendor MediaCodec/VirtualDisplay behaviour is worth independent evidence;
- Fire OS 7;
- Fire OS 8.

Vega OS remains explicitly unsupported.

## Per-device acceptance run

For each supported phone/TV pair:

1. discover and pair;
2. run encoder probe and private-display probe;
3. Second Screen for 30 minutes;
4. Mirror for 30 minutes;
5. rotate mirror portrait/landscape repeatedly;
6. lock/unlock phone;
7. stop mirror from notification;
8. induce weak-link behaviour if possible;
9. play a local media file;
10. pause, seek, resume, stop;
11. disconnect TV mid-stream and verify cleanup;
12. reconnect without reinstalling either app.

## CI additions

Keep the normal PR gate fast, then add separate instrumentation/hardware lanes:

- PR: JVM/unit/lint/source guards;
- emulator: encoder/decoder pixel round trip where hardware codec support exists;
- nightly/device-farm: long running rotation, projection lifecycle and media transfer;
- release: signed APK + physical acceptance evidence recorded for the version.

Do not make a hardware-dependent test silently pass when no codec/device exists. Report it as not executed and keep the release evidence explicit.

---

# Concrete PR sequence

Do not put the remaining implementation into one giant PR. Use this order:

| PR | Scope | Merge evidence |
|---|---|---|
| 1 | Hardware pixel-round-trip harness + diagnostics | Known pixels survive encode/decode on a physical phone |
| 2 | Second Screen geometry + scene host + cockpit | Real TV shows stable landscape Flint surface |
| 3 | Mirror rotation/reconfiguration + deterministic codec policy | Portrait/landscape/portrait without disconnect |
| 4 | Key-frame fallback + transport failure handling | Forced ignored sync request recovers with bounded GOP |
| 5 | Thermal policy + long-run degradation | Controlled thermal/network state changes are narrated and bounded |
| 6 | Mirror audio | Capturable source produces synchronized AAC; opt-out remains video-only |
| 7 | Media picker + push coordinator | File hash matches on receiver and progress/cancel work |
| 8 | Media remote UI + receiver cleanup hardening | Real video play/pause/seek/stop and temporary file cleanup |
| 9 | Surface state reducer + Second Screen/Media integration | No competing surfaces; deterministic transitions |
| 10 | Receiver phone-aware copy + snapshots | Phone sessions never say "Your PC" |
| 11 | Hardware matrix / release hardening | Signed release passes recorded physical acceptance run |

Each PR should leave all existing Windows and receiver tests green and should avoid unrelated refactors.

---

# Immediate next PR

The next implementation PR after this plan should be **PR 1: hardware pixel-round-trip harness + diagnostics**.

Do not start by making the Media tab prettier or adding more Second Screen scenes. The phone has proved that it can create the display and that hardware codecs exist; the highest-value unknown is now whether the exact production encoder path produces correct pixels and whether the receiver displays them correctly. Once that is green, the rest of the work is product engineering rather than platform speculation.
