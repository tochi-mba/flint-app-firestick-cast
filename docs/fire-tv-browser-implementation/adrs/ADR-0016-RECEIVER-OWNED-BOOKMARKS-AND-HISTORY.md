# ADR-0016 — receiver-owned bookmarks and history

- **Status:** Superseded
- **Implementation:** Implemented (software); receiver persistence scheduled for removal
- **Date:** 2026-09-06
- **Scope:** Fire TV browser experiment
- **Deciders:** Flint engineering
- **Owners:** TV browser UX workstream
- **Related:** ADR-0001, ADR-0009, ADR-0010, ADR-0015
- **Supersedes:** None
- **Superseded by:** [ADR-0018](ADR-0018-WINDOWS-PROFILE-BROWSER-LIBRARY.md)

> This record is retained as decision history. ADR-0018 moves durable bookmarks and recent visits
> to the current Windows user profile and permits only a session-scoped in-memory projection on the
> receiver. New implementation work must follow ADR-0018.

## Context

A browser people return to needs somewhere to keep the sites they return to. Without it, every visit
starts by typing a full address on a remote, which is the slowest thing this feature asks anyone to
do (ADR-0015).

That means storing browsing data, and browsing data is sensitive. ADR-0009 keeps preview pixels
memory-only, and this ADR must say plainly why the library is treated differently: a preview frame
is a transient artefact of a debugging aid, while a bookmark is a thing a person deliberately made
and expects to survive a restart. Persisting the first would be surveillance; refusing to persist
the second would make the feature pointless.

ADR-0001 puts the browser on the receiver. The library follows the browser, not the desktop.

## Decision

- Bookmarks and history live **on the receiver**, in its own private app storage. Windows never
  keeps a second copy; the cockpit renders a projection of what the television sent.
- Both collections are **strictly bounded**: 200 bookmarks, 500 history entries, titles capped, and
  the file itself capped. Newest first, deduplicated by canonical URL.
- Only URLs that pass `BrowserUrlPolicy` are stored, so the library can never become a route to an
  address the browser would refuse to open.
- Writes are **atomic**: temporary file plus rename, so a power cut cannot leave the only copy
  half-written — a real risk on the API-25 devices in scope.
- A malformed, truncated or future-version file **recovers to empty** rather than failing. A
  browser that will not start because its bookmark file is corrupt is worse than one that has
  forgotten them.
- The library is included in **Clear TV browser data**, alongside cookies, storage, cache and
  WebView history. The confirmation names it.
- The file is versioned, so a later format change has somewhere to branch.

## Consequences

### Positive

- Returning to a site is a couple of presses instead of a full address.
- The stored set is bounded by construction, so it cannot grow without limit on a constrained device.
- Corruption is a recoverable state rather than a failure to launch.
- One erase clears everything, and it says what it erases.

### Trade-offs

- Browsing history now exists on disk on the television, which it did not before. That is a real
  privacy change and the reason it is an explicit, clearable, bounded decision rather than a
  side effect.
- Bounds mean old entries are dropped silently once the caps are reached.
- Anyone with the device and developer access can read the file; app-private storage is not
  encryption, and no document should claim otherwise.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| No library at all | Every visit starts with typing a full URL on a remote. |
| Keep it in memory only | Lost on every restart, which is the same as not having it. |
| Store it on Windows | Contradicts ADR-0001, and makes the television's browser depend on a PC being present. |
| Unbounded history | Unbounded disk and parse cost on a device with neither to spare. |
| Encrypt the file | Implies a threat model — a key, and somewhere to keep it — that this feature has not designed and must not imply it has. |
| Exclude it from Clear Data | Makes "clear browsing data" a false statement. |

## Invariants and validation

- `BrowserLibraryStoreTest` proves the bounds, canonical-URL deduplication, ordering, clear
  semantics, and recovery from a corrupt file.
- Entries that fail `BrowserUrlPolicy` are refused on the way in.
- The clear path erases the library with the rest of the browsing data in one operation.

## Revisit criteria

A measured need for larger bounds on real usage, or a decision to sync the library to the desktop —
which would be a new privacy commitment and needs its own ADR, not an extension of this one.

## References

- [Master browser plan](../../FIRE_TV_BROWSER_PLAN.md)
- [Project constraints](../../../AGENTS.md)
- [ADR-0010 — native dialogs, data clear and recovery](ADR-0010-NATIVE-DIALOGS-DATA-CLEAR-AND-RECOVERY.md)
