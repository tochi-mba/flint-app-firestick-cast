# Slice 01 — truthful capability verdict

**Governing ADRs:** [ADR-0024](adrs/ADR-0024-PHONE-BINDS-EVERY-SOCKET-TO-THE-SELECTED-INTERFACE.md),
[ADR-0025](adrs/ADR-0025-SECOND-SCREEN-IS-APP-OWNED-PRESENTATION-CONTENT.md), and
[ADR-0026](adrs/ADR-0026-FLINT-MOBILE-CONSUMES-THE-PROTOCOL-IN-REPO.md).

## Outcome

The phone says plainly what it can and cannot do on this network with this television, and nothing
else. There is no cast, no discovery and no session in this slice. What there is, is the one thing
every later slice is judged against: a verdict per mode that is traceable to evidence, and a
vocabulary that cannot be used to overstate.

This is first because the alternative is the ordinary way a casting app goes wrong. A button appears,
somebody presses it, and the failure arrives several seconds later wearing a stack trace's clothes.
Everything after this slice is gated on a verdict this slice produces.

## Entry criteria

- `:protocol` builds and its tests pass, unchanged.
- Nothing else. This slice deliberately has no dependency on the network, the platform, or the
  Android SDK.

## User-visible vertical behaviour

1. The Cast tab shows which end of the local network this phone is on, in one sentence: it is the
   access point, it is a client of somebody else's network, or there is no local network at all.
2. Three cards, one per mode, each carrying a status word, a reason, and — when there is something to
   do — a block saying what.
3. A mode that has not been probed reads Blocked with a remedy that runs the probe. It never reads
   Available, and it never reads Not possible.
4. Nothing is startable. No control in this slice does anything, because nothing behind one exists.

## Scope

### In scope

- `CastMode`, `ModeStatus` and `ModeVerdict` ported to Kotlin, with the four honesty mechanisms
  intact: only Available is painted Signal; `isOfferable` is the single gate on enabled-ness; four
  distinct words; and `remedy == null` wherever no action would change the answer.
- `ModePresentation`, carrying `isComingSoon` and `hasRemedy` as two independent booleans rather than
  one branch. They are exclusive only because the assessor never attaches a remedy to an unfinished
  verdict, and collapsing them throws away the ability to catch the day one does.
- `LocalNetwork` and `LocalNetworkAssessor`: three verdicts derived from an interface snapshot, with
  the client case chosen by subnet shape rather than by interface name.
- `MobileCapabilityAssessor`, pure, with the second screen judged independently of the mirror.
- `ProbeOutcome`, so "not asked yet" is a value rather than a missing one.
- The decision that this logic lives in `:castcore` rather than in a source set inside `:mobile`.

### Explicitly out of scope

- Discovery of any kind. A television is an argument to the assessor in this slice.
- Any socket. Any thread. Any Android class.
- Any control that starts anything.

## Planned files and boundaries

`castcore/src/main/kotlin/com/rextechnologies/flint/castcore/capability/` — `Modes.kt`,
`Evidence.kt`, `LocalNetwork.kt`, `MobileCapabilityAssessor.kt`. Nothing in the package imports
`android.*`, and nothing performs I/O, reads a clock, or consults ambient state. The assessor takes
its evidence as arguments and returns verdicts; that is the whole boundary.

## Test-first implementation order

1. `ModeVerdict`'s four `require` guards, before any verdict exists to break them.
2. `StatusWord` and `ModePresentation.toneOf`, asserting that Blocked and Impossible share the failure
   tone and that Coming soon does not — live is the failure tone, and a planned feature is not a
   failure.
3. `LocalNetworkAssessor` across every tether family, the client election, and the cases that must
   produce nothing: a down interface, a loopback, and a carrier-style address that is not site-local.
4. The assessor, one gate at a time, ending with the matrix.

### Required verdict matrix

Every network, every platform, every ADB state, every probe outcome and every measured path, crossed.
Two invariants hold over all of it: every Blocked verdict carries a remedy, and no Impossible or
Coming-soon verdict does. A blocker somebody cannot act on should have been an impossibility instead.

## Full-suite checkpoint

`./gradlew :protocol:test :castcore:check`. The coverage gate is 95% line and 85% branch on the
bundle, the same numbers `:protocol` carries.

## Exit evidence

- The matrix test, passing, with its assertion count above a thousand verdicts.
- A copy sweep asserting no verdict makes a confidentiality claim, raises its voice, or outgrows the
  card it has to fit in.
- The divergence from Windows asserted directly: a phone with no screen-capture service is Impossible
  for mirror and Available for second screen.

## Rollback and stop conditions

The module is additive and nothing depends on it yet, so rollback is deleting it. Stop if the
assessor needs a fourth status: four words is the whole design, and a fifth would mean the
distinction between "go and do something" and "stop trying" has been lost.
