# ADR-0023 — independent-page browser workspace and workspace-scoped VPN

- **Status:** Accepted
- **Implementation:** In progress
- **Date:** 2026-09-06
- **Scope:** Fire TV browser
- **Deciders:** Flint engineering
- **Owners:** TV browser UX and networking workstreams
- **Related:** ADR-0001, ADR-0006, ADR-0007, ADR-0012, ADR-0018
- **Supersedes:** ADR-0019, ADR-0020, ADR-0021, and the implementation claims in ADR-0022

## Context

The earlier "mosaic" experiment rendered placeholder cards and described a video product. The
requested product is different: a viewer creates independent web pages, sees them in pane slots,
and can operate each page with a Fire TV remote. A video is merely content inside one of those
pages. The old scaffold must not be presented as a working multi-page browser.

Android WebView and `VpnService` impose two important boundaries. A bounded number of WebViews can
be resident at one time on a Stick-class device, and Android has one active VPN per Android user;
it cannot route individual WebViews or panes through separate VPNs.

## Decision

- The product term is **Browser Workspace**. It contains independent browser pages in panes; it
  is never called a video mosaic.
- One service-owned workspace is authoritative. Compose only renders its state; it must not create
  a private pane collection with `remember`.
- Initial capability is one or two simultaneously live pages. More visible saved pages may be
  suspended, and the UI must say so. A 2×2 layout is unavailable until real target-device PSS and
  concurrent-decode evidence permits it.
- Each pane has an opaque stable id, independent URL/title/history/loading state, renderer
  residency, and page-requested fullscreen state. A page fullscreen custom view is clipped to the
  requesting pane. A separate explicit theatre mode may enlarge a focused pane.
- D-pad ownership is explicit: workspace chrome moves pane focus; Select enters the focused page;
  Back exits that focused page's fullscreen, returns to workspace chrome, then leaves the
  workspace. No touch-only operation is permitted.
- Play/pause and mute are targeted best-effort requests to exactly one pane. They are not displayed
  as confirmed media truth unless the renderer can actually verify it. System-wide audio controls
  are never used as a substitute for a pane mute control.
- User-created panes do not relax the popup policy. Web-page `window.open()` remains denied.
- A persistent TV profile owns its saved workspace metadata. A connected-device profile is an
  ephemeral projection: its workspace data is discarded on disconnect and never persisted by the
  receiver. Existing profile data does not imply cookie or sign-in isolation until multi-profile
  WebView capability has been verified on the target WebView provider.
- The optional VPN is one **TV browser-workspace VPN**. It applies to every current pane in the
  workspace. Only a local TV profile may own encrypted VPN configuration; a device projection may
  not inherit, configure, or persist it. Profile changes apply on the next workspace start unless
  the viewer explicitly restarts the workspace.
- VPN consent is a local Activity-result action. Auto-connect never bypasses it. If the viewer
  selects "require VPN before pages load", connection failure is fail-closed rather than silently
  using the normal network. System always-on VPN is not the product contract.
- Browser workspace wire traffic is a protocol-v3 family with real version negotiation. v2 peers
  keep the existing single-tab cockpit and never receive unknown workspace messages.

## Consequences

### Positive

- A pane is an actual independently navigated WebView rather than a fabricated player card.
- Renderer and decode limits remain explicit and testable.
- Fullscreen, remote focus, device-profile teardown, and network scope have one owner each.
- VPN claims match Android's actual routing model and consent requirements.

### Trade-offs

- A page can be suspended and restored instead of remaining live when the TV cannot safely keep it
  resident.
- A local profile currently isolates workspace/library metadata only. Cookie isolation remains a
  feature-gated future capability, not an implication of the profile picker.
- Per-pane VPN, per-pane volume, automatic unverified media state, and silent full-tunnel fallback
  are explicitly out of scope.

## Invariants and validation

- Pure reducer/property tests cover pane identity, layout/focus transitions, renderer caps,
  fullscreen ownership, profile-source teardown, and 10,000-action sequences.
- Renderer bindings prove that a page has one driver, one pane bounds, and a pane-local custom-view
  host. Hardware tests measure PSS/decode/thermal behaviour before increasing capacity.
- Kotlin, C#, and Rust codecs use byte-identical v3 golden vectors and test v2 fallback.
- VPN storage is authenticated encryption backed by Android Keystore, excludes backup/export,
  rejects device-profile ownership, clears credentials on profile deletion, and is covered by
  migration/corruption/redaction tests.
- A physical Fire TV test with a disposable WireGuard peer proves consent, route survival for the
  pinned controller, disconnect/revoke, and fail-closed behavior before enabling VPN in release UI.

## References

- [Android VPN guide](https://developer.android.com/develop/connectivity/vpn)
- [`VpnService.Builder`](https://developer.android.com/reference/android/net/VpnService.Builder)
- [`WebViewCompat.setProfile`](https://developer.android.com/reference/androidx/webkit/WebViewCompat#setProfile(android.webkit.WebView,%20java.lang.String))
- [ADR-0012](ADR-0012-USER-INITIATED-BOUNDED-TABS.md)
