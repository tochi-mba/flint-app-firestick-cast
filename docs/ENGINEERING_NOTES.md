# Engineering Notes

This document separates platform fact, current implementation, and product intent. The distinction
is part of the product: Flint must never promise a capability merely because an API exists.

## Current boundary

Version `0.1.0` mirrors a Windows desktop to a Fire TV Stick and hands it local media files, both
proven end to end on real hardware (AFTMM, Fire OS 6.7.1.1).

It finds advertised Fire TV devices on the local network, identifies an authorised Android device
through ADB build properties, foregrounds the installed Flint receiver through that same authorised
connection, and negotiates a paired TCP session. Media files are **pushed down that connection** in
ordered chunks rather than fetched by the receiver - see the protocol notes for the Fire OS
behaviour that forces this. Desktop capture, colour conversion, hardware H.264 encoding and frame
transmission are implemented; the receiver decodes and renders them.

It also recognises documented Vega build models, enumerates host encoders and display adapters,
samples TCP-connect round-trip behaviour, and reports which modes the pair could support.

Second-screen output is not implemented. Glass-to-glass latency has not been measured; the host path
has, and `docs/LATENCY_BUDGET.md` keeps the two apart.

## Fire TV device families

Fire OS 5, 6, 7, 8, 14 and 16 are Android-based. They expose ADB and permit sideloading. The prober
reads the Android release and API level, then assigns a Fire OS generation only where Amazon
publishes that API mapping. It does not mislabel Android's `ro.build.version.release` property as the
Fire OS software-version number.

Being Android is not the same as being able to run Flint's receiver, and the two were conflated until
a device class fell through the gap. Fire OS 5 is Android 5.1, API 22; the receiver is built with
`receiver-min-sdk = 25`, so a Fire OS 5 television refuses the package outright with
`INSTALL_FAILED_OLDER_SDK` however the install is attempted. It is therefore reported as impossible
rather than installable, with its own verdict: Vega is not Android and never will be, while a Fire OS
5 device is Android and simply too old, and neither carries a remedy because no setting on the
television and no future version of Flint can lower a package manager's floor. The floor is a single
constant in `:phone:core` pinned by a test to the catalogue value, so raising `receiver-min-sdk`
fails the build until the copy and the verdicts move with it.

Vega OS is a Linux system Amazon built in-house. It is not Android, it does not run APKs, and no
sideloading path exists - not ADB, not Downloader, not a sideload helper app. Devices known to ship
with it are the Fire TV Stick 4K Select (October 2025) and the Fire TV Stick HD (2026). On these
devices Flint's mirroring and second-screen capabilities are impossible, and the prober says so
rather than failing later with a confusing install error.

The product must list tested models rather than claim universal Fire TV support.

ADB over network appears across a bounded `5555–5585` port range rather than only `5555`, so the
probe scans that range on devices first found through multicast advertisement or on one address the
user explicitly enters. It never widens a direct-address request into a subnet sweep. The RSA
authorisation prompt on the television is mandatory and is not routed around; Flint keeps a stable
per-user RSA key so an accepted identity survives application restarts.

## Capture on hybrid-GPU hosts

DXGI Desktop Duplication requires the capturing process to run on the adapter that drives the
display being captured. On hybrid laptops the desktop is commonly driven by the integrated GPU while
the discrete GPU holds the better encoder, so the naive pairing - capture and encode both on the
discrete GPU - fails or silently falls back.

Windows Graphics Capture works across adapters and is what the capability prober prefers when the
two differ. **The engine implements Desktop Duplication only**, so a host whose capture and encode
adapters are different devices is reported honestly by the prober and is not yet served by the
engine; on this project's development machine they are the same device and duplication is correct.

Duplication also lends rather than gives: `ReleaseFrame` invalidates the texture it handed out, and
the frame must be released before the next acquire or capture stalls. Anything that outlives the
acquire has to be copied first, which is a GPU-to-GPU copy and cheap.

### Keeping the frame on the GPU

A captured frame starts in video memory and a hardware encoder reads from video memory, so the
whole path can avoid system memory entirely: the video processor converts BGRA to NV12 and scales in
one hardware pass, and the encoder reads the result. Measured at 1920x1200 this is about 1.9ms per
frame against about 7.4ms for reading the frame back, converting on the processor and uploading it
again.

Two constraints make or break it. The encoder must run on the *capture* device, because two D3D11
devices on one adapter cannot pass textures without shared handles. And a video processor input
texture created with `D3D11_BIND_SHADER_RESOURCE` alone is refused - no bind flags works, shader
resource with render target works, shader resource alone does not - with a rejection that names no
parameter.

Neither API is a guarantee of frame delivery. Both can stall under full-screen exclusive
applications, during display topology changes, and across secure-desktop transitions (UAC, lock
screen). These are normal state transitions and must be handled as such, not treated as errors.

## Encoder selection

NVENC, AMF and Quick Sync are all capability-probed rather than assumed from vendor strings. A
present GPU does not imply a present encoder: encode support varies by silicon generation, by
codec, and by driver version, and virtualised or remoted sessions may expose none.

Low-latency encoder configuration is not a single switch. The settings that matter are an
ultra-low-latency tune, constant bitrate rate control, zero B-frames, an infinite GOP with IDR
frames requested on demand, and slice-based output so packetization overlaps encoding. Preset
choice trades compression against encode time; higher presets are not better for this workload.

Ordering matters as much as the values. Low-latency properties are modifiable only *before* a media
type is committed and rate-control properties only *after*; set on the wrong side of `SetOutputType`
each is accepted with a success code and changes nothing. Setting the low-latency flag late cost
eighteen frames of startup delay here before it was noticed.

The on-demand IDR is not universally honoured. This machine's hardware encoder ignores the request
often enough that an unbounded GOP would leave a recovering receiver frozen indefinitely, so that
path runs a bounded two-second interval instead. Prefer on demand; measure before trusting it.

Finally, a hardware encoder that configures without error is not a working encoder. One shipped here
that produced correctly framed H.264 with correct parameter sets, correct dimensions and a plausible
bitrate, every frame of which decoded to a field of zeroes - a flat green television. Anything that
produces pixels is held to a round trip that decodes its output and asserts a picture survived.

## Decoder latency on the receiver

`MediaFormat.KEY_LOW_LATENCY` (Android 11+) and real-time priority are optional hints. Decoders may
reject or ignore them, so capability checks and a tested fallback are required rather than assumed.

Amazon's Amlogic-based Fire TV hardware has historically needed a vendor-specific path
(`OMX.amazon.fireos.index.video.lowLatencyDecode`) in addition to the standard key, and some Fire TV
generations have shipped decoders with latency measured in seconds rather than milliseconds. The
receiver must measure its own decode latency and report it in `StatsMessage` so the host can show
the truth instead of an assumption.

Rendering goes direct to a `SurfaceView`. A general-purpose player's buffering strategy is correct
for playback and wrong for mirroring.

## Transport

Video moves over UDP because a retransmission that arrives after its frame's display deadline is
worse than the loss it repairs. Forward error correction covers ordinary loss; a lost frame beyond
FEC's reach triggers an IDR request rather than a stall.

Control traffic moves over a reliable channel. The two must not share a socket's fate.

Pacing is driven by the display's vertical blank rather than a fixed timer, and the encoder is fed
at the rate content actually changes rather than at a nominal frame rate.

## The television is usually the bottleneck

A TV's own picture processing commonly adds more delay than the entire Flint pipeline. Game Mode,
which bypasses most of it, has a larger effect on perceived latency than any host-side tuning.
Flint measures glass-to-glass, attributes what it can, and never reports a figure that quietly
excludes the panel.

## Second screen

An extended desktop requires an indirect display driver (IDD). Unsigned drivers require test-signing
mode, which is a material change to the user's machine and is never enabled silently.

Windows 11 24H2 and 25H2 currently cannot set an IDD display as the primary display. This is a
platform limitation, not a Flint defect, and the implementation accounts for it. Depending on an
existing signed community driver is evaluated before writing and signing one.

## Developer session logs

For agent-assisted debugging, Windows `Flint.App` writes Avalonia/Trace output to
`%LOCALAPPDATA%\Flint\logs\windows-latest.log` (`DevFileLog`). The Fire TV receiver already logs
under `Flint*` / `BrowserTlsServer` tags. Snapshot both into the repo with:

```powershell
.\tools\scripts\pull-dev-logs.ps1
```

Outputs land in `artifacts/logs/` (`firetv-latest.log`, `windows-latest.log`, `session-meta.txt`),
which is gitignored. See `AGENTS.md` (“Debugging logs”). Do not put secrets in these streams.

## Measuring the managed paths

`flint perf` times the C# paths that run on every wire message, every change to the queue and every
drop: encoding and decoding a 64 KiB video frame, adding, walking and shuffling a queue of 2,000
files, opening a dropped tree of 6,000 files (2,000 kept), sorting 2,000 names, judging 10,000
file types, ten settings updates, and a thousand ticks of the playback bar. Each path is warmed
up, then timed run by run, and the median and 95th percentile are kept, not the mean, so one
garbage collection cannot move the answer.

`./dev.ps1 perf compare` builds Release, runs it, and compares the medians with
`tools/perf/baseline.json`. A path fails when it takes more than half as long again and more than
twenty microseconds longer. Unchanged code measured on a laptop drifts by a fifth or more between runs
as its clock follows heat and power, so a tighter ratio fails on nothing, while the regressions this
is for (a lookup that turns into a walk, a sort that copies) are several times slower. The floor
keeps paths measured in microseconds from failing on noise. Every path is warmed up for 300 ms
before it is timed, so the runtime's second, optimised compilation is what gets measured, and is
timed in three rounds of which the best is kept. The report names the PC only by an opaque
profile (a hash of its name, processor count, Windows build and architecture), so the committed
baseline does not publish a machine name. A baseline from another profile, runtime, schema or a
Debug build is refused rather than compared, because the comparison would mean nothing. Measure a
baseline on the PC that will compare against it with `./dev.ps1 perf baseline`, and commit it with
the change it measures.

These numbers describe one PC on one day. They are not claims about Flint's speed, and none of them
is a latency: capture, encoding and the television are measured separately, by the engine's
hardware tests and the procedure in `LATENCY_BUDGET.md`.

## Reconnecting without a code

At pairing, the TV's Flint app grants a session token in its AUTH reply, and accepts that token in
place of the pairing code for as long as its listener runs. Windows keeps it in `known-tvs.json`,
protected with Windows data protection for the current user, under the name the TV gave in its
HELLO. `CastSession.ConnectWithTokenAsync` reads the TV's HELLO before it sends anything, and refuses
to present the token to a TV giving another name: an address a router hands out can pass to another
device.

- **On start**, with "Reconnect to the last TV" on, Flint probes the remembered address and logs in.
  If nothing answers there it searches the network, because a TV's address can change.
- **After a drop**, with "Keep trying" on, it tries after 1, 2, 4 and 8 seconds and then every 15,
  until the configured time is up or the person cancels. A disconnect the person asked for is never
  retried.
- **A refused token** means the TV's app restarted or someone pressed New code. Flint forgets the
  token, says the TV needs its code, and opens the TV app over ADB when it can.
- **After reconnecting**, it offers to carry on with what was on the TV: a file from where it was,
  or the screen. "Carry on" does it without asking, but a screen share always counts down first,
  where it can be seen and stopped.

The token is regenerated whenever the TV app's listener restarts, so after the TV reboots the first
connection needs the code once. Making the TV remember a PC across restarts would take a TV app
change. The handshake itself is limited to ten seconds, so a device that takes the connection and
never answers no longer leaves pairing waiting forever.

## Choosing a display, picture modes and the frame-rate cap

Displays are numbered by one walk over DXGI adapters and their outputs (`capture/outputs.rs`), and
both the list the Screen page shows (`flint_probe_outputs`) and capture itself (`find_output`) use
that walk, so the index a person picks is the index capture opens. Windows' display configuration
supplies each monitor's own name and its device path. The path is what a chosen display is
remembered by, because it survives replugging where the `\.\DISPLAYn` name does not. A laptop's own
panel reports no name and is called "Built-in display".

The picture modes live in one table, `ScreenQualityPreset`, and the sentence under each mode is
built from the same numbers a share starts with. The picture is never wider than the screen size
the TV reports in its HELLO.

`MirrorSession` holds a share to its frame rate. A frame that arrives too soon is kept, a newer one
replaces it, and it is sent the moment the interval has passed, on a tick whose wait is cut to the
time left. Sends keep a fixed cadence, so rounding each wait up to a whole millisecond does not
slowly lower the rate. Before this, a 144 Hz display sent up to 144 frames a second.

Changing the display or the mode while sharing is applied in place: the runner, on its own worker
thread, disposes the engine session, starts one with the new options and sends a new `VIDEO_CONFIG`,
without a second surface message. The TV rebuilds its decoder and stays on the mirror surface. If the
new options cannot be captured, the share starts again with the old ones and the page says why.

The live numbers on the Screen page and on Diagnostics are counters: frames and data sent each
second over at least a second, the picture size, whether the graphics card or software encodes, and
the TV's own dropped frames and queue. If the TV keeps dropping frames for ten seconds, the page
suggests Data saver; it never changes the picture by itself.
