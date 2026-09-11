# Slice 10 — receiver install over ADB

**Governing ADR:**
[ADR-0027](adrs/ADR-0027-RECEIVER-INSTALLATION-REQUIRES-RSA-CONSENT-AND-OFFERS-REMOVAL.md).

## Outcome

A phone-only setup works end to end. Somebody with a new Fire TV and no PC can install the receiver
from the phone, and remove it again from the same screen.

## Entry criteria

- Slice 04 finds a television and reports its address.
- Slice 02's release workflow stages the receiver package into the phone app's assets.

## User-visible vertical behaviour

1. The Settings tab names exactly what would be installed — package, version, size, where it came
   from and what it does — before offering to install it.
2. Pressing Install connects over ADB, and the television shows its own authorisation prompt.
3. The phone waits for that prompt and says so, pointing at the TV remote rather than offering a way
   around it.
4. When it is installed, the same screen offers to remove it.
5. A Vega OS device is refused plainly, with no remedy offered and nothing to try.

## Scope

### In scope

- The RSA identity: generated once, persisted, and used for every connection afterwards so the
  television does not re-prompt.
- `AdbConnection` and `AdbAuth` from `:protocol`, which already implement the wire protocol.
- Read-only identification first: ask the television what it is, change nothing.
- `pm install` of the bundled package, and `pm uninstall` of it.
- `ReceiverSetup` in `:castcore`: the disclosure, the stage machine and the copy, all pure.
- `ReceiverPackage`: reading the bundled APK's own package name and version through
  `getPackageArchiveInfo` rather than trusting the sidecar the workflow writes.

### Explicitly out of scope

- Enabling ADB debugging on the television. That is a setting only its owner can change, and the copy
  says where it is rather than pretending otherwise.
- Installing anything that did not come with this build.

## Three constraints that shape the screen

**The prompt is honoured, not routed around.** It is the television owner's only say in what runs on
their television. `ReceiverInstallStage.AWAITING_AUTHORISATION` therefore has no action on the phone
at all: the only thing that moves it forward is somebody picking up the remote.

**Removal is offered wherever installation is.** On the same screen, not in a different menu and not
only once something has gone wrong.

**Nothing is installed without saying what.** `ReceiverSetupPlan` will not construct with an install
action and an empty disclosure; that is a `require`, not a convention.

## The package is read, not recorded

The workflow writes a sidecar naming the commit and the hash, and that is useful for diagnostics. What
appears above the words "this is what will be installed" comes from the archive itself, because a
properties file says what somebody intended to bundle and the APK says what is actually there.

It is a debug-signed build, and the screen says so. `pm install` refuses an unsigned APK and the
receiver's release signing key is not available to the workflow, so the package name the phone
installs ends in `.debug`. Hiding that would mean somebody later finds two Flint receivers on their
television and no explanation.

## Test-first implementation order

1. `ReceiverSetup`, every stage and every platform, including that Vega carries no remedy whatever
   stage anything thinks it is at.
2. The disclosure, asserting the package name comes first and the debug note appears only for a debug
   package.
3. `ReceiverPackage` against a real archive under Robolectric.
4. The ADB flow against a physical television, which is the only place it can be exercised.

## Full-suite checkpoint

The default suite, plus the physical-device job. That job requires a serial and an explicit
acknowledgement flag, and prints every device-affecting action before performing it.

## Exit evidence

- An install and a removal on a real Fire TV, from a phone, with no PC involved.
- A UI test asserting both affordances appear on one screen.
- A copy test asserting the Vega verdict carries no remedy.

## Rollback and stop conditions

Reverting removes the Settings card; pairing with an already-installed receiver is unaffected. Stop
if the flow would need to suppress or pre-answer the television's prompt for any reason.
