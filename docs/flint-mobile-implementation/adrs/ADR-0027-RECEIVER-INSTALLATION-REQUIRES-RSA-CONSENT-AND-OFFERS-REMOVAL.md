# ADR-0027 — receiver installation requires the television's RSA consent and always offers removal

- **Status:** Accepted
- **Implementation:** Planned
- **Date:** 2026-09-11
- **Scope:** Flint Mobile
- **Deciders:** Flint engineering
- **Owners:** Slice 01 (capability honesty and the installation verdict) and Slice 04 (receiver
  installation and removal over ADB); Slice 09 owns the workflow that bundles the package.
- **Related:** ADR-0024, ADR-0025, ADR-0026, ADR-0028, ADR-0029, ADR-0030, and Fire TV browser
  [ADR-0002](../../fire-tv-browser-implementation/adrs/ADR-0002-PLATFORM-AND-DISTRIBUTION-SCOPE.md)
  on evidenced Fire OS platforms and sideload-only distribution
- **Supersedes:** None
- **Superseded by:** None

## Context

The phone is the first Flint host that will actually install the receiver. The desktop product ships
`Flint.Receiver.apk` in its zip and hands the hardest step back to the user: `docs/INSTALL.md` says
to install it "using your normal sideloading method". `AdbReceiverLauncher` can only start a package
somebody else already put there. A phone already speaking ADB to the television has no excuse for
that gap — `AdbConnection.installApk` streams the APK through `exec:cmd package install` and leaves
no copy in the television's temporary storage, so a failed install cannot strand a file the user
must then find and delete.

Installing is where a host can take a decision that is not its to take. The on-screen RSA prompt is
the television owner's only say in what runs on it, and it belongs to whoever owns the room rather
than to whoever holds the phone. The repository already treats it as a contract:
`AdbConnection.connect()` answers the first `AUTH` challenge with a signature and offers the public
key only after that is rejected, so the prompt does not reappear on every reconnection once the
owner has accepted this phone, and `AdbAuthorizationRequiredException` carries the plain remedy
"Accept the USB debugging prompt on the TV, then try again".

The second failure is already in the tree as a constant. `AdbReceiverLauncher.cs` hardcodes
`com.rextechnologies.flint.receiver.debug` as a `const string`. That is right only while the receiver
is a debug build; against a release-signed one it names a package that is not installed, and the
failure reads as the television's fault. Those bytes are knowable here:
`.github/workflows/mobile-release.yml` builds `:receiver:assembleDebug` from the same commit as the
phone, copies it to `mobile/src/main/assets/flint-receiver.apk`, and records the commit and SHA-256
beside it. It is debug-signed because `pm install` refuses an unsigned APK and the receiver's release
key is not available to that workflow, and the receiver's debug build type sets
`applicationIdSuffix = ".debug"`.

## Decision

- The phone installs **only after the television's own RSA authorisation prompt has been accepted**.
  `AdbAuthorizationRequiredException` becomes a Blocked verdict whose remedy is to accept that
  prompt. Flint never writes the device's `adb_keys`, never offers a way around the prompt, and never
  shows an unauthorised television as ready to install.
- Identification before installation is **read-only**: the banner, `ro.product.*` properties and
  package queries, each through `AdbConnection.shell`'s allowlist, which admits no metacharacter
  that could chain a second command.
- Before anything is written, the screen states **exactly what will be installed and why** — package
  name, version name, version code and size **parsed from the bundled APK at runtime**, with the
  recorded commit and SHA-256. No build-time constant names the package, and the `.debug` suffix is
  shown literally rather than trimmed for tidiness.
- **The screen that offers installation offers removal, in the same place, always** — not in a
  settings menu, not behind a developer toggle, and not only once something has failed. Removal is
  `pm uninstall` on the name the phone read, behind a confirmation that names it.
- A Vega OS television is **Not possible, with no remedy**. It is not Android and cannot install an
  APK by any method, so `ReceiverPlatform.VEGA` fails `canInstallReceiver()`, and `ModeVerdict`'s
  `init` already refuses an impossibility carrying a remedy.
- The receiver package and the phone come from **one commit built by one workflow**, which is the
  payoff of keeping `:mobile`, `:castcore`, `:protocol` and `:receiver` in one repository: the
  version offered never drifts from the wire the phone speaks.
- The ADB socket is bound to the selected tether address under ADR-0024.

## Consequences

### Positive

- The step people fail at is done by the app that is already connected.
- Nothing Flint installs can be removed only by somebody who knows where to look.
- The owner keeps the veto, and the prompt keeps meaning what it says.
- The name shown and the name uninstalled both come from the bytes on disk.

### Trade-offs

- The installed package ends in `.debug` and the UI says so. Moving to a release-signed receiver is
  not an upgrade across signatures; it needs the debug package removed first, and that must be stated
  rather than discovered.
- The phone's APK carries the receiver's APK, so the download is larger.
- A television with nobody in front of it cannot be installed to. There is no headless path.
- Parsing the bundled package adds a failure mode — asset missing or unparsable — reported as
  Blocked rather than filled in from a constant.
- The emulator and physical-device tests for install, prompt handling and removal are written but
  have not been run.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Keep a build-time `const` package name and version, as `AdbReceiverLauncher.cs` does. | Correct only for a debug receiver. Against a release-signed one it names a package that is not installed, and the failure reads as the television's. |
| Write this phone's public key into the device's `adb_keys` so no prompt appears. | It needs the authorisation it is skipping, and it removes the owner's only say in what runs on their television. `AGENTS.md` requires the prompt be honoured, not routed around. |
| Put removal on a separate screen, or reveal it only after a failed install. | `AGENTS.md` requires removal wherever installation is offered, and a control that appears only once something has broken is hardest to find exactly when it is needed. |
| Download the receiver from the GitHub release at install time. | While tethering, the phone's upstream is metered mobile data, and the fetched release need not be the commit the phone was built from. |

## Invariants and validation

- A UI test renders the receiver screen and asserts **both affordances appear on it at once**, the
  install action and the removal action, in every state the screen supports. It fails if either
  moves elsewhere or becomes conditional on a prior failure.
- A copy test asserts every Vega verdict is `IMPOSSIBLE` with a `null` remedy and that no Vega reason
  string instructs the reader to do anything. `ModeVerdict` already throws on an impossibility with a
  remedy, so the test guards the copy rather than the type.
- A manifest test parses a fixture APK and asserts the rendered package name, version name and
  version code come from it; changing the fixture must change the text. A `:castcore` source guard
  fails on a receiver package-name literal in `:mobile` outside test sources.
- An `AdbConnection` test drives a scripted device that challenges twice and asserts
  `AdbAuthorizationRequiredException`; a second asserts no install or uninstall is issued before a
  `CNXN` banner is accepted. The allowlist test keeps `;`, `|`, `&`, backticks and redirection
  rejected, so neither path widens into arbitrary command execution.
- The install and removal round trip on a real Fire TV is an opt-in physical-device test that skips
  cleanly with no device attached. It is written, it has not been run, and no result is claimed.

## Revisit criteria

A superseding record needs one of three things: a reviewed receiver release signing key available to
the workflow, which retires the `.debug` package name and the copy explaining it; device evidence
that `exec:cmd package install` is absent on a supported Fire OS level, which changes how the bytes
are streamed but not the consent rule; or an Android change to debugging authorisation that gives
the owner an equivalent say by another mechanism. Convenience, demo pressure, a wish to remove one
tap, and any unmeasured performance claim are not triggers.

## References

- [Project constraints](../../../AGENTS.md)
- [Flint Mobile implementation slices](../README.md)
- [`AdbConnection`](../../../protocol/src/main/kotlin/com/rextechnologies/flint/protocol/adb/AdbConnection.kt)
- [`AdbAuth`](../../../protocol/src/main/kotlin/com/rextechnologies/flint/protocol/adb/AdbAuth.kt)
- [`ReceiverPlatform.canInstallReceiver()`](../../../castcore/src/main/kotlin/com/rextechnologies/flint/castcore/capability/Evidence.kt)
- [`MobileCapabilityAssessor`](../../../castcore/src/main/kotlin/com/rextechnologies/flint/castcore/capability/MobileCapabilityAssessor.kt)
- [`ModeVerdict`](../../../castcore/src/main/kotlin/com/rextechnologies/flint/castcore/capability/Modes.kt)
- [Receiver package bundling](../../../.github/workflows/mobile-release.yml)
- [`AdbReceiverLauncher`](../../../src/Flint.App/Services/AdbReceiverLauncher.cs)
- [Manual sideloading instructions today](../../INSTALL.md)
