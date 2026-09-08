# Research record — Fire TV-resident browser controlled by Flint

**Audience:** Flint maintainers  
**Date:** 2026-09-04  
**Decision:** Whether, and how, Flint can let Windows control a browser that executes and renders on a Fire TV receiver.

## Direct answer

Yes, on Android-based Fire OS devices that can run the Flint receiver, the receiver can host an Android `WebView` while the Windows app supplies native navigation and remote-input controls. The Windows machine must not host a second browser, proxy page traffic, inject JavaScript, or receive DOM data. The TV fetches pages and owns cookies, authentication, JavaScript, and rendering.

For a genuine “browse in Flint” experience, the receiver must return a deliberately lossy, opt-in visual preview plus sanitized browser state. Without that reverse channel, Flint can only operate the TV browser while the TV remains the sole display.

The current Flint control socket is authorized but explicitly not encrypted. Browser typing, URLs with secrets, and visual previews must therefore use a new, separately authenticated and certificate-pinned TLS session. A plain-session fallback is unacceptable for browser controls.

## Scope and assumptions

- The first target is the existing AFTMM-class receiver: Fire OS 6 / Android API 25.
- The receiver is sideloaded or otherwise already installable. Vega OS is out of scope because it cannot run the Android receiver.
- The product must remain fully D-pad operable. A PC pointer is an enhancement, never the only escape route.
- The plan covers an open-web browser, not DRM circumvention, provider automation, a cloud relay, a host-side Chromium instance, or remote debugging in production.
- The requested deliverable is an implementation-ready Markdown plan, not a code change.

## Consequential findings

| Finding | Evidence and consequence | Confidence |
|---|---|---|
| Android `WebView` is an embeddable renderer, not a complete browser. | Android requires the app to provide its own navigation/browser chrome. Flint must build a native TV/Windows UX instead of expecting an address bar or history UI for free. [Android: Build web apps in WebView](https://developer.android.com/develop/ui/views/layout/webapps/webview) | High |
| The current target is constrained legacy hardware. | Amazon lists AFTMM as Fire OS 6/API 25 with 1.5 GB RAM; API 25 lacks API-26 renderer-death and Safe Browsing callbacks. Use one WebView, no tabs/preloading, feature gates, and a mandatory physical-device spike. [Amazon: AFTMM specifications](https://developer.amazon.com/docs/device-specs/device-specifications-fire-tv-streaming-media-player.html?v=ftvsticklite), [Amazon: Fire OS overview](https://developer.amazon.com/docs/fire-tv/fire-os-overview.html), [Android: WebViewClient](https://developer.android.com/reference/android/webkit/WebViewClient) | High |
| Fire TV distribution has a policy gate. | Amazon says third-party browser apps on Fire tablets and Fire TV are restricted. The feature may be developed/sideloaded, but it must not be marketed or submitted as generally publishable without written Amazon approval. [Amazon: Device filtering and browser apps](https://developer.amazon.com/docs/app-submission/device-filtering-and-compatibility.html) | High |
| Fire TV is D-pad first. | All visible TV controls need reachable focus, Select activation, and a predictable Back route. Host mouse input cannot replace this. [Amazon: Fire TV design guidance](https://developer.amazon.com/docs/fire-tv/design-and-user-experience-guidelines.html), [Android: TV navigation](https://developer.android.com/training/tv/get-started/navigation) | High |
| Arbitrary page JavaScript must not get a native Flint bridge. | Android warns that `addJavascriptInterface` exposes native functions to untrusted web content. Browser control must stay in Flint’s socket protocol and native view APIs. [Android: insecure WebView native bridges](https://developer.android.com/privacy-and-security/risks/insecure-webview-native-bridges) | High |
| The web renderer needs explicit hardening. | Explicitly deny file/content access, file-URL cross access, unsafe URI schemes, mixed content, popups, web permissions and file chooser requests. SSL failures must be cancelled, not presented as a user choice. [Android: unsafe file inclusion](https://developer.android.com/privacy-and-security/risks/webview-unsafe-file-inclusion), [Android: unsafe URI loading](https://developer.android.com/privacy-and-security/risks/unsafe-uri-loading), [Android: WebSettings](https://developer.android.com/reference/android/webkit/WebSettings), [Android: SslErrorHandler](https://developer.android.com/reference/android/webkit/SslErrorHandler) | High |
| Browser text must not use the current control transport. | Flint’s own `AGENTS.md` correctly says tokens authorize but do not encrypt. A browser session needs a TLS channel with certificate pinning and user-visible first-pair confirmation. Android supports TLS 1.2 on API 25; .NET provides `SslStream` certificate callbacks. [Android: SSLSocket](https://developer.android.com/reference/javax/net/ssl/SSLSocket), [.NET: SslStream](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream) | High |
| WebView lifecycle and renderer recovery are real risks. | WebView calls belong to its creation/UI thread. A dead renderer’s WebView must be destroyed and replaced; that callback begins at API 26, so API 25 has a weaker tested fallback. [Android: WebView](https://developer.android.com/reference/android/webkit/WebView), [Android: Handle WebView termination](https://developer.android.com/develop/ui/views/layout/webapps/handle-termination) | High |
| Browser video/DRM cannot be promised. | Amazon documents WebView video resource-release and navigation instability risks. Native Fire TV DRM capability does not prove a provider works in WebView. Keep video/DRM experimental and unclaimed. [Amazon: Fire TV FAQ](https://developer.amazon.com/docs/fire-tv/faq-general.html) | High |
| Real WebView testing needs both fixtures and hardware. | Espresso-Web tests native/WebView interaction; `WebViewAssetLoader` gives secure, deterministic local fixtures. Those do not substitute for the actual Fire OS provider, D-pad, memory, or preview capture test. [Android: Espresso-Web](https://developer.android.com/training/testing/espresso/web), [Android: WebViewAssetLoader](https://developer.android.com/reference/androidx/webkit/WebViewAssetLoader) | High |

## Existing Flint evidence

- `receiver/src/main/kotlin/.../ReceiverService.kt` already owns the paired receiver session and `ReceiverUiState`, but must **not** own a `WebView` because the view requires the activity main thread.
- `receiver/.../ui/ReceiverScreen.kt` already uses `AndroidView` for native player/mirror surfaces, which is the correct lifecycle pattern for a browser surface.
- `src/Flint.Session/CastSession.cs` already serializes a duplex TCP stream but only dispatches media/stats feedback. A browser needs a separate secure session and a bounded reverse state/preview stream.
- `src/Flint.Protocol/WireMessage.cs` and Kotlin `WireMessages.kt` contain generic pointer/key/text controls, but the receiver currently ignores pointer/text. Browser input needs typed, platform-neutral semantics and an actual WebView adapter.
- `ReceiverServer.send()` currently creates a coroutine per outgoing message. It cannot be used directly for preview frames; the browser needs a single writer with bounded control and latest-only preview mailboxes.
- Cross-language protocol parity is already inconsistent: Rust `flint-engine/src/wire` only models earlier message IDs while C#/Kotlin include media extensions. The browser work must repair this before claiming byte-exact three-language compatibility.

## Rejected designs

1. **Windows WebView/Chromium mirrored to the TV:** violates the request because browsing, cookies, and rendering would live on Windows.
2. **A host-side HTTP proxy or remote DevTools control path:** creates a privacy/security boundary, depends on Fire OS private-LAN behaviour that is already unreliable, and turns the product into a proxy rather than a receiver-resident browser.
3. **Injecting host-supplied JavaScript with `evaluateJavascript` or an Android JS bridge:** breaks page isolation and is unsafe for arbitrary sites.
4. **Using the present plaintext paired socket for passwords/preview:** violates Flint’s own authorization-versus-encryption rule.
5. **A second unbounded H.264 reverse-mirror pipeline as the first preview implementation:** too costly and risky on API-25/1.5-GB hardware. Start with bounded opt-in JPEG previews; add video feedback only after measured need and separate design review.
6. **Appstore-first distribution:** Amazon explicitly restricts browser publication; policy approval is a gate, not something to work around.

## Research stopping rule

Discovery covered official Android WebView, security, lifecycle and test documentation; official Amazon Fire OS, Fire TV UX, device and publishing-policy documentation; official .NET TLS documentation; and the complete relevant Flint protocol/session/receiver/UI test seams. The remaining material unknowns are provider- and hardware-specific (`WebView` capture, text injection, page/video compatibility, and TLS key/cipher availability on AFTMM); the plan makes them explicit physical-device gates rather than treating further web search as proof.
