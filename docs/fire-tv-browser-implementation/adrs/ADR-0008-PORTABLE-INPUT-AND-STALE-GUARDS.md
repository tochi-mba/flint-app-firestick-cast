# ADR-0008 — portable input and stale-data guards

- **Status:** Accepted
- **Implementation:** Planned
- **Date:** 2026-09-04
- **Deciders:** Flint engineering
- **Owners:** Slices 02, 05, 08, and 09
- **Related:** ADR-0003, ADR-0005, ADR-0007, ADR-0009

## Context

Windows, Android, and WebView expose different keyboard, input-method, pointer, and display
coordinate APIs. Passing raw Windows virtual keys or global desktop events through a receiver would
make the protocol host-specific, risk unintentional keystroke capture, and make input timing/
ordering impossible to reason about. Sending typed text through injected JavaScript would create an
unnecessary native bridge and reveal page integration assumptions.

A remote receiver also has asynchronous state: navigation can change while a host is looking at an
older BrowserState or preview. A click mapped to a stale frame must not act on a newer page. A
disconnected text send must not silently retry, because a user cannot know whether the original
form field received it.

## Decision

Browser input is a typed, portable, TLS-only contract:

- BrowserInput contains an epoch and a session-owned strictly monotonic sequence. Commands have
  their own monotonic command ID; BrowserState reports bounded last-accepted IDs for non-secret
  correlation.
- The only navigation keys are semantic values: Up, Down, Left, Right, Select, Back, Tab,
  Shift-Tab, Escape, PageUp, PageDown, Home, End, and Refresh. Raw scan codes, virtual keys,
  global hooks, and arbitrary desktop keyboard forwarding have no wire form.
- Text is user-initiated through a dedicated Windows **Send text to TV** field, valid UTF-8, bounded
  to 4 KiB, and delivered through BrowserPort's native IME/InputConnection-compatible path. The
  address bar is not a page-text proxy.
- Pointer/scroll remain disabled until a current, consented preview exists. When enabled, pointer
  input carries current epoch, navigation ID, frame ID, fixed normalized coordinates, primary action,
  and action order. The receiver rejects stale/non-finite/out-of-order/invalid input before it can
  reach WebView.
- Input is never scripted, DOM-derived, accessibility-service injected, clipboard-scraped, or
  dispatched through a JavaScript/native bridge.
- On focus loss, close, new epoch, disconnected session, surface switch, or cancellation, pending
  input is invalidated and active pointer state receives a bounded native cancel/up equivalent.
- BrowserSession owns sequences and serialized sending. View models never invent IDs. An
  unknown/disconnected text outcome is reported to the user and is not replayed automatically.

## Consequences

### Positive

- The protocol is portable and auditable across C#, Kotlin, and Rust.
- The product does not capture unrelated desktop input or need broad OS privileges.
- Epoch/frame/sequence checking makes stale preview interaction and late network events safe.
- Text has a clear privacy boundary and can be measured through non-secret acknowledgement IDs.
- TV D-pad remains the primary native interaction path even if the host is absent.

### Trade-offs

- Some page behaviours cannot be perfectly emulated by a host remote, and unsupported input must be
  reported rather than faked.
- Explicit text entry requires a deliberate Send action; it cannot behave like transparent desktop
  keyboard mirroring.
- Pointer interaction requires later preview evidence and may be disabled permanently on constrained
  targets.
- Input rejection/focus/disconnect states add testable product complexity.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Forward all Windows key events | Captures unrelated input, leaks host-platform semantics, and makes focus/security unclear. |
| Global keyboard hook | Violates least privilege and user expectations; unnecessary for explicit browser controls. |
| Raw virtual-key/scancode protocol | Not portable to Android/WebView and can encode undocumented OS behaviour. |
| Inject text/events through JavaScript | Creates prohibited native bridge/injection surface and depends on page DOM. |
| Automatically retry text after reconnect | Risks duplicate form input/submission and cannot truthfully report delivery. |
| Accept pointer based only on coordinates | A stale preview can target a different page; frame/navigation reference is required. |

## Invariants and validation

- Rust/C#/Kotlin vectors cover every semantic input, UTF-8/boundary case, sequence/epoch condition,
  pointer action/reference, and malformed value.
- Property/fuzz/mutation tests prove input validation cannot accept unknown key, raw code, invalid
  Unicode, oversized text, out-of-range coordinate, missing frame reference, or stale identifier.
- BrowserSession concurrency tests prove only it allocates monotonic sequences and serializes sends.
- Fake BrowserPort tests prove unauthenticated, stale, duplicate, focus-lost, closed, or
  disconnected input never dispatches.
- Controlled WebViewAssetLoader fixture tests prove native text/semantic/pointer/scroll behaviour
  without real website DOM inspection.
- Source/static review rejects global hooks, WebView JavaScript bridge/evaluation APIs, and
  accessibility-service input in production paths.
- Physical Fire TV evidence compares host input to the independently usable D-pad path.

## Revisit criteria

Adding a new input type requires an ADR amendment or replacement with a platform-neutral schema,
threat model, privacy/retention analysis, exact state/ordering rules, all-language vectors,
instrumentation, and hardware evidence. A page or platform that “needs a shortcut” does not
override the typed-input boundary.

## References

- [Slice 05 — secure remote controls and text](../SLICE-05-SECURE-REMOTE-CONTROLS-AND-TEXT.md)
- [Slice 08 — preview interaction and accessibility](../SLICE-08-PREVIEW-INTERACTION-AND-ACCESSIBLE-POLISH.md)
- [ADR-0005 — protocol v2 contract](ADR-0005-PROTOCOL-V2-CANONICAL-CONTRACT.md)
