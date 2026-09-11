# ADR-0030 — the REX token set has one source of truth in :design

- **Status:** Accepted
- **Implementation:** Planned
- **Date:** 2026-09-11
- **Scope:** Flint Mobile
- **Deciders:** Flint engineering
- **Owners:** Slice 02 (the REX design system in `:design`) and Slice 01 (modules, build and the
  module graph); Slice 06 consumes it for the second-screen surfaces and declares nothing of its own
- **Related:** ADR-0024, ADR-0025, ADR-0026, ADR-0027, ADR-0028, ADR-0029, and Fire TV browser
  [ADR-0007](../../fire-tv-browser-implementation/adrs/ADR-0007-SURFACE-LIFECYCLE-AND-TV-FIRST-UX.md),
  which makes D-pad focus and 5% overscan margins first-class on the television and is therefore the
  record this one deliberately does not follow onto a handset
- **Supersedes:** None
- **Superseded by:** None

## Context

The REX ink/signal system already exists three times. It is Avalonia resources in
`src/Flint.App/Theme/FlintColors.axaml` and `FlintTypography.axaml`, it is `ReceiverTokens.kt` and
`ReceiverTheme.kt` on the Fire TV receiver, and it is the marketing site. Nothing links those copies.
`tests/Flint.App.Tests/RexDesignSystemTests.cs` pins the desktop values as literals for exactly that
reason, and its remarks block says why: drifting one of them would silently break the family
resemblance across three codebases, which no compiler would catch.

The drift is not hypothetical. `ReceiverTheme.kt` carries `Muted = Color(0xFFA3ABA0)` while the
desktop carries `#858D83`. That divergence is deliberate — the television value is lifted so
secondary text survives a room's distance — but until this record it lived only in a code comment on
one side of the repository, and a fourth copy of the palette written by hand inside `:mobile` would
make the count four with no statement of which one is right for a phone. A new module is the moment
that becomes cheap to prevent and expensive to fix later.

The receiver's system is also shaped for a remote control, not a thumb. `tvFocus` animates a 1.06
lift and a 3dp Signal halo on focus change; `tvClickable` wraps it for buttons. Neither affordance
means anything under a finger, because a touch surface has no persistent focus to show.
`ReceiverOverscan.SAFE_FRACTION` reserves the outer 5% of every edge because Amazon asks for it on a
television; on a phone that same margin is simply a wasted band, while the real hazard — a cutout, a
gesture bar, a status bar — is a different shape entirely. And the receiver draws its text through
`androidx.tv.material3`, a library whose focus and container semantics exist for ten-foot UI.

## Decision

- **`:design` owns the Compose expression of the REX system**: `FlintColors`, `Tone`, `FlintType`,
  `FlintSpace`, `FlintShapes`, `FlintTheme` and the shared components in `Components.kt`. `:mobile`
  consumes it and declares none of its own.
- `:mobile` contains **no colour literal, no font size, no letter spacing and no corner radius**. A
  dimension it does need is taken from `FlintSpace`, whose ramp is 1/4/8/12/16/24/32/48 with a 28dp
  page margin, 16dp card padding, 12dp card spacing and a 48dp touch target.
- **A status is painted by `Tone`, never by a colour.** Components take `Tone.Signal`, `Tone.Live`,
  `Tone.Neutral` or `Tone.Line`, so there is no call site that can paint an unavailable capability
  the accent. The mapping from a capability verdict to a tone stays in `:castcore`, where it is
  tested without a renderer.
- **Two divergences from the television are deliberate and recorded.** `Muted` is the desktop's
  `#858D83` rather than the receiver's `#A3ABA0`, because a phone is held at near-field distance and
  the lifted value reads there as washed out rather than as secondary. The type scale is the
  desktop's — 28/32, 18/22, 15/19, 13/19, 12/17, 9 with 1.4 tracking, and a 15 bold tabular Readout —
  rather than the television's ten-foot scale, on the same reasoning.
- **Three things are deliberately not carried over.** `ReceiverOverscan` does not come with the
  tokens; the phone's equivalent is `WindowInsets.safeDrawing`, which describes the actual obstacles.
  `androidx.tv.material3` is not a `:mobile` dependency, and neither is Material generally: every
  component is built on foundation primitives so a library update cannot restyle an instrument.
  `tvFocus`/`tvClickable` are replaced by `Modifier.flintClickable`, whose pressed and hovered
  opacities are the desktop's 0.72 and 0.88 from `FlintControls.axaml` rather than a new pair
  invented for this surface, and which drops `indication` because Material's ripple is invisible
  against this palette.
- **Two genuine gaps are filled rather than copied.** Touch state has no precedent in either existing
  product, so `flintClickable` supplies it and still honours focus, because a phone may have a
  keyboard attached and a switch-access user has nothing else to go on. Reduced motion has no
  precedent either: nothing in this repository reads `Settings.Global.ANIMATOR_DURATION_SCALE` today,
  the Windows shell has effectively no motion and a television is not where people turn animation
  off. The phone will have more motion than either, so `FlintTheme` reads that setting once, defends
  against a settings provider that throws, and publishes `LocalReducedMotion` for every animation to
  gate on. Opacity rather than scale is used for press state precisely so the affordance survives
  when motion is off.
- **`:receiver` does not consume `:design` in this change.** Switching it touches 36 UI files whose
  rendered text is pinned by 15 approved Robolectric snapshots under
  `receiver/src/test/snapshots/approved/`, and those images cannot be regenerated without an Android
  SDK. That migration is its own slice and its own pull request. `:design` is held at the receiver's
  `minSdk` floor of 25 rather than the phone's 26 so that the module is never itself the reason the
  television app cannot adopt it.

## Consequences

### Positive

- A token has one definition per platform and two executable pins, so drift fails a test rather than
  arriving as a screenshot somebody notices months later.
- The phone cannot paint a capability the accent by accident, because components take a tone.
- Divergence is now a documented decision with a reason attached, rather than an accident of which
  file an implementer happened to copy.
- Accessibility improves for both future consumers: the receiver inherits touch states and a
  reduced-motion signal it never had when it migrates.

### Trade-offs

- Two palettes remain live until the receiver migrates, and `Muted` genuinely differs between them.
  Anyone comparing a phone screenshot with a television screenshot will see it.
- `:design` carries a Compose dependency that `:castcore` deliberately does not, so design work
  cannot move into the pure-JVM module.
- Building on foundation primitives means Flint writes and tests its own text field, button and
  dialog behaviour instead of inheriting Material's.
- The screenshot suite's approved images and the emulator tests for reduced motion are written but
  have not been run.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Declare the tokens inside `:mobile` and copy the values across. | It makes a fourth uncoordinated copy of a palette that has already drifted once, with no test on the new copy. The desktop test's own remarks name this failure: nothing fails to build, the products simply stop looking like one product. |
| Extract `:design` out of `:receiver` and migrate the receiver in the same change. | The migration alters rendered text in 36 UI files pinned by 15 approved snapshots, and those images cannot be regenerated without an Android SDK. A migration whose evidence cannot be refreshed is not reviewable, so it is a separate slice. |
| Build `:mobile` on `androidx.compose.material3` and map the palette into a `ColorScheme`. | A `ColorScheme` has no slot for a status tone, so honesty would be enforced by convention at every call site instead of by the type. The ripple is invisible against this palette, and a Material version bump would restyle an instrument through a dependency update. |
| Use `androidx.tv:tv-material` on the phone, as the receiver does. | Its focus and container semantics are built for a D-pad; it supplies no touch state, and adopting it would carry ten-foot layout assumptions onto a handset held at arm's length. |
| Publish `:design` as a versioned artifact consumed from a repository. | A token change would then need two pull requests and a version bump to reach one product, and the two pins could disagree for as long as the bump took. In-repo modules let the token, the test and the consumer change atomically. |
| Match the receiver exactly, taking `Muted = #A3ABA0` for perfect parity. | The television value is lifted for legibility across a room. At phone distance it reads as washed-out primary text rather than as secondary, which destroys the hierarchy the token exists to create. |

## Invariants and validation

- `design/src/test/kotlin/com/rextechnologies/flint/design/FlintTokenTest.kt` asserts every palette
  value, every type size and line height, the 1.4 label tracking, the radii and the spacing ramp as
  **literals**, mirroring `RexDesignSystemTests.cs` rather than comparing one copy against another. It
  asserts explicitly that `FlintColors.Muted` is `#858D83` and is not `#A3ABA0`, so restoring the
  television value fails the build and reopens this record instead of passing quietly.
- A **source guard** over `:mobile` fails on a `Color(0x…)` literal, a `TextStyle` declaration, an
  `sp` literal, and any `androidx.tv` or `androidx.compose.material` import, so the single source of
  truth is enforced rather than remembered.
- A **Robolectric screenshot suite** in `:design` renders each component in each tone at phone
  density and holds it to approved images, using the harness pattern already proven by
  `receiver/src/test/kotlin/com/rextechnologies/flint/receiver/snapshot/ReceiverSnapshotTest.kt`. It
  needs no device or network. The approved images do not exist yet; the suite is planned, and an
  unapproved run must fail rather than write its own baseline.
- A **reduced-motion test** proves `LocalReducedMotion` is false by default, true when
  `ANIMATOR_DURATION_SCALE` is zero, and false rather than fatal when the settings provider throws.
- `:design` builds with `allWarningsAsErrors` and a ktlint check wired into `check`, and the
  component tests render real themed Compose rather than reading properties back.

## Revisit criteria

Revisit when the receiver migration slice lands: at that point the carve-out is discharged, and the
`Muted` divergence becomes a per-product override that must be stated in one place rather than in two
comments. Revisit if Inter is bundled into `design/src/main/res/font/`, because the metrics the
current tests pin were chosen against the platform sans-serif. Revisit on measured legibility
evidence — a contrast measurement or a genuine accessibility finding — not on an opinion that a value
looks better. A light variant, an off-palette colour, or a per-screen token override would each need
a superseding record rather than an exception.

## References

- [Project constraints](../../../AGENTS.md)
- [Flint Mobile implementation slices](../README.md)
- [`FlintColors` and `Tone`](../../../design/src/main/kotlin/com/rextechnologies/flint/design/FlintColors.kt)
- [`FlintType` and `FlintSpace`](../../../design/src/main/kotlin/com/rextechnologies/flint/design/FlintType.kt)
- [`FlintTheme` and `LocalReducedMotion`](../../../design/src/main/kotlin/com/rextechnologies/flint/design/FlintTheme.kt)
- [`Modifier.flintClickable`](../../../design/src/main/kotlin/com/rextechnologies/flint/design/Interaction.kt)
- [Shared components](../../../design/src/main/kotlin/com/rextechnologies/flint/design/Components.kt)
- [Kotlin token test](../../../design/src/test/kotlin/com/rextechnologies/flint/design/FlintTokenTest.kt)
- [Desktop token test](../../../tests/Flint.App.Tests/RexDesignSystemTests.cs)
- [Desktop pressed and hovered opacities](../../../src/Flint.App/Theme/FlintControls.axaml)
- [`ReceiverTokens` — `tvFocus`, `tvClickable`, `ReceiverOverscan`](../../../receiver/src/main/kotlin/com/rextechnologies/flint/receiver/ui/ReceiverTokens.kt)
- [The receiver's lifted `Muted`](../../../receiver/src/main/kotlin/com/rextechnologies/flint/receiver/ui/ReceiverTheme.kt)
- [Receiver snapshot harness](../../../receiver/src/test/kotlin/com/rextechnologies/flint/receiver/snapshot/ReceiverSnapshotTest.kt)
- [`:design` module build](../../../design/build.gradle.kts)
