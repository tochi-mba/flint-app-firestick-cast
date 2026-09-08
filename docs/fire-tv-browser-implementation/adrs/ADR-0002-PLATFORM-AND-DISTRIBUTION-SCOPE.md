# ADR-0002 — platform and distribution scope

- **Status:** Accepted
- **Implementation:** Planned
- **Date:** 2026-09-04
- **Deciders:** Flint engineering
- **Owners:** Slices 01 and 09
- **Related:** ADR-0001, ADR-0011

## Context

A Fire TV brand label does not guarantee Android or APK compatibility. Some current devices use Vega
OS and cannot host the Android receiver. Older Android-based Fire OS devices, including the AFTMM
target, have API/provider/resource constraints that cannot be inferred from a desktop emulator.

Amazon also restricts third-party browser apps on Fire TV. An embedded WebView does not make public
distribution automatically permissible. Technical feasibility and distribution authorization are
separate facts.

## Decision

The browser feature supports only an **evidenced Android-based Fire OS receiver**. Capability
reporting must distinguish at least unsupported platform, protocol too old, secure endpoint absent,
probe pending, WebView unsupported, and available/eligible. Unknown hardware/API is unavailable
until a probe record says otherwise.

Until written Amazon approval is recorded, the feature is documented, packaged, and presented as a
personal/developer sideload experiment:

- do not claim public Appstore availability;
- do not disguise the browser feature in metadata/marketing to bypass policy;
- do not install/replace/remove the TV receiver without user-visible explanation and approval;
- include a removal path wherever installation is offered.

Vega is a definite unsupported result; no APK/ADB workaround is designed.

## Consequences

### Positive

- Users receive a truthful remedy instead of a broken “Web” button.
- Device/model/API/provider evidence can drive support decisions and regression analysis.
- Distribution claims stay legally/policy accurate even when the technical implementation succeeds.
- Target hardware constraints force early WebView/preview feasibility testing.

### Trade-offs

- The feature may remain developer-only despite passing all engineering tests.
- Capability UI and test matrix are more complex than a generic “Android supported” flag.
- A physical target/device evidence process is mandatory before broad implementation/release.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Treat every Fire TV as Android-compatible | False on Vega and unsafe on unknown models. |
| Submit to Appstore first and resolve policy later | Risks misleading release work and does not establish approval. |
| Hide unsupported states behind a generic error | Prevents actionable diagnosis and violates product truthfulness. |
| Build an alternate Vega receiver path | Materially different platform/product effort; outside this feature scope. |
| Use emulator-only qualification | Cannot prove actual Fire TV provider, D-pad, memory, capture, or lifecycle behaviour. |

## Invariants and validation

- The pure capability reducer has exhaustive tests for supported, unsupported, unknown, stale, and
  contradictory observations.
- Hardware runs identify model, Fire OS/API, receiver build, WebView behaviour, and test date.
- UI/docs/site tests reject wording that calls the feature generally available or Appstore-ready
  without the explicit approval record.
- Default automated tests have no Fire TV dependency; physical tests are opt-in and named.

## Revisit criteria

A new supported platform requires an ADR update/superseding record with real-device evidence,
receiver installation mechanism, and capability semantics. Public Appstore distribution requires
written Amazon approval linked in the release evidence; a verbal assumption is insufficient.

## References

- [Slice 01 — feasibility and quality gate](../SLICE-01-FEASIBILITY-AND-QUALITY-GATE.md)
- [Slice 09 — release evidence](../SLICE-09-HARDENING-RELEASE-EVIDENCE-AND-DOCUMENTATION.md)
- [Amazon device filtering and browser policy](https://developer.amazon.com/docs/app-submission/device-filtering-and-compatibility.html)
