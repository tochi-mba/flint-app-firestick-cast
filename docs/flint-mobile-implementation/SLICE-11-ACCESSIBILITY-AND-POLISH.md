# Slice 11 — accessibility and polish

**Governing ADR:** [ADR-0030](adrs/ADR-0030-ONE-SOURCE-OF-TRUTH-FOR-THE-REX-TOKEN-SET.md).

## Outcome

TalkBack reads every screen in an order that makes sense, the layout survives a large font scale,
nothing animates on a phone that asked for less motion, and right-to-left is laid out rather than
mirrored by accident.

## Entry criteria

- Slices 01 to 10 have produced every screen this slice has to check.

## User-visible vertical behaviour

1. TalkBack announces a verdict changing without the reader having to go looking for it.
2. At the largest font scale, every card still shows its whole reason and every control is still
   reachable.
3. With animations switched off in accessibility settings, nothing moves.
4. In an RTL locale the layout mirrors and the numbers do not.

## Scope

### In scope

- Live regions on the states that change under the reader: the verdict stack, the live strip and the
  notice.
- Content descriptions where a control's visible text is not its meaning — the tab glyphs in
  particular, which are letterforms rather than words.
- Decorative elements hidden from the reader rather than announced as unlabelled images. `StatusDot`
  always sits beside text that says the same thing, so it is cleared.
- Every animation gated on `Settings.Global.ANIMATOR_DURATION_SCALE`, read once through the theme.
- Contrast measured and recorded for every token pair the UI actually uses.
- `layoutDirection` handled by using start and end rather than left and right everywhere.

### Explicitly out of scope

- A separate high-contrast theme. The palette is a fixed system, and the honest move if a pair fails
  is to change where it is used rather than to ship a second product.

## Why reduced motion is a gap rather than a copy

Nothing in this repository honours it today. The Windows shell has essentially no motion, and a
television is not where people turn animation off. A phone is, and the phone app has more motion than
either — so this is filled rather than ported, and it is read from the system setting rather than
from a preference of the app's own, because the person already told the system once.

## Touch targets

48dp, everywhere, with no exceptions. `FlintSpace.TouchTarget` is the number and
`SignalButton`/`OutlineAction` carry it as a minimum height rather than as a padding sum, so a short
label cannot shrink a control below it.

## Test-first implementation order

1. A semantics test per screen, asserting the reading order and the live regions.
2. A screenshot suite at the largest font scale, approved separately from the default one.
3. A reduced-motion test asserting no animation runs.
4. An RTL screenshot suite.

## Full-suite checkpoint

The default suite, with the accessibility screenshot suites added to it.

## Exit evidence

- Contrast numbers recorded for every token pair in use, as numbers rather than as a claim.
- Approved screenshots at the default and the largest font scale, and in an RTL locale.
- A TalkBack pass on a real phone, recorded.

## Rollback and stop conditions

Every change here is additive to existing screens. Stop if a screen cannot be made to work at the
largest font scale without cutting its reason short: the reason is the product, and a truncated
explanation is worse than a taller card.
