# Wire Protocol

Flint speaks the REX wire protocol, the same framing REX Cast defines in its `protocol` module.
One protocol across the family means the host, the phone app and the receiver can never disagree
about what a byte means, and a receiver written for one can be reused by the other.

## Framing

Every frame is a four-byte big-endian body length followed by an eight-byte envelope and a payload.

```
+--------+--------+--------+--------+
|         body length (u32)         |
+--------+--------+--------+--------+
|   magic (u16)   | version (u16)   |
+--------+--------+--------+--------+
|  type (u16)     |  flags (u16)    |
+--------+--------+--------+--------+
|            payload ...            |
```

* **magic** is `0x5243`, the ASCII bytes `RC`. A frame whose magic does not match is rejected
  before anything else is read, which is what stops a port scan meeting an unrelated service and
  being interpreted as a receiver.
* **version** is the payload version, not the framing version. Framing is fixed.
* **flags** is reserved and echoed. A receiver must preserve flags it does not understand.
* **body length** is capped at 16 MiB. A peer that declares more is dropped rather than allocated
  for.

## Message types

| Id | Name | Direction | Purpose |
|---|---|---|---|
| 1 | `HELLO` | both | Version range, device name, codec capabilities, screen geometry |
| 2 | `AUTH` | host to receiver | Pairing code, session token, or public-key proof |
| 3 | `VIDEO_CONFIG` | host to receiver | Codec, dimensions, codec-specific data (SPS/PPS/VPS) |
| 4 | `VIDEO` | host to receiver | One access unit, with presentation time and a key-frame flag |
| 5 | `AUDIO_CONFIG` | host to receiver | Codec, sample rate, channel count |
| 6 | `AUDIO` | host to receiver | One audio packet |
| 7 | `CONTROL` | receiver to host | Transport, pointer, key, text and volume events |
| 8 | `STATS` | receiver to host | Queue depth, decode latency, round-trip time, dropped frames |
| 9 | `BYE` | both | Ordered shutdown with a reason |
| 10 | `MEDIA_COMMAND` | host to receiver | Play a file: fetched from a URL, or the file most recently pushed on this connection |
| 11 | `SURFACE` | host to receiver | Which surface the receiver renders: idle, player, mirror, presentation, or (v2 TLS only) browser |
| 12 | `PLAYBACK_STATE` | receiver to host | Playback position, duration, state and errors |
| 13 | `MEDIA_DATA` | host to receiver | One ordered chunk of a media file, pushed over the control connection |
| 14 | `BROWSER_CAPABILITY` | receiver to host | Secure-session browser capability snapshot after TLS + pairing |
| 15 | `BROWSER_COMMAND` | host to receiver | Ordered Open/Navigate/Back/Forward/Reload/Stop/Close/preview/clear |
| 16 | `BROWSER_INPUT` | host to receiver | Portable pointer, scroll, semantic key, or explicit text |
| 17 | `BROWSER_STATE` | receiver to host | Bounded safe URL/title/progress/history/viewport/error state |
| 18 | `BROWSER_PREVIEW` | receiver to host | Newest-only bounded JPEG preview frame |
| 19 | `BROWSER_DIALOG` | receiver to host | Native alert/confirm/prompt/before-unload observation |
| 20 | `BROWSER_DIALOG_REPLY` | host to receiver | Explicit answer to the one active native dialog |
| 21 | `BROWSER_TAB_COMMAND` | host to receiver | New/Close/Select/Move/Duplicate for one television tab |
| 22 | `BROWSER_TAB_STATE` | receiver to host | Full replacement snapshot of every open tab and which is active |
| 23 | `BROWSER_VIEW_COMMAND` | host to receiver | Zoom, user agent, dark page, interaction mode, fullscreen, find, search engine |
| 24 | `BROWSER_VIEW_STATE` | receiver to host | Reflected view settings, find result, and whether a page field has focus |
| 25 | `BROWSER_FAVICON` | receiver to host | One bounded PNG favicon, referenced by id from tabs and library rows |
| 26 | `BROWSER_LIBRARY_COMMAND` | host to receiver | Add/remove a bookmark, clear history or bookmarks, request a snapshot |
| 27 | `BROWSER_LIBRARY_STATE` | receiver to host | Bounded receiver-owned bookmarks and recent history |

#### Why these are separate ids rather than new fields

Every browser decoder ends by asserting there are no trailing bytes, so appending a field to an
existing message is a breaking change for any peer that has not been updated: the extra bytes read
as corruption and the whole frame is rejected. Unknown *type ids*, by contrast, are already carried
opaquely and relayed intact. Extending the family is therefore always additive.

That tolerance is one-directional. A receiver that has never heard of id 21 will not dispatch it,
so a host must not send one speculatively — the cockpit families are enabled only after the
receiver has volunteered a snapshot of its own.

### Browser transport boundary (protocol v2)

Message IDs 14–27 and `SURFACE` mode `Browser` (5) are **forbidden on the ordinary plaintext
CastSession / ReceiverServer socket**. They are legal only inside a separately negotiated,
certificate-pinned TLS BrowserSession after HELLO + AUTH succeed. A peer that observes any of
those values on the legacy channel must close with `BYE` rather than dispatching them.

Browser payloads require payload version **2**. Encoding or decoding them at version 1 is a wire
error. Legacy media and mirror traffic remains legal at version 1; a v2 session may still carry
those messages, but browser values never silently inherit a plaintext fallback.

### Why media is pushed rather than fetched

`MEDIA_DATA` exists because of a Fire OS behaviour that is not documented anywhere by Amazon and
looks exactly like a bug in Flint until it is understood.

**Fire OS silently drops outbound connections the receiver initiates to private LAN addresses.** Not
refused, not reset — dropped, with no error the application can see. The same TCP connection works
perfectly in the other direction, which is how pairing succeeds in the first place, so every
diagnostic points at a working network.

The consequence is that a receiver cannot fetch a file from an HTTP server on the host, which is the
obvious way to build media handoff and the way this project built it first. So the host pushes the
file down the control connection it already owns, in ordered chunks, and `MEDIA_COMMAND` with an
empty URL means "play what I just sent you".

A URL is still honoured. A receiver on a network where outbound fetches work is free to use it and
skip the push entirely; the push is the fallback that works everywhere, not a replacement.

## Version negotiation

Each side sends a `HELLO` carrying the range it supports. The session runs at the highest version
both ranges cover; when the ranges do not overlap, the connection closes with `BYE` and reason
`UNSUPPORTED_VERSION` rather than attempting a downgrade.

## Forward compatibility

A message type a build does not recognise is preserved rather than rejected. It is carried as an
opaque payload and relayed intact, so an older receiver stays usable against a newer host for
everything it does understand.

This is why `flags` must be echoed and why unknown types are not an error. A protocol that fails
closed on anything unfamiliar cannot be extended without a flag day.

## Transport split

Video and audio move over UDP. A retransmission that arrives after its frame's display deadline is
worse than the loss it repairs, so loss is covered by forward error correction, and a gap beyond
FEC's reach triggers an IDR request rather than a stall.

Control and stats move over a reliable channel. The two must not share a socket's fate: losing a
key-up event because a video packet was dropped would leave a key held down.

## Authorization is not encryption

On the ordinary CastSession, `AUTH` carries a cryptographically random per-session token. The token
proves a client is allowed to name a resource. It does not encrypt anything.

The browser path is different: its `AUTH` happens only after a dedicated TLS 1.2 (or newer) session
has completed certificate pinning. That TLS channel is the encryption boundary for browser
commands, text, dialog replies, and preview bytes. Ordinary cast/media traffic is still not claimed
as encrypted.

Until a given channel has an authenticated encrypted transport that has been verified, no document,
log line or interface string in this project may describe *that* channel's traffic as private,
confidential or encrypted.

## Golden vectors

`protocol/golden/` holds encoded frames committed as bytes. Rust, C#, and Kotlin each assert they
produce and parse them identically. A wire change that does not regenerate these fails the build,
which is what stops three implementations drifting apart in three languages.

The corpus covers every message type including media (10–13) and browser (14–27), both `Option`
states, empty and non-empty collections, the edges of each integer width, multi-byte UTF-8, and the
forward-compatibility path. Legacy and media cases remain at payload version 1; browser cases and
the Browser surface are committed at version 2. Completeness tests on each language fail when a
vector has no case or a case has no vector.

A round-trip test proves only that an implementation agrees with itself. Agreement with committed
bytes is what actually tests interoperability, which is why C# asserts against vectors Rust
generated rather than producing its own.

### Regenerating

```text
cd apps/windows/engine
cargo test --test golden -- --ignored regenerate
```

Regenerating is deliberate, and gated behind `--ignored` so a routine test run can never quietly
rewrite the contract. A change to these files is a protocol change, and their diff is the review
surface for it.


### Protocol 4: workspace dividers and presentation

Protocol 4 adds TLS-browser-only IDs 35 (`BROWSER_WORKSPACE_RESIZE`) and 36
(`BROWSER_WORKSPACE_GEOMETRY`). Existing payloads and their golden vectors remain unchanged.
Peers reject these new payloads when their frame declares a version below 4. A receiver publishes
geometry only when the session negotiated version 4; the host enables controls only after receiving
geometry for the same epoch/revision as its workspace state.

All integers below use the existing big-endian encoding, without padding:

| ID | Payload |
| --- | --- |
| 35 | i64 epoch, i64 commandId, i64 expectedRevision, u16 column, u16 row, u8 mode |
| 36 | i64 epoch, i64 revision, u16 column, u16 row, u8 mode |

Epochs, command IDs and revisions must be positive. Column and row fractions are ten-thousandths
bounded to 1500..8500. Command mode 0 preserves presentation, 1 selects Tabs, 2 selects Workspace.
The receiver emits state mode 1 or 2; decoders also accept 0 for the initial geometry-only vector.
Unknown modes, invalid fractions, truncated payloads and trailing bytes are rejected.

Resize uses the existing authenticated command watermark. Future expected revisions trigger an
updated snapshot; older positive revisions are accepted, as page progress may advance revisions
during a drag. Pane identities are unchanged. Exclusive presentation rejects geometry changes.
Switching presentation retains the tab model and workspace model while their inactive UI hosts
release renderers. Cross-language canonical cases are `browser-workspace-resize` and
`browser-workspace-geometry`.

Workspace command action 15 (`MOVE_PANE`) is additive on the existing command payload: pane ID plus
target slot 0..3. Unknown actions remain rejected. The canonical case is `browser-workspace-command-move-pane`.
