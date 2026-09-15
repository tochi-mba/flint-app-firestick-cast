# Flint

**A REX Technologies product.**

Flint is an in-progress Windows host designed to cast a PC screen to a Fire TV on the same network
at the lowest latency the hardware allows. It carries the REX Technologies ink/signal visual system.

This repository is under active construction. The current `0.1.0` build includes:

- **screen mirroring**, proven end to end on a Fire TV Stick: Desktop Duplication capture, colour
  conversion and downscale on the GPU's video processor, hardware H.264 encode, and a receiver that
  decodes and renders it;
- **local media handoff**, pushing a selected file to the receiver over the control channel and
  playing it at original quality, with no transcode;
- a Fire TV receiver built with Compose for TV: fully remote-operable, with visible focus and
  overscan-safe margins;
- a Windows application shell with the REX ink/signal visual system, and a first-run
  walkthrough that states the Vega limitation before asking anyone to set anything up;
- a device capability prober that identifies Fire TV hardware and reports, per device pair,
  which Flint modes are actually possible;
- native DXGI display-adapter and Media Foundation hardware-encoder probing;
- authenticated, read-only ADB identification that honours the television's RSA consent prompt;
- a Diagnostics page and headless CLI that expose the evidence behind each verdict;
- the REX wire protocol implemented in Rust, C# and Kotlin, held byte-identical by a committed
  cross-language vector corpus;
- connection-aware screens that do not present unavailable casting modes as working controls.

Second-screen output — using the television as an extra desktop rather than a copy of this one — is
not built. It needs an indirect display driver that Windows will install only once it carries a
signature the machine already trusts, and Flint will not ask anyone to disable driver signing to
work around that.

Glass-to-glass latency has not been measured. The host path has: see
[Latency Budget](docs/LATENCY_BUDGET.md) for the figures and for the distinction between the two.
See [Engineering Notes](docs/ENGINEERING_NOTES.md) for platform limits and the implementation
boundary, and [Latency Budget](docs/LATENCY_BUDGET.md) for what has and has not been measured.

## Install

Windows 10 or 11, 64-bit. Download the zip from the
[rolling Windows release](https://github.com/tochi-mba/flint-app-firestick-cast/releases/tag/latest-windows),
which every merge replaces with the newest build, extract it, and run `Flint.App.exe`. Everything it
needs is included; there is no .NET runtime to install first.

Check your Fire TV before you bother: **Settings → My Fire TV → About**. Fire OS 7 or 8 works.
Vega OS cannot, and no future version will change that.

Full walkthrough, including the unsigned-build warning Windows will show you, is in the
[installation guide](docs/INSTALL.md). To build a package yourself:

```powershell
./dev.ps1 package     # produces dist/Flint-<version>-win-x64.zip
```

## Why this shape

Casting to a TV has two halves with opposite requirements. The frame path — capture, colour
convert, encode, packetize, send — must never pause, so it is Rust with no allocation in the loop.
The shell around it — discovery, pairing, installation, diagnostics, UI — is never in the frame
path, so it is C# where the tooling and the tests are better.

Flint does not use Miracast. Miracast is a fixed pipeline whose latency Flint cannot influence;
the whole point of a custom receiver is control over the decode path.

## Modules

| Module | Role |
|---|---|
| `apps/windows/engine` | Rust engine; native adapter/encoder probing and the wire codec |
| `apps/windows/src/Flint.App` | Avalonia desktop shell and the REX visual system |
| `apps/windows/src/Flint.Core` | Device model, capability report, session configuration |
| `apps/windows/src/Flint.Discovery` | Fire TV discovery, authenticated read-only ADB identification, and path sampling |
| `protocol/dotnet/Flint.Protocol` | The REX wire protocol: framing, messages, and a strict codec |
| `apps/windows/src/Flint.Engine.Interop` | The small managed FFI boundary to `flint-engine` |
| `apps/windows/src/Flint.Cli` | Headless probe runner, for capturing a report into a bug thread |
| `apps/receiver/app` | Fire TV decode and render: Compose for TV surfaces, MediaCodec, and Media3 playback |
| `protocol/kotlin` | The REX wire protocol in Kotlin, sharing the committed vector corpus |
| `site` | The GitHub Pages site, published by CI from `main` |

## Running the probe

The desktop app has a Probe button on the Cast page. The same probe runs headlessly:

```powershell
dotnet run --project apps/windows/src/Flint.Cli          # full capability report
dotnet run --project apps/windows/src/Flint.Cli -- --services   # what is advertising on this network
dotnet run --project apps/windows/src/Flint.Cli -- --address 192.168.1.42
```

The `--services` scan exists to separate two failures that look identical from the Cast page: a
television that is off or on another network, and a network where multicast never reaches this
machine at all. If it lists nothing, the problem is client isolation, a firewall on UDP 5353, or a
guest network — not Fire TV. Networks that suppress multicast can still use the direct-address
field in the Cast page or `--address`; leaving `--port` out checks only the bounded 5555–5585 range.

### Scripting it

`--json` prints the whole verdict as one object on stdout and moves the banner and progress lines
to stderr, so the stream pipes straight into a parser without being cleaned up first:

```powershell
dotnet run --project apps/windows/src/Flint.Cli -- --json --address 192.168.1.42 | ConvertFrom-Json
```

The object carries a `schema` number, and enums are written as names rather than ordinals so a
value inserted into an enum does not silently change what a script reads. Two fields exist to
separate absence from failure, and are worth branching on: `host.encodersProbed` distinguishes "no
encoder" from "never looked", and `path.throughputMeasured` distinguishes a slow network from one
that was not measured.

Exit codes are a contract — `tools/scripts/*.ps1` and CI branch on them:

| Code | Meaning |
|---:|---|
| 0 | Success |
| 2 | The probe did not finish in time |
| 3 | The mirror ran but encoded no frames |
| 4 | This PC cannot mirror |
| 64 | Bad command line (`sysexits.h` EX_USAGE) |
| 69 | The receiver is not offering that service (`sysexits.h` EX_UNAVAILABLE) |

`--version` prints the build, including the source revision when one was stamped, which is what a
bug report should quote. `--help` prints the full option list and this table.

## The mark

Flint's icon is a struck spark on the REX ink ground, inside the same off-white square frame REX
Cast uses for its signal bars. The two read as siblings on a taskbar, which is the point.

It is generated rather than checked in as an opaque binary, so the palette stays tied to the one
the UI uses:

```powershell
python tools/scripts/generate-icon.py
```

Icons at or below 32 pixels drop the frame and draw the spark larger, because at that size the
frame collapses into a smudge.

## Device support is conditional

Fire TV devices released from October 2025 — the Fire TV Stick 4K Select and the 2026 Fire TV Stick
HD — run **Vega OS**, which is not Android and cannot install APKs by any method. On those devices a
custom receiver is impossible, and so is Flint's mirroring path.

Android-based Fire OS devices can install a receiver. Amazon currently maps Fire OS 5, 6, 7, 8, 14
and 16 to documented Android API levels; Flint recognises only those published mappings and leaves
an unfamiliar level unknown. It does not guess or claim that an untested model works.

## Build

Requirements: PowerShell 7, the .NET 10 SDK, Rust with the MSVC build tools (the exact release is
pinned in `rust-toolchain.toml`), JDK 17 and Android SDK platform 36 for the Android apps, and
Python 3 for the site. `./dev.ps1 doctor` checks them and prints the command that installs anything
missing.

```powershell
./dev.ps1 doctor    # what this machine is missing, and how to install it
./dev.ps1 check     # every gate CI runs: formatting, analyzers, tests and source rules
./dev.ps1 help      # every other task
```

`check` takes areas, such as `./dev.ps1 check windows engine`, so a change to one app runs only its
own gates. The Windows app and the Rust engine build on Windows; the Android apps and the site build
on any platform. The .NET tests write Coverlet reports under `TestResults`; no .NET coverage floor is
enforced yet.

## Privacy

No account, analytics, ads, cloud relay, or crash-reporting SDK. Current network activity is local
discovery and read-only device probing. Future session tokens will authorize a receiver; they will
not by themselves encrypt the stream, and nothing in this project describes local traffic as
private or encrypted.

## Current Status

The connection report, local diagnostics, wire protocol, and Windows package are available now.

Available: capability reporting, direct Fire TV connection, diagnostics, and the portable Windows
package.

Unavailable: receiver installation, screen mirroring, media handoff, and second-screen output.

## Authorship

Flint is designed and built by **REX Technologies**, and shares the REX ink/signal
visual system and engineering conventions with REX Cast. Application identifiers use the
`com.rexflint` namespace, parallel to REX Cast's `com.rexcast`; reserve them before any store or distribution work, because
package identity cannot be changed after a listing exists.

Flint is independent software and is not affiliated with Amazon, NVIDIA, Microsoft, or any
device manufacturer.

## License

MIT. Copyright (c) 2026 REX Technologies. See [LICENSE](LICENSE).
