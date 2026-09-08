# ADR-0006 — fixed WebView security profile

- **Status:** Accepted
- **Implementation:** Planned
- **Date:** 2026-09-04
- **Deciders:** Flint engineering
- **Owners:** Slices 01, 04, 06, and 09
- **Related:** ADR-0001, ADR-0003, ADR-0007, ADR-0010

## Context

WebView is an embedded browser engine with powerful default integration points. A feature that lets
a user navigate arbitrary sites must resist local resource access, dangerous URI schemes, SSL
click-through, popup/external-app escape, web permission prompts, and native JavaScript bridge
exposure. “We will add security settings later” is unsafe because the first public navigation would
already exist.

The target includes API-25 Fire OS, so optional newer WebView APIs cannot be assumed.

## Decision

The initial production WebView is created only through one tested BrowserWebViewDriver and receives
a fixed security profile before any public navigation:

- permit only absolute HTTPS URLs with validated host/length; run the same policy on initial and
  redirect/override navigation;
- deny HTTP, file, content, data, javascript, intent, market, tel, mailto, and custom schemes;
  reject relative, user-info, malformed, control-character, and oversized addresses;
- enable JavaScript only for normal modern-site compatibility, but ban addJavascriptInterface,
  evaluateJavascript, WebMessagePort, WebMessageListener, injected scripts, DOM extraction, and
  remote debugging in release;
- disable file/content and file-URL cross-origin access, disallow mixed content, and use
  WebViewAssetLoader HTTPS appassets fixtures rather than file URI tests;
- deny multiple windows/popups, external intents, uploads/file chooser, downloads, geolocation,
  media capture, and web permissions;
- cancel SSL errors unconditionally; no user consent dialog converts a website cert error into trust;
- disable third-party cookies by default. No compatibility exception is added without a future ADR;
- use AndroidX feature gates for optional API/provider features and document API-25 recovery limits.

## Consequences

### Positive

- Security policy is centralized, pure/testable where possible, and present before first site load.
- Test fixtures exercise the same HTTPS semantics as production instead of accidentally enabling
  local file privileges.
- UI can present clear safe error categories rather than raw exception dumps.
- A hostile page cannot obtain an unreviewed native/OS escape route through this feature.

### Trade-offs

- Some sites/features will not work, especially popups, uploads, custom-scheme handoff, permissions,
  some cookies, downloads, and DRM/video flows.
- JavaScript remains enabled, so the no-bridge/no-escape policy and instrumentation are essential.
- WebView compatibility claims must remain a tested observation matrix, not a blanket promise.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Enable broad WebView defaults then block known bad sites | Fails open for unknown paths and makes security non-auditable. |
| Native JavaScript bridge for text/control/status | Expands attack surface and violates receiver ownership/input design. |
| File URI fixtures for simple tests | Normalizes file access and hides production security differences. |
| User click-through SSL warning | Transfers a technical security decision to users and permits MITM. |
| Allow all URI schemes/external intents | Lets arbitrary pages leave Flint's controlled experience. |

## Invariants and validation

- URL policy has exhaustive table/property/fuzz/mutation cases.
- Robolectric/driver tests assert every setting/callback; source guards detect prohibited bridge APIs.
- Espresso-Web/WebViewAssetLoader tests prove safe fixture navigation, redirect policy, and denial of
  unsafe URI, mixed content, SSL, popup, chooser, permission, and intent paths.
- API-26 renderer crash handling is isolated from API-25 fallback evidence; no nonexistent callback
  is claimed.
- Normal logs/state contain bounded safe URL/title/error metadata only, never page source/cookies.

## Revisit criteria

Any relaxation (HTTP, custom schemes, upload/download, permission, third-party cookies, native
bridge, or cert exception) needs a dedicated ADR with threat model, least-privilege design,
instrumentation/physical evidence, bounded UX, and updated compatibility/privacy docs.

## References

- [Slice 04 — secure navigation](../SLICE-04-SECURE-TV-LOCAL-NAVIGATION.md)
- [Android WebView security guidance](https://developer.android.com/privacy-and-security/risks/insecure-webview-native-bridges)
