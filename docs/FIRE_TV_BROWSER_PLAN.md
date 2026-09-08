# Fire TV-resident browser controlled from Flint — detailed implementation plan

**Status:** planned; no browser capability is shipped or implied by this document.  
**Audience:** Flint maintainers.  
**Prepared:** 2026-09-04.  
**Decision records:** [Fire TV browser ADR index](fire-tv-browser-implementation/adrs/README.md).  
**Companion research record:** [`report-source.md`](../report-source.md).

## Executive decision

Build a **single-tab browser inside the Android Fire TV receiver**. The Fire TV executes every
network request, JavaScript task, cookie/session operation, authentication flow, and pixel render.
The Windows Flint app is a native remote-control and status surface; it must never create a second
browser engine, proxy web traffic, relay a DOM, or inject page JavaScript.

The experience is deliberately a remote browser, not a remote desktop:

- The TV is the canonical display and directly visits the sites the user opens.
- Flint can show an **opt-in, bounded, low-rate visual preview** of that TV WebView. The preview is
  a convenience for operating the TV from the desk, not a promise of full remote-desktop fidelity.
- Browser commands, typed text, and previews travel only on a new **certificate-pinned TLS browser
  session**. The existing paired cast session remains authorization-only and is never a fallback for
  browser text, passwords, URLs, or preview frames.
- The first release is a secure, one-tab, HTTPS-only browsing surface. It has no uploads, downloads,
  popups/tabs, external intents, web permissions, native JavaScript bridge, DRM promise, or
  Appstore-public release claim.

This decision is technically feasible on Android-based Fire OS, but not automatically on every Fire
TV. The current AFTMM-class target is Fire OS 6 / Android API 25 with 1.5 GB RAM, while Vega OS
cannot run the Android receiver at all. A real-device spike is therefore a hard gate, not a
checkbox. [Amazon device specifications](https://developer.amazon.com/docs/device-specs/device-specifications-fire-tv-streaming-media-player.html?v=ftvsticklite)

Amazon also explicitly restricts publication of third-party browser apps on Fire TV. Until Amazon
gives written approval, this is a **personal/sideload developer feature**, visible as such in the
product and documentation; it must not be disguised to pass Appstore review. [Amazon device filtering policy](https://developer.amazon.com/docs/app-submission/device-filtering-and-compatibility.html)

## Product contract

### What users get

1. They pair a Fire TV receiver from the Flint Windows app as they do today.
2. In the new **Web** page, they open a secure browser session by comparing a short certificate
   fingerprint shown on both the TV and PC, then enter the current TV pairing code into the PC.
3. They enter an HTTPS address in Flint. The receiver’s WebView loads it directly from the TV.
4. Flint immediately shows the canonical URL, title, load state, back/forward state, and errors.
5. The user can operate the page using URL controls, a large D-pad, semantic keys, a deliberate
   text-send control, and (after capability validation) click/drag/scroll on a live preview.
6. The TV remains usable if the PC is absent: it has visible focus, standard D-pad navigation, and
   a reliable Back route out of browser mode.

### Explicit non-goals for v1

- No Windows-side browser, browser synchronization, HTTP proxy, cloud relay, or remote DevTools.
- No unrestricted remote automation, arbitrary JavaScript, `addJavascriptInterface`, message bridge,
  accessibility-service injection, or screen-recording workaround.
- No incognito promise, password manager, file upload, download, printing, sharing, external
  application intents, custom protocols, notification permission, camera/mic/location permission,
  page-initiated pop-up/new-window support, or browser extensions.
- No promise that Netflix, Prime Video, YouTube, DRM/EME content, payment pages, every login
  provider, or every website works. Fire TV itself warns that WebView video can retain media
  resources across pause/stop and destabilize navigation. [Amazon Fire TV FAQ](https://developer.amazon.com/docs/fire-tv/faq-general.html)

> **Two of these non-goals have been superseded.** User-initiated tabs are now in scope, bounded to
> 8 with at most 2 live renderers — see
> [ADR-0012](fire-tv-browser-implementation/adrs/ADR-0012-USER-INITIATED-BOUNDED-TABS.md). The
> page's own fullscreen request is now honoured — see
> [ADR-0013](fire-tv-browser-implementation/adrs/ADR-0013-PAGE-REQUESTED-FULLSCREEN.md). Neither
> changes the ban on page-initiated pop-ups, and neither is a promise that a given site plays; that
> remains a tested observation matrix.
- No Vega OS support. Its capability verdict remains an honest unavailable state until a separate
  native Vega receiver and browser feasibility project exists.

### Why the reverse preview is bounded rather than a video stream

An interactive PC view needs feedback; commands alone leave the TV as the only visual display. A
reverse H.264 remote-desktop pipeline would be a second media system on a 1.5-GB API-25 stick and
would delay the useful browser. The first implementation instead sends a latest-only JPEG preview:

| Property | Chosen rule |
|---|---|
| Default | Off until the user enables it for the current secure browser session. |
| Target | At most 960×540, JPEG, quality tuned in the hardware spike. |
| Rate | At most 5 fps while actively interacting; at most 1 fps otherwise. |
| Size | Hard cap 768 KiB after compression; oversized candidates are downscaled once or dropped. |
| Queueing | One latest-only slot; never queue a backlog, retry a dropped frame, or block WebView/UI work. |
| Lifetime | Memory only on both ends; cleared/disposed when the session, page, or preview toggle ends. |
| Failure | Report `Preview unavailable`; browsing and TV-local D-pad controls continue. No capture bypass is attempted. |

The first physical-device spike decides whether `WebView.draw(Canvas)` produces usable pixels on the
actual Fire OS provider. If it cannot, the product remains a TV-canonical remote with state/control
only; it does **not** silently switch to MediaProjection, an accessibility service, or a hidden
screen recorder.

## Hard gates before any feature is offered

| Gate | Passing evidence | Failure behaviour |
|---|---|---|
| Android receiver | Capability probe confirms installable Android-based Fire OS, not Vega. | Web page says the receiver platform cannot host this feature. |
| Appstore policy | Written Amazon approval if public Appstore distribution is requested. | Keep it sideload/developer-only; no misleading packaging or marketing. |
| WebView smoke test | Actual target loads the controlled local fixture and a public HTTPS page; reports title/history; survives D-pad Back and app pause/resume. | Browser is unavailable for that device/provider. |
| Secure channel | TLS 1.2+ handshake, certificate fingerprint comparison/pinning, pairing-code auth inside TLS, and no plain fallback. | All browser controls are unavailable; media/mirror remain unchanged. |
| Preview | Actual Fire TV produces bounded nonblank pixels from the WebView without memory/resource regressions. | Offer state/D-pad mode only, with preview disabled and clearly labelled. |
| TDD/coverage | All new Flint-owned logic meets the gates in “Quality and coverage contract”; protocol vectors agree in Rust, C#, and Kotlin. | Build fails. |

## Architecture

```text
                                      Direct HTTPS to requested websites
                                                     │
                                                     ▼
Windows Flint                         Fire TV receiver (Android Fire OS)
──────────────                         ─────────────────────────────────
BrowserPage + BrowserViewModel         ReceiverActivity / Compose
  address, D-pad, text, preview          AndroidView<WebView> ── WebView renderer
          │                                      ▲        │
          │ BrowserCommand / BrowserInput        │        │ WebView callbacks,
          ▼                                      │        │ capture candidate
BrowserSession over pinned TLS 1.2+               │        ▼
          │                                      BrowserCoordinator
          └──────── encrypted LAN ───────────► Browser TLS listener
          ◄──────── state + latest preview ───┘

Existing CastSession (plain, authorized) remains separate for current media/mirror.
It never carries BrowserCommand, BrowserInput, BrowserState, BrowserPreview, or browser text.
```

### Ownership and thread rules

- `ReceiverService` owns browser **session state**, authorization, network listeners, and a pure
  browser command reducer. It never owns a `WebView`, `Activity`, `Context` tied to an activity, or
  a `Bitmap` tied to a dead surface.
- `ReceiverActivity` / `ReceiverScreen` owns the actual `WebView` via `AndroidView`. All WebView
  creation, navigation, input dispatch, capture, pause/resume, detach, and destruction occur on its
  main/UI thread. Android requires WebView APIs to be used on the creating thread. [WebView API](https://developer.android.com/reference/android/webkit/WebView)
- A `BrowserWebViewDriver` translates immutable `BrowserEffect`s from the coordinator into WebView
  calls and sends sanitized `BrowserObservation`s back. It is replaceable with a fake in unit tests.
- A single receiver writer serializes browser frames. Reliable state/prompt/error messages have a
  small bounded FIFO; preview uses a one-slot overwrite mailbox. There is never one coroutine/task
  per preview frame.
- Browser, media player, mirror, and presentation are mutually exclusive surfaces. Switching away
  stops loading, detaches and destroys the browser view, releases browser-only buffers, and publishes
  a terminal reason before the next surface begins.

### Receiver lifecycle

1. A secure browser session authenticates and sends `Open`.
2. `BrowserCoordinator` validates the URL, increments an epoch, sets `SurfaceMode.Browser`, and
   emits `CreateOrNavigate`.
3. Compose creates `ReceiverBrowserView`, which creates the WebView with the activity context,
   applies its one immutable security profile, requests focus, then executes the effect.
4. `WebViewClient` / `WebChromeClient` produce title, URL, progress, history, main-frame error,
   SSL failure, prompt and capability observations. The coordinator sanitizes and versions them
   before publishing to the host.
5. Activity recreation detaches the old view. The coordinator retains only bounded, validated
   navigation state and asks a newly attached driver to recreate/reload; it never retains an old
   WebView instance or Activity reference.
6. On secure-session disconnect, close browser mode after a short visible “controller disconnected”
   state, destroy the WebView, clear in-memory previews, and return to idle. Do not leave a remotely
   controlled authenticated browser stranded on the television.
7. On API 26+, handle `onRenderProcessGone`: remove/destroy the dead view, publish a recoverable
   error, and create a fresh instance only once for a validated last URL. On API 25, where that API
   is absent, the hardware suite verifies the conservative fallback; do not claim equivalent crash
   recovery. A dead WebView must never be reused. [Android termination guidance](https://developer.android.com/develop/ui/views/layout/webapps/handle-termination)

## Security, privacy, and trust design

### 1. Separate encrypted browser session

The current `CastSession` uses an authorization token but Flint’s invariants correctly say that the
traffic is not encrypted. Browser commands can carry passwords, account URLs, page screenshots and
private content, so the plan adds a **separate TLS listener**, bound to the same validated receiver
interface but advertised as a distinct secure-browser endpoint.

Do not multiplex TLS detection onto the current stream by consuming bytes and trying to push them
back. That is fragile around the existing discovery framing. A dedicated receiver-advertised port
and a dedicated `BrowserSession` make downgrade behaviour auditable:

- Browser clients connect only to the secure endpoint. They never retry on the plain cast port.
- Receiver TLS accepts TLS 1.2 or later; API 25 supports TLS 1.2 while TLS 1.3 starts at API 29.
  The device test records the negotiated protocol and cipher; anything below TLS 1.2 is rejected.
  [Android SSLSocket protocol support](https://developer.android.com/reference/javax/net/ssl/SSLSocket)
- The receiver creates a non-exportable identity key in Android Keystore and serves a self-signed
  certificate. The host pins the full SHA-256 SPKI fingerprint, not a hostname or a permissive
  certificate callback. Android Keystore is available from API 18. [Android Keystore](https://developer.android.com/privacy-and-security/keystore)
- First pairing displays the same 64-bit fingerprint short code on the TV and PC. The user must
  compare it physically, then enter the six-digit TV pairing code **inside TLS**. The full fingerprint
  is stored on Windows using DPAPI/credential storage keyed to the receiver identity.
- Later connections accept only an exact pin match. Any certificate rotation, reinstallation, or
  mismatch requires TV-visible re-verification; never silently trust-on-first-use after a mismatch.
- The receiver returns a high-entropy browser session token only inside the authenticated TLS channel.
  It limits the session to one paired Windows client and rate-limits authentication failures.
- Do not write TLS key material, pairing codes, typed browser text, full URLs, previews, page bodies,
  cookies, or authorization headers to logs, diagnostics, crash reports, or test snapshots.

`SslStream` provides a certificate-validation callback on Windows; Android’s standard TLS sockets
provide confidentiality/integrity/authentication when properly configured. The implementation must
use these platform primitives, not invent its own encryption protocol. [.NET SslStream](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream)

### 2. WebView security profile

Apply the profile once when a new view is created; expose it through a small `BrowserSecurityProfile`
adapter so every setting has a unit test and an instrumentation assertion.

| Area | v1 policy |
|---|---|
| JavaScript | Enabled because modern sites require it; no `addJavascriptInterface`, `WebMessagePort`, `WebMessageListener`, injected scripts, or host-supplied `evaluateJavascript`. |
| Navigation | Accept absolute `https` URLs with a nonempty host; normalize through a URI parser. Reject blank/malformed, `http`, `file`, `content`, `data`, `javascript`, `intent`, `market`, `tel`, `mailto`, and all other custom schemes. Reapply policy for page navigation/redirect callbacks. |
| TLS errors | Always call `SslErrorHandler.cancel()`; never ask a user to “continue anyway.” [Android SslErrorHandler](https://developer.android.com/reference/android/webkit/SslErrorHandler) |
| Local access | Explicitly disable file access, content access, file-URL access, and universal file-URL access. This is essential on older Android versions where file access defaults can be unsafe. [Android unsafe file inclusion](https://developer.android.com/privacy-and-security/risks/webview-unsafe-file-inclusion) |
| Mixed content | `MIXED_CONTENT_NEVER_ALLOW`; no compatibility downgrade. [Android WebSettings](https://developer.android.com/reference/android/webkit/WebSettings) |
| Cookies/storage | DOM storage allowed for normal sites; third-party cookies disabled by default. The host has a visible per-session compatibility choice before enabling them. No “private” or “incognito” claim. |
| Popups | No multiple windows and no `onCreateWindow`; target-blank/pop-up requests are blocked rather than silently leaving Flint. |
| Permissions | Deny camera, microphone, screen capture, geolocation, MIDI, protected media, notifications, and file chooser requests by default. A later permission design, if ever approved, requires a TV-local, D-pad-visible, top-level-origin confirmation and Android runtime permission; the Windows host can never auto-grant. |
| Files | Uploads and downloads are blocked. No receiver filesystem or Windows filesystem becomes website-visible. |
| Debugging | `WebView.setWebContentsDebuggingEnabled(true)` only in debug builds. Production builds do not enable it and host commands cannot toggle it. Android warns that debugging allows ADB users to inspect/modify WebView state. [Android WebView](https://developer.android.com/reference/android/webkit/WebView) |
| Browser data | “Clear TV browser data” is an explicit destructive action with confirmation. It clears cookies, WebStorage, cache, form/session state and in-memory history; it does not pretend the user has anonymous browsing. |

The URL policy intentionally validates a **general browser** differently from a fixed-domain embedded
app: it validates a real absolute HTTPS URL and blocks dangerous schemes, but does not claim an
impossible global host allowlist. All host/title/error strings remain bounded, treated as untrusted
plain text, and never interpreted as markup.

### 3. Preview privacy rules

- The initial browser page explains that the TV visits websites directly and that a preview may show
  everything visible on the TV, including sign-ins.
- Preview begins only after an explicit per-session `SHOW TV PREVIEW` choice on the Windows app.
- The preview travels over the pinned TLS session; it is still not described as anonymous, private
  browsing, or a cloud-free guarantee about third-party websites.
- Flint stores no preview on disk and releases the decoded image when replaced, hidden, disconnected,
  or disposed.
- A protected/blank/unavailable capture is honoured. Flint never uses privileged capture to defeat
  content protection.

### 4. Distribution and platform truthfulness

- Update the existing `LOCAL ONLY · NO CLOUD` shell copy. Browser mode instead says: **“The TV
  connects directly to sites you open. Flint does not relay web traffic through a Flint cloud.”**
- The Web nav item is hidden or neutral/disabled until the receiver advertises secure WebView support.
  It must not look like a working feature on Vega, legacy protocol v1, unsupported provider, or
  unverified secure connection.
- Documentation distinguishes *TLS-protected Flint browser-control traffic* from the website’s own
  TLS/security/privacy practices. It never calls arbitrary web browsing private.

## Wire protocol and session contract

### Repair parity before extending it

The repository invariant says Rust, C#, and Kotlin must agree byte for byte. Before browser messages
are added, reconcile the already divergent models: C#/Kotlin have media message IDs 10–13 whereas
`flint-engine/src/wire` presently represents the earlier set. Extend Rust, regenerate/verify every
golden vector, and add a Kotlin consumer of the committed corpus. Do not label the protocol
three-language compatible until that existing gap is closed.

### Versioning rule

Raise `ProtocolVersion.Current` to **2** and keep `MinSupported` at **1**.

- New host + new receiver negotiate v2 and may use browser messages on the secure browser session.
- New host + old receiver negotiate v1. Existing media/mirror can continue; Web remains unavailable.
- No v2 browser message is sent on a v1 framing session. `SurfaceMode.Browser` is invalid on v1.
- Make the negotiated version a first-class property of both session implementations. Do not keep
  writing `Current` into every outgoing frame after a v1 negotiation.
- Codec implementations reject typed v2 browser messages encoded as v1, unknown enum values, unknown
  union variants, invalid booleans, malformed UTF-8, trailing bytes, invalid sizes, and impossible
  state combinations. Unknown future types remain opaque only where existing compatibility rules say
  they may be relayed.

### New v2 messages

Assign the following stable IDs after adding the v1 media IDs to Rust. The names below are the only
browser types; do not overload `SurfaceMessage.Caption`, media messages, or ambiguous generic
`ControlMessage` semantics.

| ID | Message | Direction | Required fields and invariants |
|---:|---|---|---|
| 14 | `BROWSER_CAPABILITY` | receiver → host | Availability/reason, secure endpoint, WebView/API diagnostics, preview support, max preview bounds. Sent only after secure authentication. |
| 15 | `BROWSER_COMMAND` | host → receiver | `epoch`, monotonic `commandId`, typed action (`Open`, `Navigate`, `Back`, `Forward`, `Reload`, `Stop`, `Close`, `SetPreview`, `ClearData`); URL only where valid; 4 KiB URL cap. |
| 16 | `BROWSER_INPUT` | host → receiver | `epoch`, monotonic `sequence`, typed platform-neutral input: pointer action, scroll, semantic navigation key, or UTF-8 text. No Windows virtual key codes cross the wire. |
| 17 | `BROWSER_STATE` | receiver → host | `epoch`, monotonic `revision`, navigation state, safe URL/title, progress 0–100, history flags, viewport, preview state, bounded user-safe error code/detail. Never includes DOM, cookies, headers, form values, or page source. |
| 18 | `BROWSER_PREVIEW` | receiver → host | `epoch`, `navigationId`, monotonic `frameId`, dimensions, JPEG format, bounded bytes (≤768 KiB). Latest-only; stale epoch/frame is discarded. |
| 19 | `BROWSER_DIALOG` | receiver → host | Expiring one-at-a-time JS dialog metadata: dialog ID, top-level origin, type, bounded message/default. It is an observation, not permission to execute code. |
| 20 | `BROWSER_DIALOG_REPLY` | host → receiver | Matching dialog ID, explicit accept/cancel, bounded text only for an active prompt. Requires the secure session. |

`BROWSER_INPUT` separates browser semantics from existing media transport controls. Its semantic key
set is deliberately small and portable: Up, Down, Left, Right, Select, Back, Tab, Shift-Tab,
Escape, PageUp, PageDown, Home, End, and Refresh. Printable Unicode goes through `Text`; arbitrary
OS scan codes and global key hooks do not.

### Ordering and stale-data rules

- A new `Open` or `Navigate` increments the browser epoch/navigation ID. Commands and input with an
  older epoch are rejected without touching WebView.
- `commandId`, `sequence`, `revision`, and `frameId` are strictly monotonic within their scope. Gaps
  are observable; duplicates/out-of-order values are ignored and counted.
- `BrowserSession` owns host command/input sequence allocation. View models must never guess or
  share counters.
- Host state reducer accepts only the latest state revision for an epoch. Preview decoder accepts
  only the newest frame for the current epoch/navigation and disposes older images immediately.
- Control/state frames must not be starved by previews. The writer drains bounded control state before
  at most one preview candidate.

### Protocol implementation files

| Area | Files to change/add |
|---|---|
| C# protocol | `src/Flint.Protocol/ProtocolVersion.cs`, `WireMessage.cs`, `WireCodec.cs`, new browser value objects as needed. |
| Kotlin protocol | `protocol/src/main/kotlin/.../wire/WireMessages.kt`, `WireCodec.kt`, session negotiation types. |
| Rust protocol | `flint-engine/src/wire/mod.rs`, `flint-engine/src/wire/codec.rs`, `flint-engine/tests/golden.rs`; keep the Rust golden corpus canonical. |
| Shared vectors | `testdata/golden/` plus a manifest that proves every typed message/edge case has exactly one named vector. |
| Documentation | `docs/PROTOCOL.md` must correct the existing `CONTROL` direction ambiguity and document v2 browser messages, TLS endpoint boundary, sizes, ordering, and compatibility. |

## UX specification

### Windows: new `Web` destination

Add **Web** between Screen and Diagnostics in `MainWindowViewModel` / `MainWindow.axaml`, using the
existing REX visual language and `W` text glyph. It is a real page, never a placeholder.

The page has four states:

1. **Unavailable:** explains the exact device/protocol/security reason and the remedy. No fake
   active controls.
2. **Verify secure receiver:** guides the user through opening the receiver, comparing the TV/PC
   certificate short code, entering the TV pairing code, and pinning the identity. It says plainly
   that browser control is encrypted only after this verification succeeds.
3. **Ready/browser active:** shows browser controls and state.
4. **Recoverable error:** distinguishes untrusted receiver, TLS failure, blocked URL, WebView crash,
   disconnected controller, unsupported preview, and website error. It never blames “the network”
   when the receiver has a precise reason.

Ready layout (wide) is a two-column broadcast-instrument surface:

```text
REMOTE WEB                                             SECURE • TV CONNECTED
[ < ] [ > ] [ reload / stop ] [ https://address........................ ] [ GO ]

┌──────────────────── TV preview (16:9) ───────────────────┐  ┌─ STATUS ─────┐
│ Latest opt-in receiver WebView image; click/drag maps      │  │ Title        │
│ precisely into normalized TV coordinates.                  │  │ Address      │
│ “TV DISPLAY IS CANONICAL” overlay when preview is off.     │  │ Load / error │
└───────────────────────────────────────────────────────────┘  │ Preview fps  │
┌────────────── PC remote ──────────────┐                       │ dropped      │
│         [ UP ]                         │                       └──────────────┘
│ [ LEFT ] [ SELECT ] [ RIGHT ] [ BACK ] │
│       [ DOWN ] [ PgUp ] [ PgDn ]       │
│ [ Send text to TV …________________ ] [ SEND ]               
└────────────────────────────────────────┘

[ SHOW TV PREVIEW ]  [ CLEAR TV BROWSER DATA ]  [ CLOSE BROWSER ]
```

Narrow layouts stack status, address bar, preview, and remote in that order. The preview retains
16:9 aspect ratio with letterboxing; pointer conversion uses the actual drawn image rectangle, not
the containing control bounds.

UX rules:

- The address bar is local Windows text. `GO` (or Enter while it has focus) is a deliberate
  `Navigate` command; the user sees “Loaded by TV” beside it.
- The **Send text** field is separate from the address bar, only enabled after secure pairing, and
  sends composed Unicode text on explicit `SEND`. Flint never installs a global keyboard hook or
  forwards every desktop keystroke.
- Controls expose tooltips/accessibility names, keyboard focus, disabled state and error reason.
  All buttons are testable through `Avalonia.Headless`.
- Preview pointer events are optional. Every preview interaction also has a D-pad/semantic-key
  alternative, and drag/scroll never becomes the only way to navigate.
- First use presents a one-session preview privacy disclosure. Preview is off by default.
- `CLEAR TV BROWSER DATA` has a destructive confirmation naming what will be erased; it is not a
  cosmetic reset button.

### TV: browser surface

Add `SurfaceMode.Browser` and a `ReceiverBrowserSurface` to the current Compose `ReceiverSurface`.
The WebView is visually dominant. Flint TV chrome is minimal and appears only while loading, showing
an error, receiving a dialog, or when the user opens the TV menu. It lives within the existing 5%
overscan-safe region.

TV interaction hierarchy:

1. A native JS/Flint dialog is dismissed or answered.
2. An active focused WebView element receives D-pad/select input.
3. Back navigates WebView history when history exists.
4. Back at the initial page shows the local browser menu/close choice; a second confirmed Back
   returns to Flint idle.

The browser screen always renders a visible focused item when the menu/dialog is open. Home,
microphone, and system-volume buttons remain system-owned; pause/resume preserves only bounded
validated navigation state. This follows Fire TV’s D-pad-first requirements. [Amazon Fire TV UX](https://developer.amazon.com/docs/fire-tv/design-and-user-experience-guidelines.html)

### Native page dialogs and errors

- JavaScript alerts/confirms/prompts become a **TV-local native dialog** with visible top-level
  origin, bounded text, Cancel as the safe default, time limit, and D-pad focus. The host gets a
  matching observation only over TLS and may reply only to the current dialog ID.
- Web permissions, SSL certificate errors, external intents, popups, uploads, downloads, and unsafe
  URLs never reuse this “user consent” dialog. They are denied and represented by a short clear
  status.
- Page errors use user-safe classifications (`No connection`, `Certificate rejected`, `Blocked
  address`, `Page renderer stopped`, `Site returned an error`) instead of raw platform exception
  dumps. Detailed redacted diagnostics remain developer-only.

## Implementation roadmap (TDD-first)

Every numbered implementation task starts with the listed failing test(s), then only enough
production code to pass, then a refactor with the same tests green. No “spike code” becomes product
code without a test characterizing its result.

### M0 — policy, device, and baseline audit

**Purpose:** turn unknown platform claims into evidence before broad implementation.

1. Record Amazon’s browser-publication restriction in `README.md`, `docs/ENGINEERING_NOTES.md`,
   `docs/INSTALL.md`, and `site/index.html`; tag the feature experimental/sideload-only.
2. Extend capability reporting with a neutral `WebBrowser` verdict: `UnsupportedPlatform`,
   `ProtocolTooOld`, `SecureEndpointUnavailable`, `WebViewSmokeTestPending`, `Available`, and
   concrete remediation. Do not infer it from generic Android capability.
3. Add a debug-only `BrowserCompatibilityProbe` to the receiver. It creates one WebView on the UI
   thread, reports API/UA/provider information available on that API level, loads an owned local
   fixture, then a controlled public HTTPS endpoint, and emits bounded outcomes. On API 25,
   `WebView.getCurrentWebViewPackage()` is unavailable; log only legal available diagnostics such as
   user agent/engine behaviour.
4. Run a real AFTMM acceptance session: fixture, HTTPS, JavaScript, back/forward, D-pad/select,
   text entry, Home/pause/resume, Wi-Fi loss/restore, WebView destroy/recreate, CPU/memory sampling,
   and a preview-capture candidate. Record facts—not estimates—in `docs/LATENCY_BUDGET.md` or a new
   browser measurement section.
5. Request Amazon policy confirmation before any Appstore distribution work. Do not proceed on an
   assumption that an embedded browser escapes the policy.

**Tests first:** `BrowserCapabilityReducerTest`, capability-report tests in C#, receiver fake-probe
tests, deterministic local fixture instrumentation test, and one `#[ignore]`/physical-device test.

**Exit:** an evidence table declares the actual receiver supported or unavailable, preview viable or
unavailable, and public distribution allowed or developer-only.

### M1 — restore protocol discipline and define v2 contracts

**Purpose:** make every side agree before any browser socket is live.

1. Add missing existing media IDs 10–13 to Rust’s wire model/codec and regenerate their vectors.
2. Write the v2 golden vectors **before** codec implementation for capability, every command/input
   union, state, preview, dialog/reply, maximum/minimum bounds, multi-byte Unicode, and unknown
   future message behaviour.
3. Implement identical v2 values/codecs in Rust, C#, and Kotlin; make encode/decode reject all
   invalid shapes.
4. Update negotiation so sessions use the actual mutually selected version. Add explicit tests for
   v2↔v2, v2↔v1, no overlap, and prohibition of browser messages on v1.
5. Add the Kotlin test that reads the committed Rust-generated binary vectors and confirms both
   decode and re-encode identity. C# and Rust do the same; vector manifest completeness fails on
   missing/extra cases.
6. Correct `docs/PROTOCOL.md` and all code comments that imply the current plain control traffic is
   encrypted or that generic control direction is only receiver-to-host.

**Files:**

- `flint-engine/src/wire/{mod.rs,codec.rs}`, `flint-engine/tests/golden.rs`
- `src/Flint.Protocol/{ProtocolVersion.cs,WireMessage.cs,WireCodec.cs}`
- `protocol/src/main/kotlin/.../wire/{WireMessages.kt,WireCodec.kt}`
- `tests/Flint.Protocol.Tests/{WireCodecTests.cs,GoldenVectors.cs}`
- `protocol/src/test/.../wire/*`, `testdata/golden/*`, `docs/PROTOCOL.md`

**Exit:** byte-identical three-language vectors, 100% branch/line coverage for the new protocol
branches, mutation checks for decoders, and a v1 peer that still mirrors/media-casts safely but sees
Web as unavailable.

### M2 — secure browser listener, identity trust, and `BrowserSession`

**Purpose:** make browser controls safe before implementing browser text or preview.

1. Add a receiver `BrowserTlsServer`/secure endpoint separate from `ReceiverServer`; bind it only to
   the validated `Inet4Address`, restart it with network changes, advertise its actual port/capability,
   and permit one active browser client.
2. Add `ReceiverIdentity`: generate/persist an Android Keystore key and self-signed receiver
   certificate; display its stable short fingerprint on TV pairing/browser screens. Never export the
   private key.
3. Add the Windows `BrowserTrustStore` backed by DPAPI/credential storage and the `BrowserSession`
   TLS client. First pairing compares fingerprint on physical TV + Windows UI, then authenticates the
   existing pairing code inside TLS. Later pairing requires exact pin match.
4. Add a TLS-only browser handshake that negotiates protocol v2 and returns `BROWSER_CAPABILITY`.
   Browser UI remains disabled unless TLS, pin, auth, v2, and capability all pass.
5. Use an outbound writer loop with a bounded reliable queue and latest-only preview cell. Refactor
   the receiver’s one-coroutine-per-`send()` pattern where browser traffic could otherwise overwhelm
   it.
6. Do not change existing media/mirror transport behaviour in this milestone other than shared
   discovery/capability plumbing. No browser plain fallback.

**Tests first:** TLS policy tests, certificate fingerprint normalizer tests, trust-store persistence
tests, first-pair accept/reject tests, pin mismatch/downgrade/wrong-code/replay/rate-limit tests,
loopback secure-server integration tests, source-address binding tests, writer fairness/coalescing
tests, and a real AFTMM TLS test marked hardware-only.

**Exit:** Wireshark/manual capture demonstrates that pairing code, URL, text and preview bytes are
not readable on the browser LAN channel; an invalid/mismatched certificate cannot be accepted; a
secure browser session survives normal reconnect without a plaintext fallback.

### M3 — pure browser domain and receiver command boundary

**Purpose:** create fully testable business logic independent of Android WebView.

1. Add `receiver/.../browser/BrowserUrlPolicy.kt`, `BrowserCommandReducer.kt`,
   `BrowserStateMachine.kt`, `BrowserInputRouter.kt`, `BrowserPreviewPolicy.kt`, and minimal immutable
   models/effects/observations.
2. Add a `BrowserPort`/`BrowserWebViewDriver` boundary. Reducer effects include `Create`, `Navigate`,
   `GoBack`, `GoForward`, `Reload`, `Stop`, `DispatchInput`, `ClearData`, `CapturePreview`, and
   `Destroy`; observations are the only route back from Android.
3. Extend `ReceiverSessionListener` / secure server dispatch to deliver browser messages only after
   secure authentication. Plain `ReceiverServer` rejects Browser surface/message attempts instead of
   accidentally selecting a browser.
4. Extend `ReceiverService` with a `BrowserCoordinator`, `BrowserUiState`, and mutual-exclusion
   transitions. Protect every transition with epoch/session ownership checks.
5. Replace `ReceiverActivity.dispatchTvKey(keyCode)` with an event-preserving route. Browser mode
   gives the focused WebView first chance to consume valid D-pad keys; the service applies the stated
   Back hierarchy only when the page does not consume it.
6. Map host pointer coordinates only after normalized finite/clamped validation and real content
   rectangle calculation. Map text through native input/IME-compatible dispatch, never JavaScript.
   Unsupported input capability is reported, not faked.

**Tests first:**

- URL table/property tests: case/whitespace/Unicode/IDN/malformed/relative/user-info/scheme variants.
- Reducer state-transition and epoch/sequence property tests.
- Input-router tests for every semantic key, pointer/down/move/up/scroll sequence, invalid/coercion
  behaviour, focus loss, stale input, and text limits.
- Fake-port receiver service tests for every command, surface switch, disconnect, error and dialog.
- `ReceiverServerTest` authenticated dispatch/reply tests proving pre-auth and plain listeners cannot
  reach the browser coordinator.
- Preview policy tests for rate limit, size cap, latest-only replacement, overload, navigation reset,
  and no allocation/backlog growth.

**Exit:** the entire browser state machine and security boundary are green without a device or
WebView; all owned branches are covered before `AndroidView` is written.

### M4 — Android WebView driver and TV surface

**Purpose:** turn the tested domain effects into an actual, secure, D-pad-friendly receiver browser.

1. Add AndroidX WebKit and feature-gate optional APIs through `WebViewFeature`; retain minSdk 25.
   Do not assume an API-26+ feature exists on Fire OS 6.
2. Add `ReceiverBrowserView` to `ReceiverScreen.kt` via `AndroidView`, and `SurfaceMode.Browser` to
   all three wire models and UI states. The view uses the activity context and is always removed from
   its parent before `destroy()`.
3. Implement `BrowserWebViewDriver` with the fixed security profile, `WebViewClient`,
   `WebChromeClient`, focus manager, native dialog bridge, activity lifecycle hooks, and guarded
   renderer recovery.
4. Add `WebViewAssetLoader` strictly for deterministic owned test fixtures under
   `https://appassets.androidplatform.net/`; never use `file://` to make tests pass. [AndroidX WebViewAssetLoader](https://developer.android.com/reference/androidx/webkit/WebViewAssetLoader)
5. Implement preview capture only after the M0 capture result is proven. Capture on the main thread,
   hand a bounded bitmap candidate to one worker, compress in the current pool/slot, and publish only
   through the latest-only mailbox. Measure allocation/GC/memory and frame drops on actual hardware.
6. Implement cleanup: stop loading, dismiss current dialog, clear pending effects, detach/destroy
   view, dispose bitmaps, and publish one final state when changing surface or ending session.
7. Do not change `android:usesCleartextTraffic="true"` blindly: audit current receiver paths first.
   Browser URL policy remains HTTPS-only immediately; a later network-security-config tightening is
   an independently tested change.

**Tests first:**

- Robolectric unit/integration tests for security-profile settings, driver effect routing, destruction
  order, dialog expiry, and API feature gates.
- Compose snapshot tests for browser waiting/loading/ready/blocked/error/disconnected/menu/dialog
  states using a deterministic fake browser surface—not live internet pages.
- Android instrumentation with Espresso-Web over `WebViewAssetLoader`: title/progress/history/link,
  back/forward, blocked URI, mixed-content rejection, popup/file chooser/permission denial, SSL
  cancellation, input dispatch, and data clear.
- Dedicated debug-only renderer-crash test (`chrome://crash`) on API 26+; API-25 fallback test is
  hardware/manual and must not claim a nonexistent callback.
- UiAutomator physical Fire TV tests for visible focus, every D-pad edge, Home/pause/resume, browser
  close, and a receiver that becomes available again after a browser error.

**Exit:** a real Fire TV can use the browser entirely with its remote and never gets trapped on a
blank/no-focus surface. Unsafe WebView capabilities are proven denied in instrumentation.

### M5 — Windows BrowserPage, preview, input, and accessibility

**Purpose:** make the PC a polished control surface without leaking browser ownership to Windows.

1. Add `BrowserPageViewModel`, `BrowserSessionCoordinator`, `BrowserPreviewFrame`, and a thin
   `IBrowserTransport` adapter rather than exposing `CastPageViewModel`’s private `CastSession`.
   Share receiver selection/probe data through a small injected connection service.
2. Add `BrowserPage.axaml` / code-behind for URL input, navigation, secure pairing, D-pad remote,
   status, privacy disclosure, preview stage, and destructive data-clear confirmation.
3. Add a robust host image decoder: enforce message byte cap, width/height/pixel cap before decode,
   decode off the session receive loop and UI thread, marshal only the final image to Avalonia,
   dispose stale images immediately, and never persist frames.
4. Add preview pointer geometry as a pure tested mapper; account for aspect ratio/letterbox, DPI,
   clamp results, and only emit pointer messages while the secure preview is current.
5. Add composed Unicode text send and semantic-key buttons. No global hooks, raw Windows virtual
   key codes, hidden browser, or password logging.
6. Update navigation shell, onboarding/capability pages, Diagnostics telemetry, user documentation,
   and site copy so the UI never says browser traffic is local-only/private or the feature is shipped
   on unsupported devices.

**Tests first:**

- `BrowserPageViewModelTests`: every state, command enablement, error, trust decision, no-plaintext
  fallback, stale state/frame, text clearing/redaction, and clear-data confirmation.
- `BrowserSessionTests`: loopback TLS incoming state/preview, concurrent send serialization,
  disconnect, malformed frame, no UI-thread decode, epoch ordering, and pin failures.
- `BrowserPreviewMapperTests`: every geometry/edge/DPI case plus property tests.
- `Avalonia.Headless` interaction tests and approved-image snapshots for all page states at 100%,
  125%, and 150% scaling; review every approved image before accepting it.
- Accessibility tests for names, focus traversal, keyboard-operable controls, contrast/theme tokens,
  disabled explanations, and no content-only colour signal.

**Exit:** Windows can operate the actual receiver browser using only documented control messages;
the preview is opt-in, accurate enough for tested interaction, bounded, disposable, and never
mistaken for a Windows-rendered page.

### M6 — hardening, documentation, and release decision

1. Run the full test matrix below on clean machines without Fire TV/GPU; only explicitly tagged
   hardware suites may require the target stick.
2. Run a 30-minute live session on the actual receiver: browsing, multiple navigation types, text,
   browser data clear, preview on/off, Wi-Fi change, host crash, receiver pause/resume, memory/CPU
   sample, heat check, and switch to/from media/mirror.
3. Test a representative public-website matrix only as recorded compatibility observations. Include
   a basic static site, form page, redirect, cookie-first-party site, JavaScript-heavy page, blocked
   popup/file chooser/permission, and a deliberately unsupported video/DRM page. Do not publish a
   blanket compatibility claim.
4. Update `README.md`, `AGENTS.md`, `docs/ENGINEERING_NOTES.md`, `docs/PROTOCOL.md`,
   `docs/INSTALL.md`, `docs/LATENCY_BUDGET.md`, `site/index.html`, release notes, and UI strings.
5. Publish only the truthful capability matrix: tested Fire OS models/provider diagnostics, whether
   preview works, browser secure-session availability, known limitations, and Appstore policy state.

**Exit:** all acceptance criteria pass, documentation agrees with observed evidence, and the feature
is either released in the approved scope or remains a clearly labelled developer experiment.

## File-level change map

| Location | Planned change |
|---|---|
| `receiver/build.gradle.kts` | Add compatible AndroidX WebKit, Espresso-Web/instrumentation dependencies, and browser test configuration; pin versions deliberately. |
| `receiver/src/main/AndroidManifest.xml` | Add Safe Browsing metadata where supported; preserve TV touchscreen declaration; audit cleartext policy before modification. |
| `receiver/.../ReceiverActivity.kt` | Preserve full key events, browser attach/detach lifecycle, and no Activity retention by service. |
| `receiver/.../ReceiverService.kt` | Own browser coordinator/state, exclusive surface transitions, secure-session lifecycle; never own `WebView`. |
| `receiver/.../ReceiverUiState.kt` | Add bounded browser presentation/capability/error data—no page/body/credential fields. |
| `receiver/.../ui/ReceiverScreen.kt` + new `ui/ReceiverBrowserSurface.kt` | Compose/AndroidView browser surface, D-pad-safe native chrome/dialog snapshots. |
| new `receiver/.../browser/*` | URL policy, immutable commands/effects/state, driver, security profile, input router, preview producer, local dialog controller, capability probe. |
| `receiver/.../net/ReceiverServer.kt` + new secure server files | Explicit secure listener, TLS session/authentication, bounded writer, browser dispatch/capability. |
| `protocol/.../wire/*` | v2 models/codecs/negotiation and test vectors. |
| `flint-engine/src/wire/*` | Restore full v1 parity, then v2 browser codec model; generated vectors. |
| `src/Flint.Protocol/*` | Matching C# v2 models/codecs/negotiation. |
| `src/Flint.Session/*` | Keep legacy `CastSession` isolated; add `BrowserSession`, trust store, secure transport interface, event dispatch and sequence ownership. |
| `src/Flint.App/ViewModels/*`, `Views/*` | New Browser page/VM; shell/nav/capability copy; diagnostics. |
| `tests/Flint.Protocol.Tests/*`, `tests/Flint.Session.Tests/*`, `tests/Flint.App.Tests/*` | Contract, TLS, state, preview, VM, accessibility and snapshot suites. |
| `receiver/src/test/*`, `receiver/src/androidTest/*`, `protocol/src/test/*` | Pure Kotlin, Robolectric/snapshot, Espresso-Web and physical-device suites. |
| `scripts/*` | Deterministic unit/coverage job plus opt-in Fire TV browser E2E/measurement script; never hijack a physical receiver without an explicit flag. |
| Documentation/site | Truthful experimental/distribution/security/capability wording and source-backed protocol reference. |

## Quality and coverage contract

“Full coverage” here means full coverage of Flint-owned decisions, not a dishonest percentage claim
about Android/Chromium code that the project does not own.

| Code class | Required proof |
|---|---|
| New protocol, URL policy, reducers, sequencing, geometry, TLS trust policy, preview policy, host VM/domain code | **100% line and branch coverage**; property/fuzz tests for parsers and ordering; mutation testing must kill meaningful mutations. |
| C#/Kotlin/Rust codec compatibility | Golden byte vectors generated deliberately, cross-language encode/decode identity, malformed input/fuzz corpus, and version-negotiation tests. |
| Receiver service/network adapters | Unit tests with fakes for every public transition plus loopback integration tests for authentication, authorization, bounded queues, cancellation and disconnect. |
| Android WebView bridge | Security-profile assertions, Espresso-Web fixture tests, Robolectric lifecycle tests where faithful, and explicit real-device suite for every API/provider-specific branch. |
| Avalonia UI | View-model branch coverage, Headless interaction/accessibility tests, reviewed snapshots of every visual state and scale. |
| Hardware-dependent behaviour | Marked/isolated physical-device tests; they skip cleanly by default but gate the browser release workflow. No emulator-only claim substitutes for the AFTMM test. |

Mandatory TDD mechanics:

1. Add a failing test naming the behaviour/security invariant.
2. Run only that test to establish a real red result.
3. Implement the smallest production change that turns it green.
4. Run its module suite, then the relevant cross-language suite.
5. Refactor only with all tests green.
6. Add/update snapshot/golden assets through a deliberate approval command; inspect assets before
   committing. Tests must never auto-approve changed screenshots or vectors.
7. Add a regression test for every defect found during the physical-device spike before fixing it.

The build fails on a coverage threshold regression, unapproved snapshot/vector, clippy/analyzer
warning, unhandled protocol type, or hardware-acceptance status marked “release required” without
the recorded evidence.

## Test matrix

| Layer | Tests | Representative failure cases |
|---|---|---|
| Rust/C#/Kotlin protocol | Golden, round-trip, cross-language, fuzz/property, version negotiation | truncated/oversized frame, invalid UTF-8/boolean/enum, v1 browser type, trailing bytes, stale IDs. |
| TLS/trust | Fake certificate, loopback server, pin store, downgrade and rate-limit tests | mismatched pin, expired certificate, wrong pairing code, user rejects code, reconnect race, plaintext fallback attempt. |
| Browser domain | Pure reducer/URL/input/preview policy tests | unsafe scheme, old epoch, duplicate sequence, invalid pointer, dialog timeout, writer overload, browser/mirror race. |
| Receiver transport | Authenticated socket integration with recording listener | browser frame before TLS/auth, pre-auth dispatch, two clients, lost network, receiver shutdown. |
| WebView instrumentation | `WebViewAssetLoader` + Espresso-Web fixtures | history, title/progress, D-pad focus, unsafe URL, SSL cancellation, popup/chooser/permission denial, clear data, lifecycle. |
| Receiver snapshots | Robolectric/Compose approved images | waiting, pairing fingerprint, loading, ready, blocked URL, dialog, error, controller disconnect, no-preview state. |
| Windows tests | VM/session/preview mapper/Avalonia Headless/snapshots | untrusted device, no capability, stale preview, DPI/letterbox click error, accessibly hidden control, data-clear cancellation. |
| Fire TV E2E | UiAutomator + explicit manual/ADB evidence | visible focus, all D-pad routes, text, TV Home/back, Wi-Fi change, memory/capture, no unbounded queues. |
| Security review | Static checks and manual packet/log review | JS bridge occurrence, plaintext secret in capture/log, debug endpoint in release, non-HTTPS navigation. |

## Measurable acceptance criteria

### Functional

- A supported Fire OS receiver opens `https://` URLs directly from the TV and reports title, current
  canonical URL, progress, history availability, and safe errors to Windows.
- Windows Back/Forward/Reload/Stop/Close, semantic D-pad, text send, and verified preview pointer
  interactions work against deterministic fixtures and the physical receiver.
- The TV remote alone can operate, escape, and close browser mode with visible focus at every step.
- Browser/mirror/media transitions release the previous surface and cannot run concurrently.
- Old v1 and Vega receivers show a neutral unavailable Web verdict; existing media/mirror flows stay
  green.

### Security/privacy

- Browser UI never enables on a plain cast session; browser text/preview packets are observed only
  inside the TLS channel during packet inspection.
- First use requires TV/PC fingerprint comparison; any pin mismatch stops before pairing code or
  browser command is sent.
- No JavaScript/native bridge, external intent, file/content access, insecure mixed content, upload,
  download, popup, permission grant, or SSL-error bypass is reachable in release.
- Browser logs/snapshots/telemetry contain no typed text, page body, cookie, secret query parameter,
  preview image, or raw auth header.
- Clear-data action has a confirmation and proves cookie/storage/cache/history removal in tests.

### Reliability/performance

- Browser preview has one-slot bounded buffering and never blocks the main thread, secure writer,
  mirror engine, or control state messages.
- Measurements from the actual device report preview capture/compress/send time, rate, dropped
  candidates, process memory, CPU and thermal behaviour; no latency or frame-rate claim is published
  before it is measured.
- Activity recreation/receiver reconnect/failed navigation return to a usable TV screen with no
  retained Activity/WebView reference or unbounded buffer.
- A 30-minute real-device session exhibits no steadily growing memory, queue, descriptor, or task
  count; results are recorded with device/OS/provider details.

### Documentation/release

- All docs/site/UI accurately call this a Fire OS WebView experiment until each gate passes.
- They disclose direct TV internet traffic, TLS scope, preview opt-in, lack of Appstore approval,
  tested devices and unsupported media/DRM situations.
- `docs/PROTOCOL.md` supplies enough field, ordering and limit detail for an independent C#/Rust/
  Kotlin implementation to interoperate byte-for-byte.

## Research sources consulted

Primary documentation was prioritized. The complete claim ledger and local-code evidence are in
[`report-source.md`](../report-source.md).

- [Amazon Fire OS overview and version/device split](https://developer.amazon.com/docs/fire-tv/fire-os-overview.html)
- [Amazon AFTMM / Fire TV Stick 4K device specification](https://developer.amazon.com/docs/device-specs/device-specifications-fire-tv-streaming-media-player.html?v=ftvsticklite)
- [Amazon Fire TV browser-app distribution restriction](https://developer.amazon.com/docs/app-submission/device-filtering-and-compatibility.html)
- [Amazon Fire TV design and D-pad guidance](https://developer.amazon.com/docs/fire-tv/design-and-user-experience-guidelines.html)
- [Amazon Fire TV FAQ: WebView video limitations](https://developer.amazon.com/docs/fire-tv/faq-general.html)
- [Android: Build web apps in WebView](https://developer.android.com/develop/ui/views/layout/webapps/webview)
- [Android: WebView security and unsafe file inclusion](https://developer.android.com/privacy-and-security/risks/webview-unsafe-file-inclusion)
- [Android: unsafe URI loading](https://developer.android.com/privacy-and-security/risks/unsafe-uri-loading)
- [Android: insecure WebView native bridges](https://developer.android.com/privacy-and-security/risks/insecure-webview-native-bridges)
- [Android: WebView termination lifecycle](https://developer.android.com/develop/ui/views/layout/webapps/handle-termination)
- [Android: Espresso-Web](https://developer.android.com/training/testing/espresso/web)
- [AndroidX: WebViewAssetLoader](https://developer.android.com/reference/androidx/webkit/WebViewAssetLoader)
- [Android: TLS sockets and supported protocol versions](https://developer.android.com/reference/javax/net/ssl/SSLSocket)
- [.NET: SslStream certificate validation](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream)

## Final implementation rule

Do not start by embedding a WebView and “seeing what happens.” Start with M0 evidence and the M1
protocol/security tests. A browser is not an ordinary new page: it changes the receiver from a
local-media endpoint into a user-facing internet client. The design above keeps that boundary
explicit, testable, reversible, and honest.
