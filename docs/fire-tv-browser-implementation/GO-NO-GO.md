# Fire TV browser — go / no-go release checklist

This checklist is the Slice 09 gate. A public Appstore release is **not** an automatic outcome of
passing software tests. Sideload/developer use may proceed earlier when the security and device
evidence rows are green.

## Software suite

- [ ] `.\scripts\build.ps1` (Rust + .NET) green on a clean checkout
- [ ] `.\gradlew.bat --no-daemon :protocol:check :receiver:testDebugUnitTest :receiver:lintDebug` green
- [ ] Golden corpus completeness: Rust, C#, Kotlin agree on every `testdata/golden/*.bin`
- [ ] Browser frames rejected on ordinary CastSession / ReceiverServer
- [ ] No file over 1,000 lines introduced by this feature without an explicit split

## Security

- [ ] Browser traffic never crosses the plaintext cast channel
- [ ] First pairing requires fingerprint comparison; mismatch has no bypass
- [ ] WebView has no JS bridge / file access / mixed content / external intents
- [ ] Logs redact URLs, text, pairing codes, certificates, and preview pixels

## Device evidence

- [ ] `.\scripts\test-fire-tv-browser.ps1 -Serial <id> -AcknowledgePhysicalDevice` run recorded
- [ ] `.\scripts\test-fire-tv-app-pair.ps1 -Serial <id> -AcknowledgePhysicalDevice` run recorded (Cast Pair + Web via app UI)
- [ ] Target Fire OS / API / WebView versions recorded in `docs/LATENCY_BUDGET.md`
- [ ] Command round-trip and preview resource bounds measured on hardware
- [ ] D-pad path remains fully usable without host pointer preview

## Distribution

- [ ] Sideload/developer packaging documented in `docs/INSTALL.md`
- [ ] Written Amazon Appstore approval obtained **before** any public browser listing claim
- [ ] Unavailable states remain honest when the feature is gated off

## Decision

| Date | Build SHA | Decision | Signed by |
|---|---|---|---|
| _pending_ | _pending_ | go / no-go / sideload-only | _pending_ |

Record the decision here and link the raw telemetry location. Do not invent latency claims.

## CI Gradle gate (software)

Until the primary CI image always has JDK 17, run this locally or on an Android-capable agent after every browser slice:

~~~powershell
$env:JAVA_HOME = "<jdk17>"
.\gradlew.bat --no-daemon :protocol:check :receiver:testDebugUnitTest :receiver:lintDebug --console plain
~~~

Treat a red Gradle suite as a release blocker even when `.\scripts\build.ps1` is green.

