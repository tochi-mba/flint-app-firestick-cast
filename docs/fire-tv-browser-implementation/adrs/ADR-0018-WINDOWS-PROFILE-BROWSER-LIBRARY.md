# ADR-0018 — Windows-current-user browser library with a session-only TV projection

- **Status:** Accepted
- **Implementation:** In progress
- **Date:** 2026-09-06
- **Scope:** Fire TV browser experiment
- **Deciders:** Flint engineering
- **Owners:** Windows cockpit, receiver browser UX, and protocol workstreams
- **Related:** ADR-0001, ADR-0003, ADR-0005, ADR-0010, ADR-0015, ADR-0017
- **Supersedes:** [ADR-0016](ADR-0016-RECEIVER-OWNED-BOOKMARKS-AND-HISTORY.md)
- **Superseded by:** None
- **Amends:** ADR-0017's direction and discovery rules for message ids 26–27

## Context

ADR-0016 put bookmarks and persistent visit history beside the WebView on the receiver. That made
the television independently useful, but it put one person's browsing record on a shared appliance.
A Fire TV may be used by a household, left signed in, transferred, or inspected through developer
access. App-private Android storage is not a user identity boundary and is not encryption.

The Windows application already runs inside a Windows user profile. That is the better boundary for
the person's durable bookmarks, recent visits, and saved favicons. This does not move the browser to
Windows: Android still owns WebView, page network traffic, cookies, site storage, rendering, input,
and the canonical TV display under ADR-0001. Only the browser-library metadata changes owner.

The original wire topology followed the original storage decision: id 26 carried host-to-receiver
library commands and id 27 carried receiver-to-host state. With Windows authoritative, those flows
must reverse. The feature is not release-verified and the receiver never completed an end-to-end id
26/27 dispatch path, so this is a coordinated pre-release semantic correction rather than a claim of
backward-compatible behaviour.

## Decision

### Ownership and persistence

- Durable bookmarks, recent-visit metadata, and retained favicon bytes belong to the **current
  Windows user profile**. “Profile” means the operating-system account running Flint; this ADR does
  not invent a second Flint account/profile system.
- Windows stores the library under its existing per-user Flint local-application-data directory.
  The persisted representation is versioned, strictly bounded, atomically replaced, and protected
  with Windows current-user data protection. It contains no cookies, page bodies, form values,
  typed text, credentials, headers, DOM, or WebView state.
- The durable store keeps at most 200 bookmarks and 500 canonical-URL-deduplicated recent visits,
  newest first. A receiver projection is smaller where the wire is smaller: id 27 uses one-byte
  counts and therefore carries at most 200 bookmarks and the newest 255 history rows.
- A loaded-page observation is accepted into history only from current, monotonically ordered
  receiver state. Windows timestamps it on receipt rather than trusting the receiver wall clock.
  Repeated state for the same navigation does not create repeated visits.
- A malformed, oversized, duplicate-key, future-version, or undecryptable file fails closed to an
  explicit unavailable/recovery state. It must not crash the app, silently parse a partial library,
  or be uploaded to a receiver.

### Receiver projection and offline behaviour

- The receiver holds only an in-memory, full-replacement projection of the authenticated Windows
  profile. It never writes the projection, bookmarks, recent visits, or host favicon bytes to disk.
- The projection is scoped to the authenticated browser session/epoch and one controller. It is
  cleared when that controller disconnects, the epoch ends, another controller takes ownership, the
  receiver process stops, or the browser is closed. Reconnect requires a fresh snapshot.
- Without a compatible verified Windows controller, the TV still supports HTTPS navigation,
  search, tabs, and WebView Back/Forward history. Persistent bookmarks and recent visits are shown
  as unavailable, not as an empty successful library and not as data belonging to a previous user.
- Receiver adoption is atomic. It validates the epoch, increasing host revision, collection bounds,
  entry kind, UTF-8 byte bounds, canonical HTTPS URL policy, duplicate URLs, timestamps, and favicon
  references before replacing the visible projection. One bad row rejects the candidate and keeps
  the previous valid projection.

### Secure synchronization protocol

- Id 26 remains `BROWSER_LIBRARY_COMMAND`, but its direction is **receiver to host**. Its existing
  actions retain their payload shapes: native TV UI may request AddBookmark, RemoveBookmark,
  ClearHistory, or ClearBookmarks, and RequestSnapshot asks the host to publish its current
  projection. The command id is receiver-owned and monotonic within the epoch. Web content cannot
  originate these operations.
- Id 27 remains `BROWSER_LIBRARY_STATE`, but its direction is **host to receiver**. It is the
  authoritative full replacement of the TV projection for the matching epoch; its revision is
  host-owned and monotonic. The receiver never echoes it as durable truth.
- The receiver discovers support by sending id 26 `RequestSnapshot` only after TLS pinning, pairing,
  authentication, and creation of a positive browser epoch. A host must not send id 27 until it has
  received that request. This preserves the important newer-host/older-receiver safety property: a
  new host never probes an old receiver with a type it may not dispatch.
- History need not add a new wire action. Windows derives visits from current `BROWSER_STATE` and
  `BROWSER_TAB_STATE` load transitions; id 26 is reserved for deliberate native library mutations
  and synchronization requests.
- Both ids remain protocol-v2, BrowserSession-TLS-only values and remain forbidden on the ordinary
  cast channel. The browser type range remains 14–27.

### Favicons

- Id 25 remains receiver-to-host in this decision. A receiver publishes each accepted bounded PNG
  before or with state that references it; Windows validates it and may retain it under the current
  user with the library.
- Favicon ids are not durable receiver object handles. A host-to-receiver id 27 snapshot may use a
  non-zero id only when that exact icon has been published by the same receiver in the current
  epoch and is known to remain in its bounded cache. Otherwise it sends zero and the TV renders its
  existing letter-tile fallback. Sending saved PNGs back to the TV would require an explicit future
  bidirectional-favicon decision and transport tests; this ADR does not silently change id 25.

### Clearing and migration

- **Clear TV website data** clears receiver WebView cookies, DOM storage, cache, form/autofill data,
  WebView navigation state, and the in-memory library/icon projection after TV-local confirmation.
  It does not delete the Windows profile library, Windows trust pins, or receiver TLS identity.
- **Clear history** and **Clear bookmarks** are separate profile-library actions with explicit
  Windows UI wording and destructive confirmation. After a successful local mutation, Windows
  advances the revision and publishes a replacement id 27 snapshot to a connected compatible TV.
- Existing receiver files (`browser-library.json` and its `.tmp`/`.bak` recovery files) are deleted
  during migration. They are not silently assigned to or imported by the next Windows user, because
  the receiver cannot prove who created that shared-device data.

## Wire-compatibility classification

Direction is not encoded in a REX frame. Reversing ids 26 and 27 while retaining their field order,
types, enum values, limits, and ids must leave every existing golden `.bin` byte-identical. The
existing vectors should not be regenerated merely to manufacture a diff; their Rust, C#, and Kotlin
case descriptions and direction comments must instead be updated.

Golden vectors cannot prove sender authorization. Dedicated loopback/session tests must prove that
the receiver accepts id 27 and emits id 26, the host accepts id 26 and emits id 27, wrong-direction
known messages fail according to the documented session policy, and neither value reaches the
ordinary plaintext listener. If a payload shape or enum value changes later, that is a normal wire
change and requires deliberate Rust regeneration plus matching C# and Kotlin manifests.

The direction reversal is semantically incompatible with the pre-ADR-0018 experimental cockpit.
New-host/old-receiver operation remains safe because the host waits for RequestSnapshot; the library
panel stays unavailable when it never arrives. Old-host/new-receiver library interoperability is not
claimed. A public/release-verified protocol would require new ids or a versioned capability instead
of reusing published semantics.

## Consequences

### Positive

- Browsing metadata follows the person signed in to Windows rather than everyone sharing the TV.
- The television retains no durable bookmark/history file for a later household member or device
  owner to inherit.
- Windows can present the same bounded library across compatible receivers without moving page
  rendering, traffic, cookies, or credentials off the TV.
- Full-replacement snapshots make disconnect/reconnect and missed-update recovery deterministic.

### Trade-offs

- Persistent bookmarks/history are unavailable when no compatible Windows controller is present.
- Windows now stores sensitive URL/title/icon metadata and must protect, bound, migrate, clear, and
  test it as such.
- Direction is a transport invariant invisible to golden bytes, so the session tests become part of
  the compatibility contract.
- The old experimental id 26/27 semantics require coordinated host/receiver updates; only the
  explicitly described asymmetric fallback is supported.
- Saved favicons fall back to letter tiles on a fresh receiver session until a separate safe
  host-to-TV favicon transfer is designed.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Keep ADR-0016's receiver store | A shared television is not a personal browsing-history boundary. |
| Copy the library to both devices | Creates two authorities, merge/conflict rules, and two durable privacy surfaces. |
| Import the legacy TV file into the next host | The receiver cannot prove that the connecting Windows user owns the existing household data. |
| Make the TV projection survive disconnect/restart | Recreates the shared-device persistence this decision removes. |
| Send id 27 before capability discovery | A newer host could disconnect an older receiver by probing with an unsupported type. |
| Allocate new ids immediately | Preferred after release, but unnecessary for this unreleased, incomplete family when payload bytes remain unchanged and the compatibility limit is stated honestly. |
| Treat favicon ids as permanent receiver handles | The receiver cache is bounded and process-local; a stored id can outlive its bytes. |

## Invariants and validation

- Windows store tests cover current-user protection, atomic replace, bounds, canonical URL
  deduplication, UTF-8 limits, corruption/future-version handling, revision persistence, concurrent
  mutation, and deletion without logging library contents.
- Host synchronization tests cover stale/duplicate state, one history write per completed
  navigation, background tabs, RequestSnapshot gating, mutation acknowledgement by replacement
  snapshot, disconnect, reconnect, and host/receiver clock disagreement.
- Receiver tests prove snapshots are validated and replaced atomically, never written to disk,
  cleared on every ownership/lifecycle edge, and unavailable without an authenticated host.
- Transport tests enforce the new directions before dispatch and keep ids 14–27 off the ordinary
  channel. Existing id 26/27 golden bytes remain unchanged unless their payload schema changes.
- UI tests and approved snapshots distinguish local profile data from TV website data and never say
  that clearing one clears the other.
- A migration test seeds every legacy receiver file variant and proves it is removed without being
  uploaded or exposed to a connecting profile.

## Revisit criteria

A product requirement for TV-only persistent bookmarks, named Flint profiles, roaming/cloud sync,
multi-controller merge, host-to-TV favicon transfer, or a release-stable compatibility promise
requires another ADR with a privacy model, migration plan, protocol capability design, and full
cross-language/session evidence.

## References

- [Protocol reference](../../PROTOCOL.md)
- [ADR-0001 — TV-resident browser ownership](ADR-0001-TV-RESIDENT-BROWSER-OWNERSHIP.md)
- [ADR-0010 — native dialogs, data clear and recovery](ADR-0010-NATIVE-DIALOGS-DATA-CLEAR-AND-RECOVERY.md)
- [ADR-0016 — superseded receiver-owned library](ADR-0016-RECEIVER-OWNED-BOOKMARKS-AND-HISTORY.md)
- [ADR-0017 — additive cockpit message ids](ADR-0017-ADDITIVE-COCKPIT-MESSAGE-IDS.md)
