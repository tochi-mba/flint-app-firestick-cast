# ADR-0028 — release scope is a sideloaded APK from GitHub with the publisher warning stated plainly

- **Status:** Accepted
- **Implementation:** Planned
- **Date:** 2026-09-11
- **Scope:** Flint Mobile
- **Deciders:** Flint engineering
- **Owners:** Slice 01 (the module build and the version name the About card reads) and Slice 09
  (the release workflow, signing, and the published notes)
- **Related:** ADR-0024, ADR-0025, ADR-0026, ADR-0027, ADR-0029, ADR-0030, and Fire TV browser
  [ADR-0002](../../fire-tv-browser-implementation/adrs/ADR-0002-PLATFORM-AND-DISTRIBUTION-SCOPE.md),
  which already fixes this project's distribution scope as a sideload experiment
- **Supersedes:** None
- **Superseded by:** None

## Context

The phone app is the first thing in this repository that an ordinary person installs on a device
they own. The desktop product is a zip: `docs/INSTALL.md` says to download
`Flint-<version>-win-x64.zip`, extract it, and install the bundled `Flint.Receiver.apk` "using your
normal sideloading method". A phone has no equivalent of extracting a zip somewhere you can find
again. Android either installs a package or refuses, and when the package did not come from a store
it first warns that the developer is unknown.

Two failures already in the tree set the rest of the shape. Versioning here has two sources and no
arbiter: `release.yml` resolves the version from `refs/tags/v*`, `scripts/package.ps1` defaults to
`<VersionPrefix>` in `Directory.Build.props`, and nothing compares them, so a tag that disagrees
produces a package named one thing and stamped another. On Android that is worse than cosmetic:
`versionCode` is what decides whether an install is an upgrade at all. Second, R8 has never run in
this repository. `receiver/build.gradle.kts` sets `isMinifyEnabled = true` and names
`proguard-rules.pro`, which keeps `GoBackend`, its inner `VpnService` and `SharedLibraryLoader`
because WireGuard resolves them by name. Nothing builds that
variant — `package.ps1` runs `:receiver:assembleDebug` and `release.yml` packages the same debug
output — so a shrinker configuration nobody has run is one nobody knows is wrong.

## Decision

- Distribution is a **sideloaded APK attached to a GitHub release**, two cadences from one job in
  `.github/workflows/mobile-release.yml`. Every merge to `master` replaces a rolling `latest`
  prerelease, so the link a phone's browser is pointed at never changes; a `v*` tag cuts an
  immutable release that is still exactly those bytes years later. There is no store listing.
- The release notes **state the publisher warning and call it accurate**, in those words. Nobody has
  reviewed this app, and the notes do not invite the reader past a true statement. They also give
  the platform floor — Android 8.0, matching `mobile-min-sdk` — and say Vega OS cannot work.
- Signing uses **one fixed key** decoded at job time from `MOBILE_KEYSTORE_BASE64` and its three
  companion secrets. A new key is a new app identity: Android refuses to upgrade across signatures,
  so anyone holding the old build must uninstall first, discarding the Keystore-wrapped session
  token and the persisted ADB RSA identity. `mobile/build.gradle.kts` configures signing only when
  **all four** values are present, because a half-configured block produces an APK signed with
  something nobody chose.
- An **unsigned build is labelled unsigned**: the job says so, builds anyway, and names the file
  `Flint-Mobile-<version>-unsigned.apk`. It never carries the name a releasable build would.
- `versionCode` is `1000 + GITHUB_RUN_NUMBER` — monotonic and reproducible from the run alone, the
  offset clearing codes a hand-built APK may already occupy on somebody's phone.
- `versionName` has **exactly one source**, `mobile.version` in `gradle.properties`. Anything that
  is not a tag build appends a seven-character commit sha. A `v*` tag that disagrees with that
  property **fails the job** rather than becoming a second source of truth.
- The artefact is a **universal APK**: no ABI splits, no app bundle. The phone app ships no native
  code of its own, and the ABI-specific bytes it carries are sealed inside the receiver APK it
  bundles as an asset, which a split cannot divide.
- **R8 runs on this path and nowhere else.** `:mobile:assembleRelease` runs on every merge, not only
  on tags, so a keep rule that stops being sufficient fails at a merge rather than at a release, and
  the mapping file is attached beside the APK.

## Consequences

### Positive

- The download link never changes, and a tagged build never changes underneath anyone.
- The warning the user sees and the words in the notes agree, so the app never asks to be trusted
  more than it has earned.
- One key means every later build is an upgrade rather than a second app.
- The version name and the version code each trace to one input: a property file and a run number.
- The shrinker finally runs here, on the module whose rules can be fixed without touching the
  receiver's snapshots.

### Trade-offs

- Every install crosses the unknown-developer warning and a per-source install grant. Nothing short
  of a store listing this project has not sought removes that step.
- A release APK assembled locally, without `-Pmobile.versionCode`, carries `versionCode` 1 and an
  unsuffixed name. It is a developer artefact and must not be handed to anyone.
- Losing the keystore ends the upgrade path for everyone; key custody is an obligation this decision
  creates rather than solves.
- The universal APK is larger than a split would be, and larger again for the bundled receiver.
- The phone's rules will be exercised; the receiver's still will not. Extracting `:design` out of
  `:receiver` and making the receiver peer-type aware both change text pinned by fifteen approved
  Robolectric snapshots that cannot be regenerated without an Android SDK, so they are separate work
  with their own slice.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Publish through Google Play or the Amazon Appstore so no warning appears. | Play App Signing moves the key out of this repository's control, so the store build is a different identity that no sideloaded install can upgrade into; browser ADR-0002 already restricts this project to sideload pending written approval. |
| Ship an Android App Bundle with ABI and density splits. | An `.aab` is not installable by the person who downloaded it — only a store can resynthesise an APK from it — and the phone app has no native code of its own, so the splits would shave nothing measurable. |
| Mint a fresh signing key per release, or commit the keystore. | A new key is a new identity with no upgrade path, forcing an uninstall that discards the stored session token and ADB identity; a committed key lets anyone who forks the repository sign something that installs as this app. |
| Take `versionName` from the git tag, as `release.yml` does for the desktop. | A rolling build has no tag at all, and a hand-created tag is exactly the second source of truth that already lets the desktop package name and its stamped version disagree unnoticed. |
| Derive `versionCode` from a timestamp, or edit it by hand. | A timestamp is monotonic but not reproducible from the run that made it; a hand-edited code goes backwards on a revert, and Android refuses to install a lower code over a higher one. |

## Invariants and validation

- **A release-notes copy test.** A `:castcore` test reads `mobile-release.yml`, extracts both
  published `body:` blocks, and puts them through `HonestyRules`: no confidentiality word, no
  exclamation mark, every sentence complete. It also asserts the rolling notes still contain the
  unknown-developer sentence and still call that warning accurate, so softening or deleting it fails
  the build rather than a review.
- A version test asserts `gradle.properties` holds exactly one `mobile.version` line, and a source
  guard fails on a version literal in `:mobile` outside test sources. A second drives the resolve
  step with a mismatched tag and asserts a non-zero exit.
- A `versionCode` test asserts the offset formula is monotonic across increasing run numbers and
  that no release path falls back to the local default of 1. A signing test asserts that with any of
  the four values absent the release variant has no signing config at all rather than an incomplete
  one, and that the file name then carries `-unsigned`.
- The emulator and physical-device install tests are written and have not been run. No result is
  claimed for them.

## Revisit criteria

A superseding record needs one of three things: a written store distribution approval with a
reviewed key-custody plan that states what becomes of existing sideloaded installs; native code
genuinely entering the phone app, which would make ABI splits a measured saving rather than an
assumed one; or an Android change to how a browser-downloaded package may be installed on the
supported API range. A wish to remove the warning dialog, download size alone, demo pressure, and
any unmeasured performance claim are not triggers.

## References

- [Project constraints](../../../AGENTS.md)
- [Flint Mobile implementation slices](../README.md)
- [Mobile release workflow](../../../.github/workflows/mobile-release.yml)
- [Mobile pull-request gate](../../../.github/workflows/mobile.yml)
- [`mobile/build.gradle.kts`](../../../mobile/build.gradle.kts)
- [`gradle.properties`](../../../gradle.properties)
- [`HonestyRules`](../../../castcore/src/main/kotlin/com/rextechnologies/flint/castcore/copy/Honesty.kt)
- [Desktop packaging script](../../../scripts/package.ps1)
- [Receiver shrinker rules that have never run](../../../receiver/proguard-rules.pro)
- [Manual sideloading instructions today](../../INSTALL.md)
