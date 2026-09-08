# ADR-0015 — TV-local address entry and search

- **Status:** Accepted
- **Implementation:** Implemented (software)
- **Date:** 2026-09-06
- **Scope:** Fire TV browser experiment
- **Deciders:** Flint engineering
- **Owners:** TV browser UX workstream
- **Related:** ADR-0001, ADR-0006, ADR-0014, ADR-0016
- **Supersedes:** None
- **Superseded by:** None

## Context

The television could not open anything by itself. There was no text entry of any kind, and the
chrome's own documentation argued that the address bar belonged on the desktop "because building a
browser UI operable by five buttons on a remote is the thing this feature exists to avoid."

Typing with a remote is genuinely unpleasant, so that reasoning is understandable. It is also the
wrong conclusion: it makes a PC a prerequisite for browsing at all, on a device most people will
use from a sofa with the remote in their hand.

There was a second gap. `BrowserUrlPolicy` rejects a bare word twice over — once for not being
absolute, once for having no dot — which is exactly right for a security policy and useless as a
browser. Searching for something was impossible, not merely awkward.

## Decision

- The television gets a **full-screen omnibox**: an address field, suggestions, and a D-pad
  keyboard. Full screen rather than inline, because at ten feet a field with a keyboard under it
  needs the room, and taking over the screen makes it obvious the D-pad now belongs to the keyboard.
- A new **`BrowserQueryResolver`** decides address-or-search **above** `BrowserUrlPolicy`, which is
  unchanged. Whatever it produces still passes the same https-only rules; search is not a way around
  the security profile.
- Resolution order mirrors the host's `BrowserAddressBarResolver`, so typing the same thing on the
  television and the desktop goes to the same place: whitespace is a search; absolute `https` is an
  address; absolute `http` is upgraded rather than refused; a scheme this browser will not run is
  searched for rather than silently dropped; a bare token with a dot is a host; anything else is a
  search.
- **The search engine is chosen on first use** and remembered, from DuckDuckGo, Google, Bing, or a
  custom https template containing `{q}`. A template that is not https, or has nowhere to put the
  terms, is refused rather than quietly ignored.
- The keyboard is a **rectangular grid with wrap-around on both axes**, so no edge is a dead end;
  **shift releases after one letter**; and a `.com` key exists because that suffix is four presses
  nobody should have to make.
- Typed text is bounded to the wire's address limit, so nothing typed can fail to send.
- The desktop stays the faster way to type and the sheet says so — as an offer, never a requirement.

## Consequences

### Positive

- The television is a browser on its own; the desktop is an accelerator.
- Searching is possible at all, which it previously was not.
- Both ends resolve what was typed identically.
- The security profile is untouched: every search URL goes through the same policy.

### Trade-offs

- Typing on a remote remains slow; a grid keyboard improves it but does not fix it.
- A chosen engine is a stored preference, which is one more piece of state to clear.
- The omnibox takes the whole screen while open, hiding the page behind it.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Keep address entry desktop-only | Makes a PC a prerequisite for using a television feature. |
| Teach `BrowserUrlPolicy` to search | Mixes a security boundary with a convenience heuristic; the policy must stay simple enough to audit. |
| Hardcode one search engine | A television is the worst place to be stuck with someone else's choice. |
| Default to Google without asking | Its consent interstitials are common from an unusual WebView user agent and painful to clear with a D-pad. |
| Inline address bar in the chrome | Illegible at ten feet once a keyboard is under it. |
| Rely on the system IME | WebView's IME focus is unreliable in embedded contexts, and the layout would not be ours to make D-pad-safe. |

## Invariants and validation

- `BrowserQueryResolverTest` covers the full rule table and asserts that **every engine's output is
  accepted by `BrowserUrlPolicy`** — the contract that keeps search inside the security profile.
- Dangerous schemes (`javascript:`, `file:`, `data:`, `intent:`) resolve to a search, never a
  navigation and never silence.
- Search terms are percent-encoded, so a query cannot alter the URL it is placed in.
- `BrowserKeyboardTest` proves every page is a full rectangle, the alphabet and digits are all
  reachable, movement wraps and never escapes the grid from any cell in any direction, shift is
  one-shot, and text is bounded.

## Revisit criteria

Evidence that voice input is dependable on Fire TV for third-party apps would justify promoting it
from a probed, optional control. A request to add a non-https search engine does not; that would
reopen ADR-0006.

## References

- [Master browser plan](../../FIRE_TV_BROWSER_PLAN.md)
- [Project constraints](../../../AGENTS.md)
- [Alternative input methods for Android TV](https://android-developers.googleblog.com/2018/08/alternative-input-methods-for-android-tv.html)
