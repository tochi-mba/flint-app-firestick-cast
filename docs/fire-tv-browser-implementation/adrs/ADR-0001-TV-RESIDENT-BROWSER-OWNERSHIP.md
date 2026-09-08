# ADR-0001 — TV-resident browser ownership

- **Status:** Accepted
- **Implementation:** Planned
- **Date:** 2026-09-04
- **Deciders:** Flint engineering
- **Owners:** Slices 01 and 04–09
- **Related:** ADR-0003, ADR-0006, ADR-0007, ADR-0009

## Context

The requested experience is to browse from the Windows Flint app while the actual page runs on a
Fire TV Stick. Several technically plausible designs would put a browser, proxy, page DOM, cookie
jar, or render pipeline on Windows and merely stream pixels to the TV. Those designs conflict with
the product requirement, add credentials/privacy exposure, increase latency variance, and blur
which device owns website traffic.

The target is constrained Android-based Fire OS hardware. Its WebView must remain lifecycle-correct
and D-pad usable, while the Windows app must remain responsive without becoming a second browser.

## Decision

The Android receiver exclusively owns:

- WebView construction/destruction, navigation, page rendering, cookies/storage, page network
  requests, renderer errors, native input dispatch, and TV display;
- canonical browser state generation and safe status/error classification;
- the sole page surface while Browser mode is active.

The Windows app exclusively owns:

- selected-receiver connection context, certificate verification/pinning UX, typed remote commands,
  safe state display, and an optional bounded preview consumer;
- no WebView/Chromium, no page fetch/proxy/cache, no DOM/HTML/JavaScript relay, no cookie handling,
  and no render-to-TV pipeline.

The TV display is canonical. Any Windows preview is an optional observation of the receiver's
already-rendered page, never an alternate authority.

## Consequences

### Positive

- Website network traffic and browser session state have a single, explainable owner: the Fire TV.
- Browser control latency avoids a host-render/encode/decode/render round trip for the main display.
- Security boundaries are smaller: no host DOM or browser credential store needs to be designed.
- The receiver can remain locally usable with its D-pad during a controller disconnect.

### Trade-offs

- Android WebView/API/provider compatibility becomes a physical-device gate, not a desktop-browser
  assumption.
- Windows cannot promise desktop-browser extensions, downloads, or arbitrary page inspection.
- Optional preview must be built as a carefully bounded feedback path rather than reused as a
  primary screen-mirroring channel.
- Browser, Media, and Screen surface ownership must be explicit and mutually exclusive.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Windows browser renders and streams/mirrors to TV | Violates TV-resident browsing, adds latency and host credential exposure, duplicates renderer ownership. |
| Windows HTTP proxy plus TV renderer | Still places page traffic/credentials in the host path and changes website/network semantics. |
| DOM/JavaScript bridge between host and WebView | Insecure, brittle across sites, violates native-bridge policy, and is not needed for typed control. |
| Cloud browser relay | Adds account/privacy/availability/latency cost outside Flint's local-first scope. |
| TV-only browser with no Windows involvement | Does not meet the remote-control/status experience; typed BrowserSession remains necessary. |

## Invariants and validation

- A source/static review prevents adding host-side web fetch/DOM/bridge code to the browser feature.
- Browser state never contains DOM, cookies, headers, form values, or page source.
- Android instrumentation proves navigation occurs in receiver WebView; Windows tests prove its page
  is a control/status surface.
- Physical Fire TV evidence verifies D-pad/local recovery independently of the Windows preview.
- Preview tests assert it is opt-in, bounded, and never required to operate the browser.

## Revisit criteria

Supersede this ADR only if a product requirement explicitly changes browser ownership and a new
architecture proves equivalent security, privacy, D-pad UX, and measured latency/resource behaviour.
A desire for “smoother preview” or a platform quirk is not enough to move the renderer to Windows.

## References

- [Slice 04 — secure TV-local navigation](../SLICE-04-SECURE-TV-LOCAL-NAVIGATION.md)
- [Slice 07 — passive preview](../SLICE-07-OPT-IN-PASSIVE-PREVIEW-AND-LATENCY-TUNING.md)
- [Master architecture](../../FIRE_TV_BROWSER_PLAN.md#architecture)
