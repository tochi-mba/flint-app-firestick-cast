# Project constraints

## Latency

- Never allocate on the frame path. Pooled buffers only; the steady-state capture → encode → send
  loop must not touch a general-purpose allocator.
- Never claim a latency figure that has not been measured on real hardware.
  `docs/LATENCY_BUDGET.md` holds measurements; targets are labelled as targets.
- No B-frames, ever. One cannot be emitted until a later frame has been encoded, which is latency
  by construction.
- Infinite GOP with IDR on demand, with one documented exception. The software encoder honours an
  on-demand key-frame request reliably and so emits an intra frame only when the receiver asks. The
  hardware encoder on this project's development machine does *not* always honour that request, and
  an unbounded GOP there means a receiver that joins late or drops a packet never gets a frame it
  can start from — a frozen picture, indefinitely. That path therefore runs a bounded two-second
  interval. Prefer on demand; fall back to a bounded interval only where the request is proven
  unreliable, and say so in the code where it is done.
- The managed shell never runs per frame. C# starts a session, polls telemetry, and stops it.
  Anything that must happen between two frames belongs in `flint-engine`.

## Honesty

- Never present an unavailable capability as a shipped one. Unavailable modes carry a neutral
  UI and are not presented as working controls.
- A capability probe reports what the hardware can do, not what the product wishes it could do.
  When the answer is "this device cannot do this", say exactly that.

## Network

- Bind every listening socket to a validated interface address. Never listen on a wildcard address.
- Bind every outgoing session socket's source address explicitly. Never bind the whole process to
  an interface, and never change the system default route.
- Never hardcode a subnet, prefix length, interface name, or address. Derive them, then bound any
  scan by what was derived.
- Serve only explicitly selected items behind a cryptographically random per-session token. A
  client must never name a filesystem path.
- Tokens are authorization, not encryption. Until an authenticated encrypted transport is
  implemented and verified, no document or UI string may call local traffic private or encrypted.

## The receiver device

- Never install, replace, or remove anything on the TV without showing the user what and why.
- Honour the device's RSA authorisation prompt. It is not an obstacle to route around.
- Offer removal wherever installation is offered.
- The receiver stays fully D-pad operable, with visible focus and 5% overscan-safe margins.
  No touch-only control, ever.

## Debugging logs (agents)

When the user reports a Fire TV / Windows browser, cast, pairing, VPN, or workspace problem —
**pull and read the latest logs before guessing**.

```powershell
.\tools\scripts\pull-dev-logs.ps1
# optional: -Serial 10.x.x.x:5555  -ClearLogcat
```

Then read, in order:

1. `artifacts/logs/session-meta.txt` — serial, pull time, receiver package timestamps
2. `artifacts/logs/firetv-latest.log` — filtered logcat (`Flint*`, `BrowserTlsServer`)
3. `artifacts/logs/windows-latest.log` — copy of `%LOCALAPPDATA%\Flint\logs\windows-latest.log`

Windows `Flint.App` opens that LocalAppData file automatically via `DevFileLog` (Avalonia
`LogToTrace` included). Continuous Stick capture (optional):
`.\tools\scripts\watch-firetv-logs.ps1`.

Granular breadcrumbs (privacy-safe) are written so a session can be reconstructed step by step:

- Windows tags via `FlintDiag` → file: `FlintCast`, `FlintTrust`, `FlintBrowser`, `FlintWorkspace`,
  `FlintVpn`, `FlintSession`, `FlintUi` (UI stall watchdog). Host-only URL views; no page text,
  pairing codes, VPN blobs, or PEMs. Pointer MOVE is throttled; DOWN/UP, scroll, keys, and text
  length are kept.
- Fire TV tags: `BrowserTlsServer` (listen / hello / pairing / session lifecycle), `FlintBrowser`
  (commands, input accept/reject, surface), `FlintWorkspace`, `FlintVpn*`, `FlintReceiver`.
- Avalonia `[Binding]` TRACE spam is filtered out of the Windows file so signal stays readable.
- The Windows file is hard-capped (`DevLogRetention`: 8 MiB max, keep newest 6 MiB when over).
  Oldest lines are dropped first; the file is never left unbounded.

Log rules: never write or quote VPN configs, private keys, cookies, passwords, certificate PEMs,
preview pixels, or page text. Prefer tags and UI-safe reasons. Logs under `artifacts/` are
gitignored.

## Testing

- Wire-format changes require regenerated golden vectors and a passing cross-language check.
  Rust, C# and Kotlin must agree byte for byte.
- Hardware-dependent tests are marked `#[ignore]` and skip cleanly when the hardware is absent. The
  default test run must pass on a machine with no GPU and no Fire TV attached.
- A codec test that only checks structure is not a codec test. An encoder can produce correctly
  framed H.264 with correct parameter sets, correct dimensions and a plausible bitrate, every frame
  of which decodes to nothing — that shipped here, and looked like a working encoder to every
  structural assertion in the crate. Anything that produces pixels is held to a round trip that
  decodes the output and asserts a picture survived.
- A latency regression fails the build the same way a broken test does.
