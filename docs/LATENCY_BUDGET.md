# Latency Budget

This document holds **measurements**. Anything not yet measured is labelled a target and is not a
claim. No figure may be moved from the target column to the measured column without a repeatable
procedure and a named device pair.

## Status

**The host path is measured. Glass to glass is not.**

Mirroring works end to end: a Windows desktop reaches a Fire TV Stick (AFTMM, Fire OS 6.7.1.1) as
H.264, decoded and rendered by the receiver. Every stage that runs on this machine has been timed
with a repeatable test, and those figures are below.

What has *not* been measured is the number that actually matters to a person watching: the delay
between something changing on the laptop screen and the same change appearing on the television.
That needs the camera procedure further down, and until it is done no end-to-end latency figure
appears in this document. The host path being fast is a precondition for a good number, not evidence
of one — the receiver's decoder and the panel's own processing are both larger terms than anything
here, and neither is under Flint's control.

The table below is what Flint is aiming at, and why each stage is bounded where it is.

## Targets

| Stage | Target | Owner | Why this bound |
|---|---|---|---|
| Capture acquire | ~1 ms | engine | Windows Graphics Capture delivers a texture already on the GPU; the cost is the wait for the next frame, not a copy. |
| Colour convert (BGRA to NV12) | ~0.5 ms | engine | A compute shader on the same device. Doing this on the CPU costs an order of magnitude more and is why naive implementations feel slow. |
| Cross-adapter copy | ~1-2 ms | engine | Only on hybrid hosts where the display and the encoder are different devices. Avoided entirely when they are the same. |
| Encode | ~2 ms | engine | Ultra-low-latency tune, constant bitrate, no B-frames, slices so packetization overlaps encoding. |
| Packetize and FEC | ~0.3 ms | engine | Reed-Solomon over a small block. Cheap relative to everything around it. |
| LAN transit | 1-4 ms | network | Wired or clean 5 GHz. Congested 2.4 GHz breaks this badly and is the most common real-world cause of a bad session. |
| Receiver jitter buffer | 0-8 ms | receiver | Sized from measured jitter, targeting zero. A fixed buffer would be simpler and always wrong. |
| Decode | 5-16 ms | receiver | MediaCodec with the low-latency path, where the device honours it. |
| Panel and compositor | 8-100 ms | **not ours** | The television's own picture processing. Game Mode bypasses most of it. |

Realistic total on Fire OS hardware: **25-50 ms**, with the panel frequently the largest single
term.

## The television is usually the bottleneck

A television's picture processing commonly adds more delay than the entire Flint pipeline. Turning
on Game Mode has a larger effect on perceived latency than any host-side tuning Flint can do.

This is worth stating plainly because it sets what "good" means. Flint controls roughly 5 ms of the
host side. It cannot control the panel, and it will not publish a figure that quietly excludes it.

## Measurement procedure

The only figure that matters is glass to glass: the time between a change appearing on the laptop
screen and the same change appearing on the television.

1. Run `tools/scripts/measure-latency.ps1`, which drives a full-screen flashing pattern with a frame
   counter.
2. Film the laptop screen and the television together, in one frame, at 240 fps.
3. Count the frames between the change on each screen. Each frame is 4.17 ms.
4. Repeat twenty times and record the median and the spread. A single reading is not a measurement.

Record, for each device pair: television model, Fire OS version, Game Mode on or off, radio band,
host adapter and encoder, resolution, and frame rate. A latency figure without its conditions is
not reproducible and does not belong here.

## Per-stage instrumentation

The engine timestamps each stage and reports them in the wire protocol's `StatsMessage`, so the
Diagnostics telemetry strip shows where time is actually going rather than only the total. When the
total is worse than the sum of the parts, the difference is queueing, and that is a signal to reduce
the bitrate rather than to tune the encoder.

## Measured results

### Glass to glass

_Empty. Needs the camera procedure above; nothing else counts for this table._

| Date | Host | Receiver | Game Mode | Band | Resolution | Median | Spread |
|---|---|---|---|---|---|---|---|

### Host path, per frame

Measured on the development machine: Intel UHD Graphics driving the panel, RTX 4070 Laptop with no
display attached, 2560x1600 desktop reduced to 1920x1200 for encoding, Windows 11 build 26100.

| Pipeline | Median | p95 | Worst |
|---|---|---|---|
| Frame stays on the GPU | **~1.9 ms** | ~2.6 ms | ~2.9 ms |
| Read back to system memory | ~7.4 ms | ~9-13 ms | ~12-16 ms |

Both start from the same acquired desktop texture, over three runs of
`report_processing_cost_of_the_gpu_path_against_readback`. The acquire is deliberately not timed —
see below.

## Host pipeline: keeping the frame on the GPU

The frame starts on the GPU, because desktop duplication hands back a texture, and it ends on the
GPU, because a hardware encoder is a GPU object. For a long time everything in between went the long
way round: read the texture back to system memory, convert BGRA to NV12 on the processor, then
upload the result into a texture for the encoder. Two bus crossings and a colour conversion on the
CPU, for a frame that never needed to leave the device.

It now goes: **captured texture -> `ID3D11VideoProcessor` -> encoder**. The video processor does the
colour conversion and the downscale in one hardware pass, on the same device that will encode the
result.

Three things this needed, none of which is guessable from the API:

* **The encoder has to run on the capture device.** Two D3D11 devices on one adapter cannot pass
  textures to each other without shared handles, so an encoder that creates its own device is back
  to system memory whatever else is done. `HardwareH264Encoder::open_on_device` exists for this.
* **Desktop duplication only lends its texture.** `ReleaseFrame` invalidates it, and the frame must
  be released before the next acquire or capture stalls, so a frame that outlives the acquire has to
  be copied first. The copy is GPU to GPU and costs a fraction of a millisecond.
* **A video processor input texture created with `D3D11_BIND_SHADER_RESOURCE` alone is refused.**
  No bind flags at all works, and so does shader resource combined with render target; shader
  resource on its own is the one combination that does not, and the rejection is "the parameter is
  incorrect" with nothing about bind flags.
  `report_which_input_texture_the_capture_device_will_accept` tries each candidate rather than
  reasoning about them, because every rejection reads identically.

### The fallback path, and why it also got faster

When the shared-device encoder or the texture pool cannot be opened, frames are read back to system
memory as before. That path reduces the frame before the bus copy, in three steps by preference:

1. **A mip chain**, for exact power-of-two halvings. Cheapest, and the narrowest: it expresses
   2560x1600 to 1280x800 and nothing in between.
2. **The video processor**, for any other ratio. This is what actually covers the common case — a
   16:10 desktop capped at 1920 is a 0.75 ratio, which no number of halvings reaches. Before this,
   such a session fell through to *no* GPU reduction at all: a full-size readback followed by a
   resize on the processor.
3. **Whole-frame readback**, when neither is available, with the session resizing afterwards.

Adding the second step nearly halved the fallback path, from about 15.6 ms per frame to about
7.4 ms: 44% fewer pixels cross the bus, and the session no longer resizes at all.

### Why the acquire is not timed

`AcquireNextFrame` blocks until the desktop changes. A measurement that includes it reports how busy
the screen is rather than how fast the pipeline is, and the two are unrelated. This project made
exactly that mistake once — a benchmark that timed the acquire reported the same configuration at
25 ms and at 80 ms on consecutive runs, and the wrong conclusion drawn from it disabled the GPU
downscale as a supposed regression for a while. Every figure in this document times the work only.

### Stage breakdown of the readback path

From `report_stage_timings`, which reduces to half size — an exact halving, so the mip chain applies
and this is the best case for that path rather than the typical one:

| Stage | Median | Worst |
|---|---|---|
| Readback | ~1.4 ms | ~1.9 ms |
| Scale and BGRA to NV12 | ~1.7 ms | ~2.5 ms |
| Encode | ~3.7 ms | ~11 ms |
| **Total** | **~6.8 ms** | |

The same test reducing on the CPU *after* readback instead totals about 12.8 ms, because the bus
copy carries four times the pixels. That comparison is the reason reduction happens before the copy
rather than after it.

## Host encoder: hardware versus software, measured

Hardware is the default and the better encoder. Software is the fallback for machines without a
hardware H.264 encoder, and for when a driver declines. `FLINT_ENCODER=software` or
`FLINT_ENCODER=hardware` overrides the choice.

Measured by `report_hardware_versus_software`, 1920x1080, five runs, single-threaded, on the
development machine (Intel iGPU driving the panel, adapter LUID 0x90002), with both encoders
producing output verified to decode into a picture:

| Encoder | Median | Worst |
|---|---|---|
| Hardware (Quick Sync, capture adapter) | ~3.1 ms | **3.8 - 4.6 ms** |
| Software (Media Foundation) | ~2.9 ms | **28 - 42 ms** |

Software wins the median by about a fifth of a millisecond and loses the tail by a factor of eight.
The tail is what matters: a 35ms frame is twice the entire 16.6ms budget, and a viewer sees it as a
stutter. Hardware also does its work on the GPU's dedicated encode block, leaving the CPU for
capture, colour conversion and the network.

### The bug that made this measurement meaningless for a while

An earlier version of this table reported hardware at a 5.4ms worst case and recommended it on that
basis. The timings were real; the encoder was not. It was emitting H.264 with correct parameter
sets, correct dimensions, correct Annex B framing and a plausible bitrate, every frame of which
decoded to a field of zeroes — which a television renders as flat green. `ffprobe` read all 59
frames without complaint. Three independent decoders agreed on the result: Media Foundation's own,
`ffmpeg`, and the MediaTek decoder in a Fire TV Stick.

**Cause.** A hardware H.264 transform is a GPU object and reads its frames from GPU memory
regardless of what its input media type advertises. Media Foundation let it accept a system-memory
NV12 type and system-memory samples without complaint, and the encoder then compressed whatever the
corresponding GPU allocation happened to contain.

**Fix.** Every frame is uploaded into an `NV12` `ID3D11Texture2D` on the encoder's own device and
handed over as a DXGI surface buffer, with the device manager attached so the transform knows where
to look. Both halves are required; either alone reproduces the green screen.

Two smaller defects were found on the way and are worth keeping fixed regardless:

* **Submitted samples were released too early.** An asynchronous transform reads a frame after
  `ProcessInput` returns, so dropping the caller's reference immediately is a use-after-free with no
  crash — the encoder compresses whatever now occupies the memory. The tell was a solid *black*
  frame encoding to 481KB while a solid white one took 32KB; both are uniform and neither should
  cost more than a few hundred bytes.
* **Direct3D 11 will not create an NV12 texture with dynamic usage.** `CreateTexture2D` answers
  "the parameter is incorrect" and names nothing. Video formats are default or staging only, so
  frames are written to a staging texture and copied on the GPU.

### The test that has to keep passing

`what_the_hardware_encoder_produces_decodes_back_into_a_picture` encodes a high-contrast pattern,
decodes it back, and asserts the picture survived. It is not ignored, needs no GPU or desktop, and
runs in under two seconds. Every structural test in the crate passed throughout the period the
encoder produced nothing but zeroes; that one did not.

### Key frames

The two paths differ deliberately. The software encoder honours an on-demand key-frame request
reliably, so it runs an effectively unbounded group of pictures and emits an intra frame only when
the receiver asks. This machine's hardware encoder does not always honour that request, so the
hardware path runs a bounded interval of two seconds instead, capping how long a receiver
recovering from packet loss can be frozen.

### Startup

Constructing a hardware encoder runs black frames through it until it publishes SPS and PPS, then
flushes them. A hardware encoder decides its parameter sets when it encodes its first key frame,
whereas Flint sends `VIDEO_CONFIG` *before* the first access unit; without priming the receiver has
no parameter sets, cannot construct a decoder, and shows a black screen with no error anywhere.

## Browser path (targets and measurement vocabulary)

Browser control latency is measured separately from mirror glass-to-glass. Do not infer one-way
latency from Windows and TV wall clocks; they are not synchronized.

### Targets (not claims)

| Span | Target | Notes |
|---|---|---|
| Host command enqueue -> TLS write complete | < 5 ms p95 on loopback | Same-host elapsed. |
| Receiver TLS read -> command validation | < 2 ms p95 | Same-clock receiver span. |
| UI-thread driver apply | < 8 ms p95 | Excludes WebView network. |
| Host Go -> BrowserState keyed by command ID | < 150 ms p95 on LAN | Round trip; warm samples only. |
| Preview interactive encode | <= 5 fps, <= 768 KiB JPEG | Resource bound, not a latency SLA. |

### Required record fields

For every physical browser measurement, record: device model, Fire OS/API level, receiver build SHA,
host build SHA, network topology, sample count, warm-up count, p50/p95/p99/max, failures, and the
correlation IDs used. Redact URLs, text, cookies, certificates, and preview pixels from normal logs.

### Measured results

| Date | Device | Span | Samples | p50 | p95 | p99 | Max | Result |
|---|---|---|---:|---:|---:|---:|---:|---|
| _pending_ | _pending_ | _pending_ | | | | | | Use `tools/scripts/test-fire-tv-browser.ps1`. |

### First device run — functional, not yet timed

2026-09-06, Fire TV Stick 4K (`AFTMM` / `mantis`), Fire OS 6.7.1.1, **API 25**, 2 GB RAM,
WebView `com.amazon.webview.chromium` **118.0.5993.155**, host on the same hotspot LAN
(`10.69.52.99` -> `10.69.52.172`, 66 ms ICMP RTT).

Confirmed working end to end:

- Browser TLS listener bound and advertised over mDNS. The Keystore identity was obtained and
  `BrowserCipherSuites.select` found a forward-secret suite on real Conscrypt — the exact failure
  the cipher policy was written against.
- Host pinned the receiver by its displayed short code and opened an HTTPS page. State flowed back
  as `Loading 0% -> 100% -> Loaded`, with the title arriving mid-load.
- Chromium 118 launched a sandboxed renderer for the receiver process.
- The TV-local cursor rendered and moved **pixel-accurately**: 5 taps up moved it exactly
  5 x `TAP_PIXELS` (120 px); 12 taps down moved it 288 px.
- The persistent browser identity survived an app reinstall — the same short code, so a host that
  trusted the receiver once does not have to verify again.

**No timing figures are recorded.** Nothing in this run was instrumented or sampled, so every span in
the table above remains a target. Do not quote the 66 ms ICMP RTT as a browser latency; it measures
the network, not the feature.

### Second device run — fullscreen, chrome, and memory

Same device. Confirmed additionally:

- **Page-requested fullscreen works.** A real HTTPS video was driven with the D-pad cursor onto
  Chromium's own fullscreen control; the video filled the screen, the control flipped to "exit",
  and Flint's chrome and cursor correctly stood down.
- **Back exits fullscreen** rather than the browser — rung 3 of the ladder, on hardware.
- **The screen is held awake while fullscreen**, confirmed by a `SCREEN_BRIGHT_WAKE_LOCK` held for
  the receiver's uid in `dumpsys power`.
- The classified error page rendered for a real failing URL, with the retry action and the
  `SITE_ERROR` sentence rather than a generic one.
- The omnibar, tab switcher and leave prompt all render and are operable with the remote.

#### Memory

| State | PSS |
|---|---|
| Receiver idle, no WebView | 67.9 MB |
| Browser open, one live WebView | 113.5 – 120.8 MB |

So **one live renderer costs roughly 46–53 MB** on this device (2 GB total RAM). ADR-0012's limit of
two live renderers therefore extrapolates to about 160–175 MB, inside the 220 MB gate.

**That two-tab figure is arithmetic, not a measurement.** A second live tab was not opened during
this run — see the open issue below — so the plateau that the freeze policy is supposed to produce
has not been observed directly. Treat the gate as unverified.

#### Open issue

~~Activating a focused control needs a second Select press…~~ **Fixed 2026-09-06.**
`tvFocus` had added its own `focusable` under `clickable`, so Select animated and dropped the halo
without firing. Chrome now uses `tvClickable` (clickable owns focus; tvFocus only paints). Holding
Select while chrome/overlays own the remote no longer flips pointer/link mode (that hid the cursor
and made Up/Down jump the page). WebView Android focus is cleared while chrome is up.

Six defects these runs found, all fixed and covered by tests:

| Defect | Cause |
|---|---|
| The CLI could open the browser only once per receiver launch, then `STALE_EPOCH` forever | `BrowseRunner` hardcoded epoch 1, and `close` records the epoch as `observedEpoch`, which the receiver then refuses at or below |
| An idle session died after 120 s while the host still showed "connected" | `SO_TIMEOUT` was treated as a liveness signal. It is a read deadline; silence after authentication is a person reading a page |
| The whole chrome was inert on the D-pad | `Modifier.tvFocus()` drew a focus ring but never made the node focusable, so no omnibar, tab-strip or switcher control could receive focus |
| The leave prompt defaulted to **Leave** | Focus was requested during first composition, before the node was placed, so it failed quietly and traversal picked the destructive button |
| New Tab was unreachable with a remote | It sat in the switcher's header with no card beneath it, and 2-D focus search needs overlap on the perpendicular axis |
| The tab switcher could not be used | Opening an overlay pulled focus back to the page surface, leaving the sheet on screen with focus behind it |

### Mosaic / multi-live evidence (Phase 0)

Targets and probe status for ADR-0019. **Do not treat targets as measurements.**

| Observation | Status | Notes |
|---|---|---|
| 1 live WebView PSS | Measured | 46–53 MB above idle (table above) |
| 2 live WebViews PSS (tabs or mosaic) | **Unmeasured** | Arithmetic ~160–175 MB vs 220 MB gate — not observed |
| 3 / 4 live WebViews PSS | **Unmeasured** | Required before raising mosaic `MAX_LIVE` |
| 2 concurrent HTML5 video decodes | **Unmeasured** | Decoder contention unknown on AFTMM |
| `VpnService.prepare` on Fire OS | Probe in software | `AndroidVpnCapabilityProbe`; hardware consent UX TBD per SKU |

**Phase 4 decision (2026-09-06):** `MosaicRegistry.MAX_LIVE` stays **2**. Raising it is blocked until
2/3/4-live PSS and multi-decode rows above are filled with real Stick numbers.

Hardware probe entry points (opt-in, ignore when no device):

- `receiver` androidTest / debug harness may call `MosaicCapability.fromProbe`
- Script: `tools/scripts/test-receiver-browser.ps1 -Mode Device` plus manual `dumpsys meminfo`
  while mosaic holds N live panes


## Flint Mobile (targets and measurement vocabulary)

**Nothing in this section is a measurement.** Glass to glass has never been measured on a phone, the
camera procedure below has not been run against any named phone-and-television pair, and no stage of
the phone's own pipeline has been timed on real hardware. Every figure here is a target, and it stays
one until a repeatable run against a named pair puts it in a measured table of its own.

The phone path is not the desktop path with a different capture source. It has stages the desktop
does not — a `VirtualDisplay` and, on the mirror path, a `MediaProjection` — and it runs on a shared
SoftAP link where the phone is the access point rather than a client, which changes what the network
term is competing with.

### Targets (not claims)

| Stage | Target | Owner | Why this bound |
|---|---|---|---|
| Compose render into the virtual display | ~8 ms | `:phone:app` | One frame at 120 Hz. The second screen draws its own content, so this is a normal Compose frame rather than a capture. |
| VirtualDisplay to encoder input surface | ~0 ms | platform | The display writes straight into the encoder's input `Surface`. There is no copy here to bound; if one appears, the surface has been wired wrongly. |
| MediaProjection to encoder input surface | ~0 ms | platform | The same, on the mirror path. |
| Encode | ~6-10 ms | platform | A hardware encoder at 1080p60 with no B-frames, `KEY_LATENCY = 1`, and `KEY_LOW_LATENCY = 1` where `FEATURE_LowLatency` is supported. Vendor variance here is large and is the main reason this is a range. |
| Frame and socket write | <1 ms | `:protocol` | `FrameWriter` writes the envelope from a reused header array and the payload straight from the encoder's buffer. Nothing on this path allocates. |
| Wi-Fi traversal, SoftAP | ~5-15 ms | network | The phone is the access point, so this is one hop rather than two. It is also the term the bitrate controller is protecting: an over-large stream degrades every other hotspot client. |
| Receiver jitter queue | ~2 frames | `:receiver:app` | `MirrorVideoDecoder` holds three frames and discards the whole queue on overflow. Two is the working depth; the third is the margin. |
| Decode | unknown | television | Not under Flint's control and not measured. On the desktop path this term is already larger than everything on the host side put together. |
| Present | unknown | television | Panel processing. Also not under Flint's control, and a television's own picture modes can add more than every other stage combined. |

The last two are deliberately blank rather than estimated. They are the two largest terms, and
guessing at them would make a total that looked authoritative and was not.

### Measurement procedure

The same procedure the desktop path uses, adapted for a phone. It is written down here so that the
slice that runs it does not have to invent it.

1. Name the pair. The phone's model and Android version, the television's model and Fire OS version,
   and the hotspot band. A figure without a named pair is not a measurement.
2. Put the phone into second-screen mode showing a full-screen surface that alternates between two
   flat colours on a known frame boundary. Second screen rather than mirror, because it has no
   capture stage and so measures the pipeline rather than the pipeline plus `MediaProjection`.
3. Film the phone and the television in one frame at 240 fps.
4. Count frames between the change on the phone and the change on the television. At 240 fps each
   frame is 4.17 ms, and that is the resolution of the result — quote it as a range, not a point.
5. Repeat at least five times and report the median and the spread. A single run is an anecdote.
6. Repeat the whole thing in mirror mode. The difference between the two is the cost of
   `MediaProjection`, which is the one term this product can actually choose to avoid.

### Required record fields

Every recorded run carries: phone model, Android version, television model, Fire OS version, hotspot
band and channel, negotiated codec, encoder name as reported by `MediaCodecInfo`, resolution, frame
rate, bitrate at the time of the run, and the thermal status. The last two matter because a run taken
while the phone was throttling is not a run of the same system.

### What must not happen

No figure moves out of this section into a measured table without that procedure and that record. No
figure from this section is quoted in the app, in the release notes, in a commit message, or in a
pull request description. The app has no latency readout at all for this reason: a number on the live
strip would be read as a measurement, and there is not one to show.
