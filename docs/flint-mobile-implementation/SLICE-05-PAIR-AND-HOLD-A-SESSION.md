# Slice 05 — pair and hold a session

**Governing ADRs:** [ADR-0026](adrs/ADR-0026-FLINT-MOBILE-CONSUMES-THE-PROTOCOL-IN-REPO.md) and
[ADR-0029](adrs/ADR-0029-SESSION-TOKENS-ARE-ENCRYPTED-AT-REST-AND-ARE-AUTHORISATION.md).

## Outcome

The phone pairs with the six-digit code the television is showing, holds the connection open, and the
television shows the phone's name. Reconnecting afterwards needs no code.

## Entry criteria

- Slice 04 finds a television and reports its bound port.

## User-visible vertical behaviour

1. Entering the code the television shows connects, and the television's idle screen names the phone.
2. Closing and reopening the app reconnects without asking for a code again.
3. A wrong code says the television did not accept this phone, and says that a code is good for one
   pairing only.
4. A television already casting from another phone says exactly that, in the television's own words.
5. The socket dying says the connection dropped, and never pretends the television said something.

## Scope

### In scope

- `CastSessionMachine`: the phone's half of the handshake as a state machine with no sockets in it,
  wrapping `SenderHandshake` and adding the envelope-version discipline and the failure vocabulary.
- The Android transport that drives it: one socket, one reader, one writer, and no logic.
- `TokenStore`: the granted `SessionToken` encrypted at rest under an AES-256-GCM key generated in
  and never leaving the Android keystore.
- The loopback harness: a fake receiver speaking the real `ReceiverHandshake` and `WireCodec` over a
  real socket, so handshake, auth, surface switch, stats feedback, reconnect, BYE and the second-phone
  refusal are all exercised with no television in the room.

### Explicitly out of scope

- Anything that produces pixels. This slice carries a `SURFACE` message and a `STATS` reply and
  nothing else.

## Two things this slice exists to get right

**Envelope versions.** `HELLO` goes out on the oldest supported envelope even though its payload
advertises the whole range, because a receiver too old to parse a newer envelope has to be able to
read the first exchange in order to say so. Everything after negotiation uses the negotiated version
— never `ProtocolVersion.CURRENT`, which is this build's ceiling and not the agreement. The receiver
applies exactly the same rule in the other direction, and its pre-establishment refusals also ride a
version-1 envelope.

**A BYE can arrive after authentication appeared to succeed.** The receiver admits one phone at a
time and refuses the second only once it has answered that phone's `HELLO` and validated its
credential. A phone that treats any post-authentication `PROTOCOL_ERROR` as a defect will report a
protocol failure for the most ordinary situation there is, which is somebody else already watching.
The refusal is recognised by its detail string — `"This TV is already casting from another phone"` —
because the reason id does not distinguish it.

## Planned files and boundaries

`castcore/session/` holds the machine and the failure copy and performs no I/O. `mobile/net/` holds
the socket. The split is what lets every refusal path be tested without a network, and the loopback
harness is what proves the two halves agree.

## Test-first implementation order

1. The machine against a real `ReceiverHandshake`, including every refusal.
2. The busy case specifically, asserted as `RECEIVER_BUSY` rather than `PROTOCOL`.
3. The token store, round-tripped.
4. The loopback harness end to end.

## Full-suite checkpoint

`./gradlew :protocol:test :castcore:check :mobile:testDebugUnitTest`.

## Exit evidence

- The loopback harness green, with no television involved.
- `HELLO` asserted to carry envelope version 1 and payload maximum 4 at the same time.
- A token persisted, reloaded, and used to reconnect without a code.
- A copy test proving no string anywhere calls the link private, secure or encrypted.

## Rollback and stop conditions

Revert the transport and the machine stays harmless. Stop if the token needs to be readable outside
the keystore for any reason: a token that can be exported is a standing proof of authorisation that
can be lifted, and there is no feature worth that.
