# Installing Flint

A REX Technologies product. Windows only for now.

## Before you start

Check your Fire TV first. It takes ten seconds and it decides whether the rest of this page is
worth your time.

On the television: **Settings → My Fire TV → About**.

| What it says | What that means |
|---|---|
| Fire OS 7 or Fire OS 8 | Good. Continue. |
| Vega OS, or no Fire OS version at all | Stop. Flint cannot work on this device, and no future version will change that. |

Vega OS is a Linux system Amazon built in-house. It is not Android, it cannot install apps from
outside Amazon's store by any method, and Flint's whole approach depends on installing a receiver.
Devices known to ship with it are the **Fire TV Stick 4K Select** (October 2025) and the
**Fire TV Stick HD** (2026).

## What you need

- Windows 10 or 11, 64-bit
- A Fire TV on the same network as this PC — the same Wi-Fi name, and not a guest network
- Nothing else. The download includes everything it needs; there is no .NET runtime to install
  first

## Install

1. Download `Flint-<version>-win-x64.zip` from the
   [latest release](https://github.com/rex-technologies/flint/releases/latest).
2. Right-click the zip, choose **Extract All**, and pick somewhere you can find again —
   `C:\Program Files` is not required and not recommended, because Flint does not need it.
3. Open the extracted folder and run **Flint.App.exe**.

There is no installer. Flint is a portable folder: it writes nothing outside its own directory
except one marker file recording that you have seen the introduction, under
`%LOCALAPPDATA%\REX Technologies\Flint`. To uninstall, delete the folder.

The download includes `Flint.Receiver.apk`. Install it on the Fire TV once using your normal
sideloading method. After the television authorises Flint over ADB, `OPEN RECEIVER ON TV` and
`PAIR WITH TV` bring the receiver to the foreground automatically. You do not need Android Studio
or the Android SDK to use Flint after that initial installation.

## Windows will warn you

The first time you run it, Windows shows:

> **Windows protected your PC** — Microsoft Defender SmartScreen prevented an unrecognised app
> from starting.

Click **More info**, then **Run anyway**.

This happens because the build is not code-signed. A signing certificate costs money and has to be
tied to a verified legal identity, and Flint does not have one yet. The warning is Windows telling
you the truth: it does not recognise this publisher. If that is not a trade you want to make,
[build it from source](#build-it-yourself) instead — the result is the same program, compiled by
you.

## First run

Flint opens on a five-step introduction. It is worth reading once; the second step is the Fire OS
check above, and the fourth explains what to do if your network hides devices from each other.

At the end it probes your network and shows you a report: what your PC can encode, what your
television is, how good the path between them is, and which modes that combination could support.

### If it finds nothing

Some networks stop devices from seeing each other. Guest networks, most corporate networks, and
access points with client isolation enabled all block the broadcast Flint listens for.

1. Check that this PC and the television are on the same Wi-Fi network, and that it is not a guest
   network.
2. If it still finds nothing, get the television's address from **Settings → My Fire TV → About →
   Network** and type it into the **direct address** field on the Cast page.
3. To confirm whether the network is the problem, open a terminal in the Flint folder and run
   `flint.exe --services`. If it lists nothing at all, no device on your network is reachable by
   discovery, and the network is the cause rather than the television.

### Turning on ADB debugging

Flint identifies your television over ADB and uses that authorised connection to open its receiver
when you pair. It does not change Fire TV settings, install packages, or access your media files
without an explicit action in Flint.

1. On the television: **Settings → My Fire TV → Developer Options**.
2. If Developer Options is not there, open **About** and press the Select button seven times on the
   device name.
3. Switch on **ADB debugging**.
4. When Flint connects, the television shows an authorisation prompt. Accept it. Flint identifies
   itself as `flint-rex-technologies` on that prompt.

## What Flint does today

Flint reports the truth about your hardware, mirrors your screen to a paired receiver, and hands
selected local files to it for full-quality playback. Second screen — using the television as an
extra desktop rather than a copy of this one — is the one mode that is not built yet.

| Mode | Status |
|---|---|
| Capability reporting | Working |
| Screen mirroring | Working after pairing |
| Local media handoff | Working after pairing |
| Second screen | Coming soon |

Second screen needs an indirect display driver, which Windows will only install once it carries a
signature the machine trusts. Flint will not ask you to disable driver signing or to install a
certificate authority to work around that, so the mode is shown as coming soon and its control
stays disabled until a properly signed driver ships. Everything else works without a driver.

See the [latency budget](LATENCY_BUDGET.md) for what has and has not been measured.

## Build it yourself

Takes about two minutes on a machine that already has the tooling.

Requirements:

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Rust](https://rustup.rs) with the MSVC toolchain
- Visual Studio Build Tools with the **Desktop development with C++** workload, which Rust needs to
  link on Windows

```powershell
git clone https://github.com/rex-technologies/flint.git
cd flint
./scripts/build.ps1        # builds and runs every test
./scripts/package.ps1      # produces dist/Flint-<version>-win-x64.zip
```

`build.ps1` is the same command CI runs. If it passes, the package will build.

## Privacy

No account, no cloud, no analytics, no crash reporting. Network activity is local discovery and
local session traffic only.

Session tokens authorise a receiver; they do not encrypt anything. Nothing in this project
describes local traffic as private or encrypted, because it is not yet.

## Licence

MIT. Copyright (c) 2026 REX Technologies.
