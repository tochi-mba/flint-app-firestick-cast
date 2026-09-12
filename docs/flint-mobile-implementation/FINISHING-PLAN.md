# Flint Mobile — the finishing plan, as executed

This is the concrete plan for taking the phone app from "compiles and pairs" to a product with a
working Second Screen, Mirror and Media Handoff, a receiver it can install, and gates that say what
was proved where. It replaces the earlier draft, which described the shape of the work without
naming the files, the tests or the evidence. Every step below names all three.

The status column is kept honest. "CI" means the step's tests ran green on the pull request gate.
"Hardware" means somebody ran it on a named phone and a named Fire TV and wrote down what happened.
Nothing is called finished on the strength of a compiler.

## What the phone has already proved on real hardware

Two checks in Settings have been run on a physical phone and both passed:

- **Second-screen check.** The phone created a display only Flint can see, drew one frame into it,
  and read that frame back intact.
- **Encoder check.** The phone reports hardware H.264 and H.265 encoders.

Those are two prerequisites, not the pipeline. The first draws into an `ImageReader`, not into the
encoder; the second lists codecs rather than exercising one. Step 1 below is what turns them into a
proof that the production path produces pixels.

## Ground rules that shaped every step

- No B-frames, ever. Infinite GOP with IDR on demand, with one documented exception per session.
- Nothing allocates on the frame path. Media push is not the frame path, so one 512 KiB copy per
  chunk is allowed there and a whole-file allocation is not.
- Every listening socket binds a validated interface address; every outgoing socket, ADB included,
  binds the hotspot interface the ladder chose.
- No client names a filesystem path. A title is stripped to its leaf; a content URI never leaves the
  phone.
- Tokens are authorisation, not encryption. No string may call the link private, secure or
  encrypted, and the copy sweep enforces it.
- Nothing is installed on, replaced on, or removed from the TV without showing what and why. The
  RSA prompt on the television is honoured. Removal is offered wherever installation is.
- No latency figure is quoted from anything but the camera procedure in `docs/LATENCY_BUDGET.md`.

---

## Step 1 — prove the production encoder produces pixels, and choose the codec deliberately

**Why first.** This project has already shipped an encoder whose every structural check passed and
whose every frame decoded to nothing. Until a frame has gone through `ScreenEncoder` and come out of
a decoder looking like what went in, "Ready" on a mode card is a guess.

**Code.**

- `castcore/.../media/PatternCheck.kt` — a four-quadrant test pattern (four saturated colours), the
  expected colour at each quadrant centre, `YuvToRgb.convert` (BT.601 limited range), and
  `PatternCheck.verdict(samples)` which requires every quadrant to match within a tolerance wide
  enough for chroma subsampling and codec loss and narrow enough that a flat frame of any colour
  fails. Pure, unit-tested against exact values, a flat green frame, a flat black frame and a
  swapped-quadrant frame.
- `mobile/.../platform/EncoderRoundTrip.kt` — on the main thread, creates a private `VirtualDisplay`
  on `ScreenEncoder.inputSurface` and shows a `Presentation` painting the pattern; collects the
  `VIDEO_CONFIG` and the first key frame plus a few packets on the encoder thread; stops; then off
  the main thread decodes them with a second `MediaCodec` into a `YUV_420_888` `ImageReader`, samples
  the quadrant centres through the planes' own strides, and returns a verdict with a reason. Every
  resource is released in `finally`, and a timeout is a failure with the stage it reached in the
  detail.
- `CapabilityCoordinator.probeEncoders(activity)` runs the enumeration and then the round trip on the
  codec the session would choose. `PhoneCapabilities` gains `encoderRoundTrip: ProbeOutcome` and a
  `roundTripDetail`. The assessor blocks both streaming modes until the round trip has passed on
  this phone, and reports IMPOSSIBLE with the detail when frames decoded to nothing. When the phone
  refused a private display the round trip is reported as not run rather than failed, because a
  mirror through a projection may still work.
- `castcore/.../media/CodecChoice.kt` — the session's negotiated codec wins when the phone can
  encode it; otherwise H.264 before H.265; and a fallback to the other hardware codec when the
  encoder refuses to start. `OutputCoordinator.startEncoder` uses it instead of the first element
  of a set. `LiveOutput.codec` is shown on the live strip.
- `mobile/src/androidTest/.../EncoderRoundTripTest.kt` — the same harness under the instrumentation
  runner, for an emulator or device lane. It is not part of the PR gate and says so.

**Evidence.** CI: `PatternCheck`, `YuvToRgb`, `CodecChoice` and the assessor's new gate. Hardware: the
encoder check in Settings reports "a test frame survived the trip through H.264" on a named phone.

## Step 2 — make Mirror and Second Screen survive what happens to them

**Code.**

- `MobileActivity.onConfigurationChanged` → `MobileController.onDisplayChanged(width, height, dpi)`
  → `OutputCoordinator.reconfigureMirror`. Order: new encoder at the new bounded size, the existing
  `VirtualDisplay` resized and pointed at the new input surface, the old encoder stopped, and the new
  encoder's own `VIDEO_CONFIG` followed by the sync-frame request `publishConfig` already makes. The
  projection and the display are reused; consent is never asked for twice. Second Screen ignores
  rotation entirely.
- `castcore/.../media/PresentationGeometry.kt` — the second screen is a landscape 1920×1080 canvas
  regardless of how the phone is held, bounded by `EncoderPolicy`. Tested for a portrait phone.
- Key-frame fallback: `OutputCoordinator` restarts the encoder once per session with
  `KeyFrameStrategy.BoundedInterval`, retargeting the display or projection surface, guarded by a
  session flag so it cannot loop.
- Transport failure: the sink counts consecutive refused writes and stops the output after a bounded
  run, keeping the first concrete failure for the notice. `CastConnection.sendVideoPacket` decrements
  its pending-bytes counter in `finally`.
- The notification's Stop is observed: when `CastService.isForeground` drops while output is live,
  `OutputCoordinator.stop()` runs, so a stop from the lock screen releases the encoder rather than
  leaving it encoding into a socket nobody reads.
- `castcore/.../screen/SecondScreenScene.kt` — a sealed scene model (`Dashboard`, `NowPlaying`) with
  no Android in it; `SecondScreenContent` renders it; the live strip becomes a cockpit with the scene,
  the size, the codec, the link word and Stop.

**Evidence.** CI: geometry, scene, the fallback guard, the failure counter, the reconfigure order as
a pure sequence. Hardware: a mirror rotated portrait → landscape → portrait without the television
dropping the session; a stop from the notification with the phone locked leaves no service behind.

## Step 3 — degrade deliberately when the phone is hot

**Code.** `mobile/.../platform/ThermalWatch.kt` adapts `PowerManager.OnThermalStatusChangedListener`
to `ThermalLevel`. `BitrateController` gains a ceiling that is a fraction of the session's own
maximum and can only ever lower it. `OutputCoordinator` applies `ThermalPolicy.decide` on every
change, stops at the emergency level with the policy's own sentence, and reports a thermal reason
separately from a congestion one. `LiveOutput.diagnostics` carries the target bitrate, the receiver
queue depth, the sender's pending bytes, the dropped-frame delta and the last decision, and the
Settings diagnostics card shows them. No latency figure appears anywhere.

**Evidence.** CI: the ceiling never raises a bitrate; every level maps to a decision; the stop fires
at the platform's own emergency levels. Hardware: a thermal step-down observed and its sentence
recorded, or its absence recorded as not yet observed.

## Step 4 — Media Handoff, end to end

**Code.**

- `MobileActivity` registers an `OpenDocument` launcher for `video/*`; the URI goes to the controller
  and nowhere else. `mobile/.../media/ContentMediaSource.kt` reads display name, MIME type and size
  through the `ContentResolver`, opens a stream, and never asks for a path.
- `mobile/.../state/MediaCoordinator.kt` with a sealed `MediaState` (`Idle`, `Preparing`,
  `Sending(bytesSent, totalBytes?)`, `Buffering`, `Playing`, `Paused`, `Ended`, `Failed`). It streams
  `MediaHandoff.CHUNK_BYTES` chunks in order on the IO dispatcher through one reused buffer, marks
  exactly one final chunk, refuses an empty file before sending anything, updates progress per
  chunk, cancels between chunks, stops reading the moment a write is refused, and then sends
  `MediaHandoff.playPushedFile`. It reduces `PLAYBACK_STATE` into the state and sends
  `TransportControl` for play, pause, seek and stop with a monotonic sequence number.
- `MediaScreen` becomes the remote: Choose video, the title and size, send progress, the receiver's
  state, play/pause, a scrubber driven by the receiver's reported position, Stop and Clear, and the
  receiver's own error wording where it sent one.
- Receiver: `receiver/.../media/PushedMediaSink.kt` extracts the pushed-file assembly from
  `ReceiverService` into a class with tests: one transfer at a time, a new transfer discards a
  previous pending file, a size cap against usable cache space, partial files deleted on error and
  disconnect, and the same bytes out as in, checked by hash.
- `castcore/.../screen/SurfaceReducer.kt` — one authority for `Idle | Presentation | Mirror | Media`.
  Starting media while a second screen is live stops the presentation stream before the LOAD;
  clearing media that displaced a second screen tells the person rather than restarting it silently.

**Evidence.** CI: chunk boundaries at 1 byte, 512 KiB, 512 KiB + 1 and several chunks; cancellation
and refusal mid-transfer; the empty-file refusal; the reducer's every transition; the receiver sink's
hash equality and cleanup. Hardware: a real MP4 chosen on the phone plays on the Fire TV, is paused,
seeked and stopped from the phone, and the temporary file is gone afterwards.

## Step 5 — mirror audio, honestly

**Code.** `mobile/.../media/AudioCapture.kt` uses `AudioPlaybackCaptureConfiguration` and
`AudioRecord` on API 29 and above, encodes AAC-LC with a `MediaCodec`, sends `AudioConfigMessage`
with the codec-specific data and `AudioPacket`s stamped on the same monotonic clock as the video.
`RECORD_AUDIO` is requested before the capture dialog, once. The live strip distinguishes: platform
too old; capture running but nothing capturable (measured, not assumed); encoder failed; working.
Video never waits for audio.

**Evidence.** CI: the level detector and the timestamp arithmetic. Hardware: an app that allows
capture produces synchronised sound on the television; an app that opts out mirrors video with the
strip saying why there is no sound.

## Step 6 — install and remove the receiver from the phone

**Code.**

- `mobile/.../platform/AdbIdentityStore.kt` — one RSA key pair per phone, generated once, stored
  encrypted with the same keystore-held AES key the session tokens use, so the television's accepted
  identity survives reinstalls of nothing and restarts of everything.
- `castcore/.../setup/FireOsPlatformResolver.kt` — the Windows host's rules ported exactly: known
  Vega build models first, then documented API-level ranges, and Unknown for anything undocumented.
- `mobile/.../net/AdbClient.kt` — a socket bound to the hotspot interface, the bounded 5555–5585 port
  scan for the selected television only, `AdbConnection` from `:protocol`, read-only identification
  (banner, `ro.build.version.sdk`, `ro.build.version.release`, `ro.product.model`, `pm list
  packages` for the receiver), then `installApk` of the staged bundled package and
  `uninstallPackage`. `AdbAuthorizationRequiredException` becomes `AwaitingAuthorisation`.
- `castcore/.../setup/InstallFlow.kt` — a pure reducer from events to `ReceiverInstallStage`, so the
  card's every transition is a table with a test. The Settings card's Install and Remove become live,
  Remove needs a second press that names the package and the television.

**Evidence.** CI: the resolver, the reducer, the identity codec, the port order, the coordinator
against a fake installer, and the client against a scripted device on a loopback socket that speaks
the real ADB framing. Hardware: an install and a removal on a real Fire TV from the phone alone, the
RSA prompt shown and accepted on the remote.

**What was not in the draft.** A television with no receiver on it answers nothing on the receiver's
port and so could never have been selected, which made installing impossible in exactly the case it
exists for. Typing its address now falls through to a read-only ADB look, and a television that
answers that way joins the list with what it said about itself. And a television identified over ADB
whose receiver is not answering used to be assessed as ready to cast; it is blocked now, with the
install remedy.

## Step 7 — the receiver stops calling every peer a PC

**Code.** The idle, mirror and playback surfaces use the peer's own name from `HELLO` where they have
it and peer-neutral wording where they do not. The approved snapshots that pin those strings are
regenerated through the Snapshots workflow, looked at, and committed.

**Evidence.** CI: the receiver snapshot suite green against the new images.

## Step 8 — documentation that matches the code

The implementation README's "what is not in this change" is rewritten to what is actually not in it.
`HARDWARE-EVIDENCE.md` records every physical run: phone model and Android version, Fire TV model
and Fire OS version, codec, size and frame rate, whether sync-frame requests were honoured, and what
was seen. The two probe results already obtained are its first two entries, with the device names
left for the person who ran them to fill in.

---

## Status

| Step | Code | CI | Hardware |
|---|---|---|---|
| 1 Pixel round trip and codec choice | done | green: Mobile CI on `ad10785` | second-screen check and encoder listing passed; the pixel round trip has not been run |
| 2 Mirror and second-screen hardening | done | green: Mobile CI on `ad10785` | not run |
| 3 Thermal and diagnostics | done | green: Mobile CI on `ad10785` | not run |
| 4 Media handoff | done | green: Mobile CI on `ad10785`; receiver CI on `c01603d` | not run |
| 5 Mirror audio | done | green: Mobile CI on `ad10785` | not run |
| 6 Receiver install over ADB | done | green: Mobile CI on `ad10785` | not run; the RSA prompt has not been shown or accepted on a television |
| 7 Peer-neutral receiver copy | done | green: receiver CI on `c01603d`, against images rendered by Snapshots run 34685433516 | not applicable |
| 8 Documentation | done | not applicable | not applicable |

The hardware column is filled in only from `HARDWARE-EVIDENCE.md`. "Green" names the commit the gate
ran on; a later commit that touched only the phone modules does not re-run the receiver gate, and a
commit that touched only the receiver does not re-run the phone gate, so two commits are named where
two gates apply.

Two things happened on the way that the table cannot show. The Snapshots workflow gained a `push`
input, because the environment this was built in could not download workflow artifacts and the
regenerated images had to reach the branch some other way; the commit it makes is the workflow's
own and says which run rendered the images. And the phone gate was red for one commit on a double
hyphen inside a manifest comment, which XML forbids and which the gate's cancellation on the next
push had hidden; that is why every step above names the same final commit rather than its own.
