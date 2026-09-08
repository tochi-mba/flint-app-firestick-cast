# Active implementation audit

This work is not complete. Earlier prototype completion statements are not evidence that the
independent webpage workspace, profile isolation, or VPN policy is production-ready.

## Current changes

- Service-owned workspace selection replaces the UI's unconditional local-profile activation.
- Workspace surface replaces the single-page surface instead of mounting both browser hosts.
- Renderer bindings carry session-unique tokens; late callbacks cannot update a replacement.
- Activity attachment rebuilds live renderers; failed native restoration reloads the saved URL.
- Observable state includes asynchronous page events, not only button presses.
- TV workspace has address/search keyboard, pointer input and navigation controls.
- Native workspace dialogs support confirmation and editable prompts with cancel-default focus.
  Results are invalidated on navigation, pane/profile replacement, timeout and host teardown.
- TV layout controls now expose single, side-by-side and stacked arrangements; the 2×2 option stays
  visibly unavailable until a hardware-qualified capacity advertises it.
- VPN credentials use profile-bound Android-Keystore AES-GCM storage, backup is disabled, and
  deleted or temporary device profiles cannot retain credentials.
- WireGuard admission now rejects default routes and any route covering a derived local interface
  address. The backend's UP result enters `TunnelUpUnverified`; browsing remains blocked until
  Android reports a validated VPN network, and failed verification tears the tunnel down.
- VPN consent requests are TV-local, single-flight, and invalidated across profile/session changes.
- Windows clears a VPN draft on profile changes and after the TV confirms provisioning; stale
  epoch, revision, or profile snapshots cannot retarget that secret.

## Verification and remaining work

- The full receiver unit suite and lint now pass. Earlier focus, pairing-layout and snapshot
  failures are superseded by `artifacts/receiver-current-full.log`; snapshots were visually
  reviewed before approval. Dialog-reentrancy regressions also pass and are included in the suite.
- The default .NET solution suite passes; `artifacts/dotnet-current-full.log` records per-project
  totals. Hardware-category tests are excluded, not silently counted as passing.
- A real out-of-process Windows introduction test passed using UIA mouse input: replay, Next,
  Back, Skip and replay again. Screenshot evidence is in `artifacts/windows-introduction`.
  Run it with `scripts/test-windows-app-headed.ps1 -AcknowledgeDesktopControl`.
- The cross-device returning-trust pairing/Web flow is implemented with Windows UIA and TV ADB,
  but has not passed: earlier attempts timed out against `10.216.219.172:5555`. The Stick is now at
  `10.175.171.172:5555` — use that serial for `scripts/test-fire-tv-app-pair.ps1` and log pulls.
- VPN software policy now distinguishes tunnel-up from Android-validated connectivity and gates
  workspace navigation. Real Fire TV route, DNS, handshake, revoke, Wi-Fi-change and LAN-control
  survival evidence is still required. Do not claim a packet kill switch.
- Rust formatting, clippy, unit tests and build passed in `artifacts/flint-rust-audit.log`.
  Kotlin protocol tests also pass. Keep real media/route checks separate from codec/model tests.
- Inspect every unfinished capability across Flint. Do not disguise missing implementations by
  removing warnings or overriding real hardware, security or protocol limits.
- Real Fire TV checks remain necessary for D-pad focus, pane-clipped fullscreen, simultaneous
  playback, audio mute support, VPN routing and Activity recreation.

## VPN: what exists, and what is still unproven

- The client half is complete and green: profile-bound Keystore storage, route admission with a
  LAN carve-out, consent handling, and a verifier that refuses to call a tunnel connected until
  Android reports a validated VPN network.
- The missing half was a server. `scripts/provision-wireguard-server.ps1` now builds one on a host
  you already have SSH access to and emits a Flint-ready client config. `docs/VPN_SETUP.md` records
  the admission rules, the OCI security-list rule the script cannot add for you, and the fact that
  Android's consent prompt requires a person.
- `BrowserVpnProvisionedConfigTest` pins the generator's exact output against every gate between a
  paste and a tunnel. Before it, each gate had only ever been exercised against a hand-made fixture;
  a config the generator emits and the receiver then rejects is indistinguishable, from a sofa, from
  a VPN that does not work.
- **Still no hardware evidence.** No handshake, route, DNS, revoke, Wi-Fi-change or LAN-control
  survival check has been run against a real Fire TV, because this environment can reach neither the
  server (ssh is blocked here) nor the television (adb is not installed here). Nothing above is a
  claim that the tunnel carries traffic. The proof is a non-zero `wg show wg0 latest-handshakes`.

## Windows Web page: the manual card

- The secure-receiver card used to appear whenever a device was selected, with a port box and a
  button, regardless of whether anything was missing. On a returning user with a remembered pin it
  showed a next step — "enter the Cast pairing code" — beside no field that could accept one,
  because the pairing code lives on the Cast page.
- The card is now driven by the first missing input (`BrowserSecureReceiverPrompt`). With trust
  remembered, routing known and a code present, it does not appear at all and the existing
  unattended reconnect runs. When something *is* missing the copy names it and says where it is
  entered. The port box appears only when discovery failed to advertise a port.

## Required final follow-up: first-use experience

After implementation and verification, audit the entire project again before implementing the
final onboarding pass. Cover Windows and TV, first launch and returning users, standalone TV
browsing, local versus temporary device profiles, pane navigation, D-pad input, fullscreen,
media control, VPN permission and failure, reconnect and recovery.

Prefer contextual, dismissible, replayable help with stable focus and accessible explanations.
Never interrupt playback, a modal consent flow, typing or a pointer gesture. Persist completion
at the appropriate device/profile scope; explicitly test profile switches, updates, restored
sessions, dismissal, repeated entry, offline startup, unavailable hardware and interrupted tours.
Automated coverage does not replace testing on a real TV remote and Windows keyboard.

## Latest first-use and lifecycle changes

- Explicit, replayable Web help on Windows and Help on idle TV/workspace surfaces. They do not
  auto-open. Workspace dropped the legacy auto-opening tip and duplicate Help chip in favour of a
  single `ReceiverHelp` control. Dismissal is device-local; test fixtures use isolated persistence.
- TV help tests cover initial focus, D-pad activation, Close, Android Back, completion across
  surface recreation and replay. Windows help tests click actual rendered controls, verify
  page-dialog precedence, completion/replay and keyboard-focus restoration.
- Windows remote text entry has a full-width row; page keys wrap. Geometry tests cover compact
  and large page sizes rather than approving a screenshot with clipped controls.
- Workspace close publishes an empty, monotonic state so Windows does not retain old panes.
- Held pointer gestures are cancelled on their original pane before focus/profile/renderer
  changes. Orphan releases cannot click a replacement pane. Router and session tests cover this.
- Reentrant dialog replacement cancels every superseded request without losing the newest one.
- VPN verification requests explicitly remove Android's default `NOT_VPN` capability. Regression
  tests cover the actual request, validated Wi-Fi rejection, unvalidated VPN rejection, successful
  verification, timeout and stale callbacks after cancellation (`artifacts/vpn-verifier-tests.log`).
  Android's builder requirements are documented at
  https://developer.android.com/reference/android/net/NetworkRequest.Builder.
- Onboarding no longer guarantees all features based on an OS version or makes claims about
  future device support. Final whole-project onboarding and physical-device acceptance remain open.

## Remaining VPN lifecycle audit findings

- The platform verifier is currently a one-shot check and unregisters after success. Continuous
  tunnel loss, revocation and revalidation need explicit monitoring; a past successful check is
  not evidence that the tunnel remains healthy.
- Tunnel setup is synchronous. Moving it off the UI thread requires serialised connect/disconnect
  ownership and cancellation tests, not simply launching independent coroutines.
- Existing pages and background loads need a tested required-VPN shutdown policy, not only guards
  before accepting a navigation command. Do not present this implementation as a packet kill switch.
- Profile policy lookup needs auditing for connected-device selections versus the remembered TV
  profile; a device session must not accidentally inherit a different profile's VPN requirement.


## Adjustable mosaic continuation ? 2026-09-08

Receiver foundation implemented and verified:

- `BrowserWorkspaceState.split` carries independent column and row fractions. `SetSplit` changes
  geometry without recreating renderers, retains ratios when changing layouts, and refuses changes
  during exclusive presentation. An inactive profile cannot acquire resize state.
- Native pane placement and mosaic hit testing use the same integer geometry. Gaps are bounded by
  available pixels; tiny roots never place rectangles outside their bounds. Extreme nudge deltas
  clamp without integer overflow.
- Layout, split, order and theater changes cancel held pointer gestures before the host renders
  new bounds. An orphan release is discarded.
- Existing standalone TV profile snapshots retain split fractions across disk restart; older files
  default to even. Connected-device projections remain excluded from TV persistence.
- Validation: `:receiver:testDebugUnitTest --tests '*BrowserWorkspace*'` passed 60 tests, zero
  failures/errors. Initial sandbox run failed writing Gradle's problems report; the authorized
  rerun succeeded. No new hardware acceptance or latency measurement was performed.

Still required to deliver the requested Windows-controlled customization:

1. Add separately negotiated resize command/state wire messages, preserving existing strict wire
   formats; implement all three codecs and regenerate cross-language golden vectors.
2. Project authoritative ratios into Windows and expose draggable dividers, keyboard resizing,
   equalize, and pane reordering. Bound/coalesce drag updates and handle stale revisions,
   disconnect, pointer capture loss and fullscreen without retargeting a page click.
3. Persist/restore these preferences with Windows device profiles, expose D-pad resize controls
   on TV, and verify actual native layout plus Windows input on hardware.

The split model and reducer are available internally; there is currently no user-facing resize
control or resize protocol. Do not describe Windows dragging as shipped.


## Protocol, desktop controls and profile session continuation ? 2026-09-08

Implemented since the receiver geometry foundation:

- Protocol 4, additive IDs 35/36, strict codecs in C#/Kotlin/Rust and regenerated canonical vectors.
- Windows authoritative mosaic panel, mouse/keyboard divider handles, bounded coalesced resize,
  explicit Tabs/Workspace controls, and selected-tab actions for new or selected panes.
- Receiver switches mode without clearing the retained tab/workspace models. Workspace selection
  is still checked against profile ownership and VPN policy. TV chrome provides resize controls.
- Windows profile documents now include both page collections, active indices, layout, divider
  fractions and presentation mode under existing DPAPI/atomic storage. Profile tests cover restart,
  isolation, defensive copies and preservation when history/bookmarks change.
- A restore coordinator waits for fresh profile-owned snapshots and confirms structural changes.
  Profile changes cancel the previous restore. Failed restore retains the saved snapshot and reports
  a recoverable error. Current implementation restores URLs, not cookies, form text or WebView history.
- Fixed browser dispatch range in Rust after golden decoding exposed the missed ID extension.
- Fixed Tabs preview visibility when a retained workspace still has panes, and switch to Tabs before
  dispatching a tab-strip action.

Validation so far: 529 .NET protocol tests passed; Rust golden tests passed (5, generator ignored).
Focused .NET profile/workspace/restore tests passed (69). Kotlin protocol and receiver workspace
checks passed before the last TV resize UI change; APK build includes that UI. Full Windows testing
exposed outdated grid assertions and a history fixture that never opened the browser; both were
updated, then a parallel run stalled and was aborted by the hang detector. A serial run is pending.

Hardware update was attempted but Android rejected the APK with INSTALL_FAILED_UPDATE_INCOMPATIBLE:
the installed debug package uses another signing key. The existing receiver/profile data was not
removed. Original signing key is needed for an in-place update and current-feature hardware evidence.

Remaining acceptance work is hardware verification. Named Windows profiles, standalone-TV
tab/mode persistence (including cold-start Browse), pane reorder, and restore retry are
implemented. `Flint.App.Tests` passed in three batches (481 tests; a single vstest invocation
can hang on this machine). Restore still reloads URLs only. An in-place APK update still needs
the original debug signing key. The current work must not be described as the entire requested
browser experience being complete.

## Continuation — 2026-09-08 (named profiles, TV session, pane reorder)

- Named Windows profiles: create/select up to eight, persisted with the library document. Each
  profile keeps its own tabs, workspace, bookmarks and history. Switching waits for a fresh
  restore and does not copy pages between profiles.
- Restore retry is offered when a Windows restore fails; the saved snapshot is kept.
- Standalone TV profiles persist tabs, active tab and Tabs/Workspace mode in the library catalog
  (optional fields on format 2). Workspace panes remain in the workspace store. Profile switch
  saves the outgoing session before selecting the next profile, then restores the incoming one.
- Pane reorder is workspace command action 15 (`MOVE_PANE`). Windows mosaic tiles and TV chrome
  expose Move left/right. Renderers are not recreated.
- Hardware verification remains blocked when the installed receiver debug APK is signed with a
  different key (`INSTALL_FAILED_UPDATE_INCOMPATIBLE`). Do not uninstall the TV package without
  the user asking — that would discard on-device profiles.
- Cold start: Browse (`openFromTv`) reloads the saved tab strip and workspace mode. Opening a URL
  from idle keeps that page in front and appends the other saved tabs. Restore still reloads URLs
  only — not cookies, form text, or WebView history.
