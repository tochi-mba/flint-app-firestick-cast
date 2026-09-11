# Flint Mobile implementation slices

Flint has been a Windows host casting to a Fire TV. Flint Mobile makes the phone the host instead —
the phone that already runs the hotspot everything else in the house joins — and the television the
screen. Same wire protocol, same receiver, same REX ink/signal visual system. The phone either
mirrors its own screen to the television or drives it as a second screen with the phone as cockpit.

The decisive finding, from reading the repository rather than from planning it, is that most of this
already exists and was written for a phone. The `:protocol` module is a pure-JVM, Android-free host
SDK whose own source says so:

- `session/CastHandshake.kt` documents `SenderHandshake` verbatim as "Phone half of the handshake".
- `net/ReceiverServer.kt` says it "Accepts phone connections on the hotspot LAN", and that "The TV is
  a hotspot client rather than its host".
- `network/NetworkInterfaces.kt` picks the tether interface (`ap*`, `swlan*`, `softap*`,
  `wlan[1-9]*`, `rndis*`) and deliberately excludes `wlan0`, because that is the client interface.
  Tested, and with no subnet assumed anywhere.
- `media/BitrateController.kt` explains its own asymmetry in terms of a phone: "an over-large stream
  on a SoftAP link degrades every other hotspot client".
- `dlna/Ssdp.kt` warns that "on a phone that also has cellular up, an unbound SSDP socket reaches
  neither the TV nor anything else useful".

So this is not a port. The phone-host groundwork is built and tested; the Android application that
consumes it is the missing piece.

## Second screen is the mode Flint on Windows cannot ship

`docs/ENGINEERING_NOTES.md` records that an extended desktop needs an indirect display driver, and
that "Windows 11 24H2 and 25H2 currently cannot set an IDD display as the primary display".
`CapabilityAssessor.cs` hard-codes `SecondScreenImplemented = false` as a result.

None of that is about the protocol or about the television. `SurfaceMode.PRESENTATION(4)` exists in
Kotlin, C# and Rust, and `ReceiverScreen.kt` already renders it as "SECOND SCREEN" today. The blocker
is a Windows driver-signing fact, and Android has no equivalent — so Flint Mobile ships the mode
Flint Windows cannot.

It ships it honestly. On Android the second screen is a private `VirtualDisplay` this app owns and
renders its own Compose content into; it is not an extension of the operating system, it cannot host
another app's window, and every card that offers it says so in the same breath.

## Module topology

| Module | Type | Owns |
|---|---|---|
| `:protocol` | `kotlin("jvm")` | The wire, both handshake halves, hotspot interface selection, ADB, DNS-SD, DLNA, the token-gated HTTP paths. Consumed unchanged. |
| `:castcore` | `kotlin("jvm")` | Android-free phone logic: capability verdicts, the discovery ladder policy, the session state machine, encoder and thermal policy, receiver-setup policy, and every user-facing sentence. |
| `:design` | `com.android.library` | The REX system in Compose — colour, type, spacing, shapes, `Tone`, and the shared components. Built on foundation primitives with no Material dependency. |
| `:mobile` | `com.android.application` | The phone app. `com.rextechnologies.flint.mobile`. |
| `:receiver` | `com.android.application` | Unchanged except for the additive UDP broadcast responder the phone's third discovery rung needs. |

`:castcore` exists as its own module rather than as a source set inside `:mobile` for one reason: only
a separate pure-JVM module can carry the same JaCoCo gate as `:protocol` — 95% line, 85% branch —
with no Robolectric anywhere near it, and can run the source guard that fails the build on a wildcard
socket bind.

## Architecture that is not up for negotiation

- Every socket is bound to the address `HotspotInterfaceSelector` chose. Never a wildcard, never a
  hardcoded subnet, prefix, interface name or address. `:castcore` fails its own build if a source in
  it or in `:mobile` breaks this.
- The wire is never forked. `:mobile` and `:castcore` depend on `project(":protocol")`, and the
  golden corpus stays the authority on what the format is.
- Nothing claims a capability it has not probed. Every probe reports `NOT_PROBED` until it has run,
  and a mode reads Blocked rather than Available or Impossible while that is the answer.
- The cast link is authorised, not encrypted. No string in the app, the logs, the release notes or
  these documents may describe it as private, secure or encrypted. A copy test enforces it.
- No latency figure appears anywhere until it has been measured on real hardware.
  `docs/LATENCY_BUDGET.md` holds measurements; its Flint Mobile section holds targets, labelled as
  targets.

## Slices

Linear. No slice merges another, and each ends in something observable with its own tests and its own
rollback gate.

| # | Slice | First usable outcome |
|---|---|---|
| 01 | [Truthful capability verdict](SLICE-01-TRUTHFUL-CAPABILITY-VERDICT.md) | The phone says plainly what it can and cannot do on this network with this TV, and nothing else. |
| 02 | [Module and CI skeleton](SLICE-02-MODULE-AND-CI-SKELETON.md) | A merge produces a downloadable signed APK that launches to the capability screen. |
| 03 | [Design system](SLICE-03-DESIGN-SYSTEM.md) | Every screen is visibly the same product as the desktop and the TV. |
| 04 | [Discovery on the hotspot](SLICE-04-DISCOVERY-ON-THE-HOTSPOT.md) | The phone finds the TV on its own hotspot, and explains it when it cannot. |
| 05 | [Pair and hold a session](SLICE-05-PAIR-AND-HOLD-A-SESSION.md) | The phone pairs with a code and stays connected; the TV shows the peer name. |
| 06 | [Second screen](SLICE-06-SECOND-SCREEN.md) | The TV shows a Flint-owned surface while the phone is the cockpit. |
| 07 | [Screen mirror](SLICE-07-SCREEN-MIRROR.md) | The phone mirrors itself to the TV. |
| 08 | [Adaptive bitrate and thermals](SLICE-08-ADAPTIVE-BITRATE-AND-THERMALS.md) | The session degrades honestly instead of stuttering. |
| 09 | [Media handoff](SLICE-09-MEDIA-HANDOFF.md) | The phone plays a picked file on the TV. |
| 10 | [Receiver install over ADB](SLICE-10-RECEIVER-INSTALL-OVER-ADB.md) | A phone-only setup works end to end with no PC. |
| 11 | [Accessibility and polish](SLICE-11-ACCESSIBILITY-AND-POLISH.md) | TalkBack, font scale, reduced motion and RTL all work. |
| 12 | [Release hardening](SLICE-12-RELEASE-HARDENING.md) | An evidence-backed go/no-go. |

Slice 06 comes before slice 07 deliberately. The second screen needs no consent dialog, no
foreground-service ordering and no `MediaProjection` API-level minefield, so it proves the whole
encode-and-send path against the simpler surface first.

## Decisions

| ADR | Decision |
|---|---|
| [0024](adrs/ADR-0024-PHONE-BINDS-EVERY-SOCKET-TO-THE-SELECTED-INTERFACE.md) | The phone binds every socket to the selected tether interface. |
| [0025](adrs/ADR-0025-SECOND-SCREEN-IS-APP-OWNED-PRESENTATION-CONTENT.md) | Second screen on Android is app-owned Presentation content, not an OS display extension. |
| [0026](adrs/ADR-0026-FLINT-MOBILE-CONSUMES-THE-PROTOCOL-IN-REPO.md) | Flint Mobile consumes `:protocol` in-repo and never forks the wire. |
| [0027](adrs/ADR-0027-RECEIVER-INSTALLATION-REQUIRES-RSA-CONSENT-AND-OFFERS-REMOVAL.md) | Receiver installation requires the television's RSA consent and always offers removal. |
| [0028](adrs/ADR-0028-RELEASE-SCOPE-SIDELOADED-APK-FROM-GITHUB.md) | Release scope is a sideloaded APK from GitHub, with the publisher warning stated plainly. |
| [0029](adrs/ADR-0029-SESSION-TOKENS-ARE-ENCRYPTED-AT-REST-AND-ARE-AUTHORISATION.md) | Session tokens are stored encrypted at rest and are authorisation, never encryption. |
| [0030](adrs/ADR-0030-ONE-SOURCE-OF-TRUTH-FOR-THE-REX-TOKEN-SET.md) | The REX token set has one source of truth in `:design`. |

## Test commands

```bash
./gradlew :protocol:test                 # the wire, and the golden corpus
./gradlew :castcore:check                # tests, ktlint, and the 95/85 coverage gate
./gradlew :design:testDebugUnitTest      # the token test and the component screenshots
./gradlew :mobile:testDebugUnitTest      # Robolectric and the Compose state tests
./gradlew :mobile:lintDebug
./gradlew :mobile:assembleDebug
```

`:castcore` and `:protocol` need no Android SDK, which is what makes them the two modules a
contributor can always run.

## What is not in this change

Stated here rather than discovered later.

- **`:design` is not yet extracted from `:receiver`.** The receiver still owns its own copy of the
  tokens. Switching it over touches 36 UI files whose rendered text is pinned by 15 approved
  Robolectric snapshots, and those snapshots cannot be regenerated without an Android SDK. It is its
  own change, with its own slice.
- **The receiver's copy is not yet peer-type aware.** `ReceiverIdleSurface.kt` still says "PC
  CONNECTED", "Your Windows PC" and "ENTER ON YOUR PC"; `ReceiverMirrorSurface.kt` still falls back to
  "Your PC" and "Unknown PC"; `PlaybackErrorMessage.kt` still says "the media on your PC". Every one
  of those strings is rendered into an approved snapshot, so changing them is the same problem as
  above and belongs in the same change.
- **The `MEDIA_COMMAND` MIME cap still diverges.** Kotlin caps `mimeType` at 255 bytes; C# and Rust
  cap it at 512. A MIME type between the two encodes on Windows and is refused by Kotlin. The phone
  caps at 255, which is safe in both directions, and reconciling the three implementations — with
  regenerated golden vectors — is outstanding work.
- **No emulator or physical-device test has been run.** The tests that need real hardware are written
  and named as such; none of them has executed. The encoder round trip in particular has not, and
  until it has, nothing in this repository knows whether a phone's encoder produces pixels.
- **The Android modules have not been compiled anywhere yet.** CI is the first thing that will build
  them.

## Governance

Documentation conflicts resolve in this order, matching the browser feature's own rule:

1. `AGENTS.md` project constraints, and any higher-priority instruction.
2. Accepted ADRs in `adrs/`.
3. This plan.

An ADR's **Status** says what the project has chosen. Its **Implementation** field is separate and
says what exists, so an accepted decision never turns into a claim that it has shipped. Every ADR here
is Accepted and Planned or In progress; none is Implemented, because no phone has run any of it.
