# Fire TV browser architecture decision records

This directory records durable architecture decisions for the Fire TV-resident browser feature. It is
deliberately separate from implementation slices:

- A **slice** says what to build, test, measure, and release in a vertical increment.
- An **ADR** says why a consequential design choice was made, what alternatives were rejected, and
  what would have to be true to revisit it.

The records use the status and implementation fields independently. “Accepted” means the project
has chosen the direction; it does not claim production code, device evidence, or release scope
already exists. Implementation is a separate evidence field, so an ADR never turns into a
misleading claim that a design has shipped. Superseded records remain in the index as history.

## ADR status rules

| Status | Meaning |
|---|---|
| Proposed | Under review; code must not depend on it. |
| Accepted | Chosen design; implementation/tests/evidence may still be required. |
| Superseded | Replaced by a named later ADR; retain this file for history. |
| Deprecated | Still historically relevant but no longer recommended for new work; state migration path. |
| Rejected | Considered and intentionally not selected. |

Do not rewrite the history of an accepted ADR when a decision changes. Add a new ADR, mark the old
one Superseded, update the implementation-slice links, vectors, and user documentation in the same
change.

Implementation states are **Planned**, **In progress**, **Implemented**, and **Release-verified**.
Moving between them requires the linked slice's tests and evidence; it does not change the ADR's
decision status.

## Governance and precedence

Within this feature, resolve documentation conflicts in this order:

1. Applicable AGENTS.md project constraints (and all higher-priority system/developer/user
   instructions).
2. Accepted ADRs in this directory.
3. The [master browser plan](../../FIRE_TV_BROWSER_PLAN.md).
4. [docs/PROTOCOL.md](../../PROTOCOL.md) for exact wire bytes, versions, and framing.
5. Implementation slices for test-first work order and exit evidence.
6. Measurement/run records and the research ledger as evidence, never as a replacement for a
   decision.

An ADR records rationale; it cannot weaken AGENTS.md. The protocol document remains the byte-level
authority, and docs/LATENCY_BUDGET.md remains the authority for observed measurements.

## Decision index

| ADR | Decision status | Implementation | Decision | Owning slices |
|---|---|---|---|---|
| [0001](ADR-0001-TV-RESIDENT-BROWSER-OWNERSHIP.md) | Accepted | Planned | WebView/network/cookies/rendering live exclusively on the Android receiver; Windows is a remote/status surface. | 01, 04–09 |
| [0002](ADR-0002-PLATFORM-AND-DISTRIBUTION-SCOPE.md) | Accepted | Planned | Support only evidenced Android-based Fire OS and distribute as sideload/developer experiment pending written Amazon approval. | 01, 09 |
| [0003](ADR-0003-SEPARATE-PINNED-TLS-BROWSER-SESSION.md) | Accepted | Planned | Browser data uses an independent TLS 1.2+ BrowserSession with Keystore identity and Windows SPKI pinning. | 02–09 |
| [0004](ADR-0004-NONSECRET-ENDPOINT-DISCOVERY.md) | Accepted | Planned | Advertise the browser TLS endpoint as non-secret receiver metadata; never treat discovery as identity. | 03, 09 |
| [0005](ADR-0005-PROTOCOL-V2-CANONICAL-CONTRACT.md) | Accepted | Planned | Use protocol v2, strict browser types, actual negotiated versions, Rust-canonical golden vectors, and ordinary-channel rejection. | 02–09 |
| [0006](ADR-0006-WEBVIEW-SECURITY-PROFILE.md) | Accepted | Planned | Use a fixed HTTPS-only WebView profile with no native bridge or escape hatches. | 01, 04, 06, 09 |
| [0007](ADR-0007-SURFACE-LIFECYCLE-AND-TV-FIRST-UX.md) | Accepted | Planned | Keep WebView activity-owned, browser surface exclusive, and TV D-pad/overscan behaviour first-class. | 04–09 |
| [0008](ADR-0008-PORTABLE-INPUT-AND-STALE-GUARDS.md) | Accepted | Planned | Use typed semantic/native input, explicit Unicode text, monotonic IDs, and current-preview references; never raw/global input. | 02, 05, 08, 09 |
| [0009](ADR-0009-OPT-IN-BOUNDED-JPEG-PREVIEW.md) | Accepted | Planned | Provide optional latest-only JPEG preview with hard capacity/privacy/priority bounds; TV remains canonical. | 01, 03, 07–09 |
| [0010](ADR-0010-NATIVE-DIALOGS-DATA-CLEAR-AND-RECOVERY.md) | Accepted | Planned | Keep page dialogs and destructive data actions native/TV-confirmed, with bounded recovery and no permission reuse. | 06, 09 |
| [0011](ADR-0011-TEST-EVIDENCE-AND-LATENCY-GOVERNANCE.md) | Accepted | Planned | Require test-first, scoped full coverage, cross-language proof, opt-in hardware evidence, and measured regression gates. | 01–09 |
| [0012](ADR-0012-USER-INITIATED-BOUNDED-TABS.md) | Accepted | Implemented (software) | Allow user-initiated tabs, capped at 8 with at most 2 live renderers and the rest frozen to saved state. | TV browser UX |
| [0013](ADR-0013-PAGE-REQUESTED-FULLSCREEN.md) | Accepted | Implemented (software) | Honour the page's fullscreen request in a fixed order, one at a time, answering its callback exactly once. | TV browser UX |
| [0014](ADR-0014-DUAL-INTERACTION-MODEL-AND-BACK-LADDER.md) | Accepted | Implemented (software) | Ship cursor and focus D-pad modes with an accelerating pointer, and one ordered Back ladder. | TV browser UX |
| [0015](ADR-0015-TV-LOCAL-ADDRESS-ENTRY-AND-SEARCH.md) | Accepted | Implemented (software) | Give the television its own omnibox and keyboard, resolving address-or-search above the unchanged URL policy. | TV browser UX |
| [0016](ADR-0016-RECEIVER-OWNED-BOOKMARKS-AND-HISTORY.md) | Superseded by 0018 | Implemented (software); removal pending | Historical receiver-owned bookmark/history decision. Do not use for new work. | TV browser UX |
| [0017](ADR-0017-ADDITIVE-COCKPIT-MESSAGE-IDS.md) | Accepted; amended by 0018 | In progress | Add cockpit families as ids 21–27; discover support from receiver-originated family messages before the host sends optional families. | Protocol, Windows cockpit |
| [0018](ADR-0018-WINDOWS-PROFILE-BROWSER-LIBRARY.md) | Accepted | In progress | Keep the durable library in the current Windows user profile; expose only a session-only TV projection through receiver→host id 26 requests/mutations and host→receiver id 27 snapshots. | TV browser UX, protocol, Windows cockpit/privacy |
| [0019](ADR-0019-MOSAIC-BOUNDED-MULTI-LIVE.md) | Superseded by 0023 | Historical mosaic scaffold; do not extend. | TV browser UX |
| [0020](ADR-0020-PANE-LOCAL-FULLSCREEN.md) | Superseded by 0023 | Historical pane-fullscreen notes; see ADR-0023. | TV browser UX |
| [0021](ADR-0021-PER-PANE-MEDIA-CONTROL.md) | Superseded by 0023 | Historical per-pane media notes; see ADR-0023. | TV browser UX |
| [0022](ADR-0022-PROFILE-GATED-VPN.md) | Accepted; amended by 0023 | Optional WireGuard profile network; consent + soft-fail; require-VPN fail-closed in 0023. | TV browser UX, network |
| [0023](ADR-0023-BROWSER-WORKSPACE-AND-WORKSPACE-VPN.md) | Accepted | In progress | Independent-page browser workspace (not video mosaic); pane-local fullscreen; workspace VPN; protocol v3 types 32–34. | TV browser UX, protocol, Windows cockpit, network |

## How an implementer uses an ADR

Before changing a slice's architecture, read every ADR linked to it. In the implementation change:

1. Link tests and production changes to the relevant ADR ID in the PR/issue.
2. Keep the ADR's invariants executable where possible: a golden vector, source guard, property test,
   UI test, loopback test, or hardware acceptance record.
3. If evidence invalidates an assumption, stop the slice, record the observation, and propose a
   superseding ADR instead of quietly taking a shortcut.
4. When a slice is genuinely complete, update the ADR's **Implementation** field only if the linked
   evidence supports it. A partial vertical slice does not implement an ADR by itself unless its
   record says so.
5. Never use an ADR to waive the AGENTS.md constraints on truthfulness, sockets, D-pad UX, or
   hardware-free default tests.

## Creating or changing a record

Start from [TEMPLATE.md](TEMPLATE.md). Assign the next four-digit number; use a concise,
stable filename; and list the owning slices. An accepted record is append-only in substance: use a
new record with reciprocal **Supersedes** / **Superseded by** links for a material change. Keep exact
wire format in docs/PROTOCOL.md and measured latency in docs/LATENCY_BUDGET.md rather than copying
either into an ADR.

## Related documents

- [Implementation slices](../README.md)
- [Master Fire TV browser plan](../../FIRE_TV_BROWSER_PLAN.md)
- [Wire protocol](../../PROTOCOL.md)
- [Latency budget](../../LATENCY_BUDGET.md)
- [Research claim ledger](../../../report-source.md)
- [Project constraints](../../../AGENTS.md)
