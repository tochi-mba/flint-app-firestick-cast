# Slice 04 — discovery on the hotspot

**Governing ADRs:** [ADR-0024](adrs/ADR-0024-PHONE-BINDS-EVERY-SOCKET-TO-THE-SELECTED-INTERFACE.md)
and [ADR-0026](adrs/ADR-0026-FLINT-MOBILE-CONSUMES-THE-PROTOCOL-IN-REPO.md).

## Outcome

The phone finds the television on its own hotspot, and when it cannot, it says which network it was
on and what it tried. Discovery is never the only route: an address can always be typed in.

## Entry criteria

- Slice 01's verdicts exist, so a television found here has somewhere to be reported.
- Slice 03's components exist, so a list of televisions has something to be drawn with.

## User-visible vertical behaviour

1. Pressing Find my TV sweeps the derived subnet and, on an ordinary hotspot, names the television in
   well under a second.
2. The Cast tab lists what answered, with the address, the port and which rung found it.
3. When nothing answers, the page says what it swept and why the next rung did not help, rather than
   showing an empty list.
4. Typing an address in by hand works whether or not any rung did.

## Scope

### In scope

- The ladder, in order, each rung pinned to the address the ladder chose: the plaintext TCP line
  probe across a bounded `Ipv4Subnet.hosts()` sweep; multicast DNS with a `MulticastLock` held for
  its duration; UDP broadcast; SSDP for televisions with no Flint receiver; and manual entry.
- `ReceiverProbe` in `:protocol`, so the line format has one definition that both ends share.
- `ReceiverBroadcastResponder` in `:receiver`, additive and independently failable.
- The round trip, measured by the phone.

### Explicitly out of scope

- Connecting to anything. A television found here is a row in a list.
- Throughput measurement, which needs a receiver willing to sink traffic.

## Why the line probe goes first

Multicast is the thing that fails on a SoftAP link. The plaintext probe is the multicast-free
fallback the receiver was built to answer, and answering one produces no state change and no listener
callback on the receiver side — deliberately, so that a phone sweeping a subnet cannot make the
television flicker between screens.

## Bounds, and why they are bounds

Every probe competes with the cast stream for the same radio, so none of the numbers is "as many as
possible". The sweep is bounded by `SweepBudget.forSubnet`, which refuses to plan a line-probe rung at
all for a subnet larger than 512 usable hosts: sixty thousand sequential connects is not a sweep, it
is a denial of the radio. The connect timeout is short because a host that is going to answer answers
immediately on a local link, and every millisecond spent waiting on one that will not is a
millisecond the radio is not carrying video.

## The round trip is the phone's own measurement

The receiver reports `roundTripTimeUs = 0` in every `STATS` frame because it never measures one.
Passing that through would put a zero on a diagnostics row, which reads as an extraordinarily good
link rather than as no measurement at all. So the phone times its own connect-and-answer, and the row
says where the number came from.

## Test-first implementation order

1. `ReceiverProbe`, both halves, including every malformed response a subnet sweep will actually meet.
2. `SweepBudget` and `DiscoveryLadder`, including the subnet too large to sweep.
3. `ReceiverBroadcastResponder` against a real loopback socket.
4. The Android runner, against a fake receiver on loopback.

## Full-suite checkpoint

`./gradlew :protocol:test :castcore:check :receiver:testDebugUnitTest :mobile:testDebugUnitTest`.

## Exit evidence

- Every rung tested, and the sweep proven bounded and interface-pinned.
- The source guard in `:castcore` passing: no wildcard bind, and no address written down that was not
  derived, with the mDNS and SSDP group addresses the only permitted literals.
- Honest copy for each failure in the ladder, asserted rather than reviewed.

## Rollback and stop conditions

The receiver change is additive and behind its own start-failure path, so reverting the phone side
leaves the television exactly as it was. Stop if a rung needs the process default network bound:
`ConnectivityManager.bindProcessToNetwork` cannot reach a tether interface, and reaching for it is a
sign the interface pinning has been lost somewhere upstream.
