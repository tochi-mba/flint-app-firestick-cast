# Slice 03 — the REX design system on a phone

**Governing ADR:** [ADR-0030](adrs/ADR-0030-ONE-SOURCE-OF-TRUTH-FOR-THE-REX-TOKEN-SET.md).

## Outcome

Every screen is visibly the same product as the Windows shell and the Fire TV receiver. The palette,
the type scale, the spacing rhythm and the components are one system with one source, and a test
fails if any of them drifts.

## Entry criteria

- Slice 02's `:mobile` builds and installs.

## User-visible vertical behaviour

1. The app is near-black with a green cast rather than pure black, its text warm off-white rather
   than pure white, and its one accent used sparingly enough to mean something.
2. Every eyebrow, pill and status word is 9sp, bold, heavily tracked and upper-cased by the component
   rather than at the call site.
3. A card's border tone is its entire status language: Signal means ready, Live means wrong, a
   hairline means the card is carrying no status at all.
4. Pressing anything dims it rather than moving it, and nothing animates at all on a phone that has
   asked for less motion.

## Scope

### In scope

- `:design` as an Android library: `FlintColors`, `FlintType`, `FlintSpace`, `FlintShapes`, `Tone`,
  `FlintTheme`, `flintClickable`, and the components — `InfoCard`, `Pill`, `StatusDot`,
  `SectionLabel`, `PageHeading`, `DiagnosticRow`, `EmptyState`, `AdvisoryBlock`, `SignalButton`,
  `OutlineAction`, `Readout`, `TextEntry` and `FlintText`.
- The Kotlin token test mirroring `tests/Flint.App.Tests/RexDesignSystemTests.cs`.
- The adaptive icon layers, generated from the same 108-unit polygon the Windows tile draws.

### Explicitly out of scope

- **Switching `:receiver` onto `:design`.** It touches 36 UI files whose rendered text is pinned by 15
  approved Robolectric snapshots. Doing it here would put a formatting change and a behaviour change
  in one diff, and the snapshots are the only thing standing between the two.
- `androidx.tv.material3`, `ReceiverOverscan`, and the D-pad halo in `tvFocus`/`tvClickable`. A phone
  has `WindowInsets.safeDrawing` and a thumb.
- Any Material dependency at all.

## Planned files and boundaries

`design/src/main/kotlin/com/rextechnologies/flint/design/`. The module depends on Compose foundation
and ui and nothing else, and its `minSdk` is the receiver's rather than the phone's — 25, not 26 — so
that the Fire TV app can adopt these tokens later without this module being the reason it cannot.

Two deliberate divergences from the television are recorded in the source: `Muted` is the desktop's
`#858D83` rather than the receiver's `#A3ABA0`, because a phone is held at arm's length where the
lifted value reads as washed out; and the type scale is the desktop's rather than the ten-foot one.

Two genuine gaps are filled rather than copied. Touch states come from the desktop's `:pointerover`
and `:pressed` opacities — 0.88 and 0.72 — rather than from a new pair invented here. And reduced
motion is honoured through `Settings.Global.ANIMATOR_DURATION_SCALE`, which nothing in this
repository has ever read: the Windows shell has no motion to speak of, and a television is not where
people turn animation off.

## Test-first implementation order

1. The token test, asserting the palette as literals, before the tokens exist.
2. Each component, with a screenshot approved as it lands.
3. The receiver's 15 approved snapshots re-run, unchanged, as the proof this slice did not touch it.

## Full-suite checkpoint

`./gradlew :design:testDebugUnitTest :receiver:testDebugUnitTest`.

## Exit evidence

- The Kotlin token test green, asserting the same eight colours the C# test asserts.
- A phone screenshot suite, every state approved, using the receiver's own harness settings verbatim:
  `mainClock.autoAdvance = false`, an explicit `advanceTimeBy`, `decorView.draw`, and the 8/255 and
  0.1%-of-pixels tolerances. A missing approved image is a failure, never an automatic approval.
- The receiver's snapshots unmoved.

## Rollback and stop conditions

`:design` is additive; `:mobile` is its only consumer. Stop if a component needs a colour that is not
in the palette — that is a sign the status language has run out of room, and adding a ninth colour is
the wrong answer to it.
