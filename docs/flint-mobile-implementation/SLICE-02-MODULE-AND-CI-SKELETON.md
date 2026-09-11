# Slice 02 — module and CI skeleton

**Governing ADRs:** [ADR-0026](adrs/ADR-0026-FLINT-MOBILE-CONSUMES-THE-PROTOCOL-IN-REPO.md) and
[ADR-0028](adrs/ADR-0028-RELEASE-SCOPE-SIDELOADED-APK-FROM-GITHUB.md).

## Outcome

A merge produces a downloadable APK that launches to the capability screen from slice 01. The whole
delivery path exists before there is much to deliver, which is the point: a release pipeline built
after the product is one that gets debugged under pressure.

## Entry criteria

- Slice 01's `:castcore` passes its own gate.

## User-visible vertical behaviour

1. A pull request touching only the phone produces a debug APK a reviewer can install, and does not
   start a forty-five-minute Windows job.
2. A merge to the default branch replaces the rolling `latest` prerelease, so the download link on a
   phone's browser never changes.
3. A `v*` tag cuts an immutable release whose bytes will still be those bytes in five years.
4. Installing it and opening it shows the Cast tab with three honest verdicts.

## Scope

### In scope

- `gradle/libs.versions.toml`, and the three new modules wired into `settings.gradle.kts`.
- `:mobile` as an application: manifest, resources, adaptive icon layers emitted by the existing icon
  script, the R8 rules, and an activity that handles its own configuration changes.
- `.github/workflows/mobile.yml` on `ubuntu-latest`, with path filters and Gradle caching — which
  `ci.yml` has never had.
- `paths-ignore` on `ci.yml` for `mobile/**`, `design/**` and `castcore/**`, and deliberately not for
  `protocol/**`, `testdata/golden/**` or the Gradle files, which both builds genuinely share.
- `.github/workflows/mobile-release.yml`, with signing material read from repository secrets and an
  unsigned build labelled unsigned rather than published as if releasable.
- `versionCode` from the run number plus a fixed offset; `versionName` from `mobile.version` in
  `gradle.properties`, with a tag that disagrees failing the build rather than becoming a second
  source of truth.

### Explicitly out of scope

- Any screen beyond the one slice 01 produced.
- Bundling the receiver APK. That is slice 10's, and the workflow step that stages it is written but
  has nothing to install with yet.

## Planned files and boundaries

`mobile/build.gradle.kts` reads every piece of signing and version material through
`providers.gradleProperty` and `providers.environmentVariable`. It deliberately does not copy
`:receiver`'s pattern of opening `keystore.properties` during configuration: this build has the
configuration cache switched on, and a file read there silently invalidates it, which would make every
phone build a cold one.

## Test-first implementation order

1. The catalog, and the existing modules migrated onto it, so a version can only be wrong in one
   place.
2. `mobile.yml`, red, then green.
3. `ci.yml`'s path filters, verified by a phone-only commit.
4. The release workflow, verified by a merge.

## Full-suite checkpoint

Everything slice 01 ran, plus `:mobile:lintDebug` and `:mobile:assembleDebug`, plus
`:mobile:assembleRelease` on the release path.

## Exit evidence

- A rolling release with an APK attached, installable on a phone.
- R8 having actually run. This is the first time it has in this repository: `scripts/package.ps1`
  only ever assembles the receiver in debug, so the receiver's ProGuard rules have never been
  exercised. Expect to discover missing keeps around BouncyCastle and the reflective
  `KeyManagerFactory` and `KeyStore` provider lookups.

## Rollback and stop conditions

Revert the workflows first and the modules second; nothing outside `mobile/`, `design/`, `castcore/`
and the Gradle files changes. Stop if `ci.yml` is a required status check and the branch rule has not
been updated: GitHub reports a path-filtered workflow as pending rather than passing, so a mobile-only
pull request would wait for a job that is never going to start.
