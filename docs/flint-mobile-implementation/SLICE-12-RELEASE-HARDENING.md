# Slice 12 — release hardening

**Governing ADRs:** [ADR-0028](adrs/ADR-0028-RELEASE-SCOPE-SIDELOADED-APK-FROM-GITHUB.md) and
[ADR-0029](adrs/ADR-0029-SESSION-TOKENS-ARE-ENCRYPTED-AT-REST-AND-ARE-AUTHORISATION.md).

## Outcome

An evidence-backed go or no-go. Not a feeling about whether it is ready, and not a green CI badge
standing in for one.

## Entry criteria

- Slices 01 to 11 merged, each having passed its own gate.

## What has to be true

1. The full suite passes: `:protocol`, `:castcore` at 95/85, `:design`, `:mobile`, `:receiver`, and
   the emulator job.
2. A physical-device run is recorded against a named phone and a named Fire TV, with every step of
   the end-to-end walk-through performed by hand.
3. `docs/LATENCY_BUDGET.md` has its Flint Mobile section, every figure in it labelled a target, and
   the measurement procedure written down so a later slice can simply run it.
4. The release workflow has produced a signed APK, and that exact APK has been installed on a clean
   phone from the GitHub release and walked through end to end.

## The end-to-end walk-through

Turn on the phone's hotspot. Join the Fire TV to it. Open Flint Mobile. Read the capability card. Let
it find the television. Enter the code the television shows. Start Second Screen and confirm the
television reads "SECOND SCREEN". Start Mirror and confirm it reads "SCREEN MIRROR". Rotate the phone
and confirm the television follows. Then download the APK from the rolling GitHub release onto a
clean phone and do all of it again.

The second pass is the one that matters. The first proves the code works; the second proves the thing
somebody can actually download works.

## What may not appear

No latency figure. Glass-to-glass has never been measured on a phone, the 240 fps camera procedure
has not been run against any named pair, and until it has, every number in the mobile budget section
is a target and says so. A figure that appears without that procedure behind it is not evidence, it
is a claim — and this project has a rule about those.

## Release checklist

- The keystore exists, is backed up somewhere that will still exist in five years, and its four
  secrets are set: `MOBILE_KEYSTORE_BASE64`, `MOBILE_KEYSTORE_PASSWORD`, `MOBILE_KEY_ALIAS`,
  `MOBILE_KEY_PASSWORD`. A new key is a new app identity with no upgrade path for anyone who
  installed the old one.
- The mobile check is required before merge, so the release job never runs on red.
- `mobile.version` in `gradle.properties` matches the tag being cut, which the workflow enforces.
- R8 has run and the resulting APK has been exercised, not merely produced. This is the only R8 path
  in the repository.
- `mapping.txt` is attached to both the rolling and the tagged release.

## Exit evidence

- A recorded physical-device run, naming the phone and the television.
- The full suite, green, including the emulator encoder round trip that asserts pixels rather than
  structure.
- The latency budget section, with its targets labelled and its procedure written.
- A clean-phone install from the published release, walked through end to end.

## Rollback and stop conditions

A tagged release is immutable by design, so the rollback is a new tag rather than a moved one. Stop
and do not tag if any of the following is true: the emulator round trip has not run, the physical
device walk-through has not been performed on the exact artefact being released, or any figure in the
documentation is presented as measured when it is a target.
