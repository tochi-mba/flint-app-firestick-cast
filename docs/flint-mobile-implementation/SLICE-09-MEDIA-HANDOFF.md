# Slice 09 — media handoff

**Governing ADRs:** [ADR-0026](adrs/ADR-0026-FLINT-MOBILE-CONSUMES-THE-PROTOCOL-IN-REPO.md) and
[ADR-0029](adrs/ADR-0029-SESSION-TOKENS-ARE-ENCRYPTED-AT-REST-AND-ARE-AUTHORISATION.md).

## Outcome

The phone hands a video file to the television, which plays it at its original quality. Nothing is
re-encoded on the way, and no filesystem path ever reaches the wire.

## Entry criteria

- Slice 05 holds a session open, which is the channel the file travels down.

## User-visible vertical behaviour

1. Choosing a video opens the system picker; Flint never browses storage itself.
2. A progress line says how much of the file has reached the television.
3. The television plays it, and the phone drives play, pause, seek and stop.
4. A playback failure is reported in the receiver's own wording rather than in a decoder's.

## Scope

### In scope

- The Storage Access Framework picker, and reading through the `ContentResolver` rather than through
  a path.
- The push path: 512 KiB `MEDIA_DATA` chunks down the control socket, then
  `MEDIA_COMMAND(LOAD, url = "")` — a blank URL, which is the receiver's own convention for "play
  what I just sent you".
- `MediaHandoff` in `:castcore`: the chunk arithmetic, the MIME cap, and the title sanitiser.
- Transport controls over `CONTROL`, and `PLAYBACK_STATE` back.

### Explicitly out of scope

- The phone's own token-gated HTTP server. It exists in `:protocol` and stays optional; see below.

## Why the file is pushed rather than fetched

Handing the receiver a URL and letting it pull is the elegant route, and on some Fire OS builds it
silently does not work: an outbound connection the receiver initiates to a private LAN address is
dropped — not refused, not reset, dropped — while the very same address works perfectly for the
connection the phone initiated. So the push path is the default and the HTTP server is the option.
The UI says this once, in the advisory block, because otherwise somebody watches a local file take
time to start and reasonably wonders why.

## No path on the wire, including the phone's own

A client must never be able to name a filesystem path, and `TokenPath` in `:protocol` already enforces
that in the other direction. This slice enforces it here: a title is stripped to its leaf before it
is sent, because a title is shown on a television in somebody's living room and
`/storage/emulated/0/` in front of it says more about the phone than anybody asked.

## The MIME cap

Kotlin's codec rejects a `mimeType` longer than 255 bytes; C# and Rust allow 512. A MIME type between
the two encodes on Windows and is refused here. `MediaHandoff.mimeTypeOrNull` caps at 255 and returns
`null` rather than truncating, because a truncated MIME type is a different MIME type and the
television would pick a decoder for something the file is not. Reconciling the three implementations
is outstanding cross-language work, recorded in the README.

## Test-first implementation order

1. `MediaHandoff`: chunk arithmetic at the boundaries, the byte-counted MIME cap, and the title
   sanitiser against both path separators.
2. The push path against the loopback harness, asserting the chunks reassemble byte-identically.
3. The blank-URL command, asserted as blank.
4. Playback error copy, using the receiver's own wording.

## Full-suite checkpoint

`./gradlew :protocol:test :castcore:check :mobile:testDebugUnitTest`, plus a real file on a real
television.

## Exit evidence

- A file pushed and reassembled byte-for-byte, verified by hash.
- A test asserting no `MEDIA_COMMAND` this app builds contains a path separator in its title.
- The transport controls driving a real playback.

## Rollback and stop conditions

The Media tab is self-contained; reverting it leaves the session and both screen modes untouched.
Stop if a file would need to be copied into the app's own storage to be sent: that is a second copy
of somebody's video on their phone, and the `ContentResolver` stream is there precisely so it is not
needed.
