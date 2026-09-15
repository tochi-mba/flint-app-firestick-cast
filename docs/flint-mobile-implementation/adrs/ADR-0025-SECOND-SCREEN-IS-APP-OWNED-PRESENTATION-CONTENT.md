# ADR-0025 — second screen on Android is app-owned Presentation content, not an OS display extension

- **Status:** Accepted
- **Implementation:** Planned
- **Date:** 2026-09-11
- **Scope:** Flint Mobile
- **Deciders:** Flint engineering
- **Owners:** Slice 01 (capability honesty and the probe) and Slice 06 (second-screen surface and
  encode path); Slice 02 owns the palette and type it renders in.
- **Related:** ADR-0024, ADR-0026, ADR-0027, ADR-0028, ADR-0029, ADR-0030, and Fire TV browser
  [ADR-0007](../../fire-tv-browser-implementation/adrs/ADR-0007-SURFACE-LIFECYCLE-AND-TV-FIRST-UX.md)
  for receiver surface exclusivity.
- **Supersedes:** None
- **Superseded by:** None

## Context

The receiving half of this feature already exists and has never been driven.
`SurfaceMode.PRESENTATION(4)` is on the wire, `ReceiverScreen.kt` routes it through the mirror
surface with a `presentation` flag, and `ReceiverService.kt` writes "Second screen active" on the
first frame. What is missing is a host that sends it.

Windows cannot be that host. `CapabilityAssessor.cs` hard-codes `SecondScreenImplemented = false`:
an extra desktop there is a mirror plus an indirect display driver Flint would have to ship and
sign, and from build 26100 — Windows 11 24H2, unchanged in 25H2 — an IDD display cannot be made
primary. None of that concerns the protocol, the television, or the encoder, so none of it travels
to Android.

Android offers a different mechanism. `DisplayManager.createVirtualDisplay` gives a display backed
by a `Surface` this app supplies, and a `Presentation` is a window on a named `Display`. A virtual
display created without `VIRTUAL_DISPLAY_FLAG_PUBLIC` is private to its creator — no permission and
no consent dialog, because the public flag is what pulls the system permission in.

That claim is load-bearing, and this repository has been burnt by one before. The Windows encoder
once reported thousands of frames sent and a healthy receiver bitrate, with correctly framed H.264
and correct parameter sets, while the television showed flat colour. An API level says a method
exists, not that a vendor's build of it honours the contract.

## Decision

- The second screen is a **private `VirtualDisplay` this app owns**, a **`Presentation` rendering
  Flint's own Compose content** into it, and that display's surface encoded and sent as
  `SurfaceMode.PRESENTATION`. No `MediaProjection`, no consent dialog, no foreground-service
  ordering, no API 34/35 branch.
- Say what it is not, in the same breath as offering it. The mode **cannot extend Android onto the
  television** and **cannot host another app's window**. Every offered verdict carries
  `MobileCapabilityAssessor.SECOND_SCREEN_BOUNDARY` verbatim; a card saying "second screen" without
  it describes the Windows feature rather than this one.
- v1 content is a full-screen player, a photo view, a now-playing surface, and a connection
  dashboard. The phone stays the cockpit; the television is an output, not a copy of the phone.
- This mode ships **first**, ahead of mirror, because it proves the encode-and-send path against the
  simpler surface. The pixels come from a display Flint already draws into, so a failure is in
  Flint's encoder, framing, or transport rather than in a permission flow.
- Support is a runtime probe, never a compile-time constant.
  `PhoneCapabilities.virtualDisplayProbe` is `NOT_PROBED`, `SUPPORTED` or `UNSUPPORTED`.
  `NOT_PROBED` yields **Blocked** with a remedy that runs the check, `UNSUPPORTED` yields **Not
  possible** with no remedy, and only `SUPPORTED` reaches an available verdict.
- Compose inside a `Presentation` is wrapped **once, in one place**. A `Presentation` is a `Dialog`,
  so its decor view is not the Activity's and carries no `ViewTreeLifecycleOwner`,
  `ViewTreeSavedStateRegistryOwner` or `ViewTreeViewModelStoreOwner`. A `ComposeView` resolves all
  three from the view tree on attach and throws when any is absent, so one wrapper sets all three on
  the presentation window's decor view before the `ComposeView` is added.

## Consequences

### Positive

- A mode the protocol has carried since v1 finally has a host, without forking the wire.
- The encode path is proven against a surface with no permission surface area, before mirror adds
  consent, service ordering, and per-capture re-consent on API 35.
- A phone that refuses screen capture can still drive a second screen, because the two modes are
  judged independently rather than as mirror plus something.

### Trade-offs

- The television shows only what Flint draws. There is no route to the phone's home screen or to
  another app's window, and the mode grows one designed surface at a time.
- The receiver's on-screen text is not peer-type aware, so a phone-driven presentation reads exactly
  as a PC-driven one. Making it aware alters text pinned by 15 approved Robolectric snapshots that
  cannot be regenerated without an Android SDK, so it is separate work with its own slice — as is
  extracting `:design` out of the receiver's 36 UI files.
- Windows and Android now differ on the same mode name. The asymmetry is explained on each platform
  rather than hidden by removing the mode from both.
- The instrumented probe test and the device tests for this path are written but have not been run.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Back the second screen with `MediaProjection`. | Buys nothing the private display does not give, and costs consent every session, a `mediaProjection`-typed foreground service running before `getMediaProjection()` on API 34+, a mandatory `MediaProjection.Callback`, and per-capture consent on API 35+. It still cannot host another app's window. |
| Create the display with `VIRTUAL_DISPLAY_FLAG_PUBLIC`. | That flag is the only difference between a display needing a system permission and one that does not, and a sideloaded APK cannot hold it. Android still has no API to move another app's window onto the display. |
| A compile-time constant or an API-level check instead of a probe. | An API level says a method exists, not that this vendor's build works. A Kotlin `const val` also lets the compiler prove the other branch unreachable, failing `:castcore`'s `allWarningsAsErrors` — the trap `CapabilityAssessor.cs` records for C#. |
| Render Compose to an off-screen `Surface` with no `Presentation`. | No `Display` means no display metrics, no window, and no animation host. Every dp would be measured against the phone's density, and the television's size would be invisible to layout. |
| Withhold the mode until Windows can offer it too. | Parity has no user. Suppressing a capability Android has, to match a platform defect elsewhere, is the same misreport as claiming one that does not exist. |

## Invariants and validation

- A copy test over `MobileCapabilityAssessor` fails the build if any second-screen reason or remedy
  claims a desktop extension, the phone's home screen, or another app's window, and if any
  `AVAILABLE` second-screen verdict omits `SECOND_SCREEN_BOUNDARY`.
- A table test drives `virtualDisplayProbe` through all three outcomes, asserting
  blocked-with-remedy, impossible-with-`null`-remedy, and fall-through. `ModeVerdict`'s `init`
  already refuses an impossibility carrying a remedy.
- `ModePresentation` keeps `isComingSoon` and `hasRemedy` as two independent booleans. Tests assert
  that a blocked verdict without a remedy renders no WHAT TO DO block, and that only `AVAILABLE` is
  painted `ToneIntent.SIGNAL`. These live in `:castcore`, gated at 95% line and 85% branch.
- A Robolectric test hosts a `ComposeView` in the wrapper and asserts it composes; three red cases
  remove one owner each and assert the attach fails.
- The probe itself is instrumented and skips cleanly on a machine with no device, as AGENTS.md
  requires. A green unit suite is not evidence that a phone made the display.
- No document or UI string calls the presentation stream private, confidential, or encrypted. The
  session token is authorisation, not encryption.

## Revisit criteria

A superseding ADR is justified by an Android API that genuinely allows an app to place another app's
window on a display it owns, or by device evidence that private virtual displays are refused widely
enough that the probe is the wrong shape. A signed Flint indirect display driver, or a Windows change
letting an IDD display become primary, would revisit the asymmetry rather than anything decided here.
Demo pressure, a wish for parity, and any unmeasured performance claim are not triggers.

## References

- [Project constraints](../../../AGENTS.md)
- [Flint Mobile implementation slices](../README.md)
- [`MobileCapabilityAssessor`](../../../apps/phone/core/src/main/kotlin/com/rextechnologies/flint/castcore/capability/MobileCapabilityAssessor.kt)
- [`PhoneCapabilities.virtualDisplayProbe`](../../../apps/phone/core/src/main/kotlin/com/rextechnologies/flint/castcore/capability/Evidence.kt)
- [`ModeVerdict` and `ModePresentation`](../../../apps/phone/core/src/main/kotlin/com/rextechnologies/flint/castcore/capability/Modes.kt)
- [`SurfaceMode`](../../../protocol/kotlin/src/main/kotlin/com/rextechnologies/flint/protocol/wire/WireMessages.kt)
- [Receiver surface routing](../../../apps/receiver/app/src/main/kotlin/com/rextechnologies/flint/receiver/ui/ReceiverScreen.kt)
- [`CapabilityAssessor.SecondScreenImplemented`](../../../apps/windows/src/Flint.Core/CapabilityAssessor.cs)
- [`Presentation`](https://developer.android.com/reference/android/app/Presentation)
