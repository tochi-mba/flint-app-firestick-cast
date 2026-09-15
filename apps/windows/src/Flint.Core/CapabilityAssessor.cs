namespace Flint.Core;

/// <summary>
/// Decides which <see cref="CastMode"/>s a host-and-device pair supports.
/// </summary>
/// <remarks>
/// <para>
/// This is the honesty mechanism for capability checks. It is pure: same inputs, same verdicts, no I/O,
/// no clock, no ambient state. Everything it reports is traceable to evidence in its arguments.
/// </para>
/// <para>
/// Verdict precedence is deliberate. A hardware impossibility outranks an unavailable capability,
/// because telling someone a feature is "coming soon" for a device that can never run it would be
/// a lie told in a friendlier tone.
/// </para>
/// </remarks>
public static class CapabilityAssessor
{
    /// <summary>
    /// Windows build from which an indirect display cannot be made the primary display.
    /// </summary>
    /// <remarks>
    /// Build 26100 is Windows 11 24H2. The limitation persists through 25H2 and is a platform
    /// defect rather than a Flint one, so it is reported as a caveat, not a blocker.
    /// </remarks>
    public const int IddPrimaryDisplayBrokenFromBuild = 26100;

    /// <summary>Throughput below which even direct-play media handoff stutters.</summary>
    public const double MinimumMediaHandoffMbps = 8.0;

    /// <summary>
    /// Whether this build of Flint can turn a captured frame into a stream the receiver can
    /// decode.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Capture and hardware encoding are probed independently, and either can report itself
    /// present without this being true: on their own, neither produces a stream. This became true
    /// once the capture to encode to transport path was proven end to end against a Fire TV Stick
    /// with the desktop visibly on screen, which is the only evidence that counts — an earlier
    /// build reported thousands of frames sent and a healthy receiver bitrate while the television
    /// showed flat colour, because the encoder was being handed blank buffers.
    /// </para>
    /// <para>
    /// <c>static readonly</c> rather than <c>const</c> on purpose: a literal constant lets the
    /// compiler prove the branches guarded by it unreachable and fail the build under
    /// <c>TreatWarningsAsErrors</c>. A field keeps both sides compiling.
    /// </para>
    /// </remarks>
    public static readonly bool MirrorStreamingImplemented = true;

    /// <summary>
    /// Whether this build of Flint can present the receiver as an additional desktop rather than a
    /// copy of this one.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="MirrorStreamingImplemented"/> because the two need different
    /// things: a second screen is a mirror plus an indirect display driver, and that driver does
    /// not exist in this build. Sharing one flag between them meant proving the mirror worked
    /// silently promised a second screen that would never appear.
    /// </remarks>
    public static readonly bool SecondScreenImplemented = false;

    /// <summary>Produces a verdict for every mode.</summary>
    /// <param name="device">The device, or <see langword="null"/> when discovery found none.</param>
    /// <param name="host">Probed host capabilities.</param>
    /// <param name="path">The measured path, or <see langword="null"/> when not yet measured.</param>
    /// <param name="pairedSessionActive">
    /// Whether a CastSession has already authenticated with this receiver. Pairing proves an
    /// Android Flint receiver is running, so ADB platform-identity gates no longer apply.
    /// </param>
    public static CapabilityReport Assess(
        FireTvDevice? device,
        HostCapabilities host,
        NetworkPath? path,
        bool pairedSessionActive = false)
    {
        ArgumentNullException.ThrowIfNull(host);

        List<ModeVerdict> verdicts =
        [
            AssessMirror(device, host, path, pairedSessionActive),
            AssessSecondScreen(device, host, path, pairedSessionActive),
            AssessMediaHandoff(device, path, pairedSessionActive),
        ];

        return new CapabilityReport(device, host, path, verdicts)
        {
            Browser = BrowserCapabilityAssessor.Assess(device),
        };
    }

    private static ModeVerdict AssessMirror(
        FireTvDevice? device,
        HostCapabilities host,
        NetworkPath? path,
        bool pairedSessionActive = false)
    {
        const CastMode mode = CastMode.Mirror;

        var receiver = AssessReceiverAvailability(device, pairedSessionActive);
        if (receiver is not null)
        {
            return receiver with { Mode = mode };
        }

        if (!host.CanJudgeEncoding)
        {
            return new ModeVerdict(
                mode,
                ModeStatus.Blocked,
                "Flint has not probed this PC for hardware encoders yet, so it cannot say whether "
                    + "mirroring would work. It will not guess from the graphics card name.",
                "Run the encoder probe from Diagnostics.");
        }

        if (!host.HasSessionCapableEncoder)
        {
            return new ModeVerdict(
                mode,
                ModeStatus.Blocked,
                "No hardware video encoder was found on this PC. Software encoding cannot meet the "
                    + "latency this mode exists to deliver, so Flint will not offer it.",
                "Update your graphics driver, then probe again. On a laptop, check that the "
                    + "discrete GPU is not disabled in your power profile.");
        }

        if (!host.CanCaptureScreen)
        {
            return new ModeVerdict(
                mode,
                ModeStatus.Blocked,
                "This build of Flint has no screen-capture backend, so it has nothing to send. The "
                    + "receiver can decode a mirror stream; this PC cannot yet produce one.",
                "Update Flint. Nothing on this PC or TV needs changing.");
        }

        if (path is not null && !path.LatencySupportsMirroring)
        {
            return new ModeVerdict(
                mode,
                ModeStatus.Blocked,
                $"The measured round trip to the receiver is {path.RoundTripMs:F0} ms. Mirroring "
                    + $"needs it below {NetworkPath.UsableRoundTripCeilingMs:F0} ms to feel attached "
                    + "to the mouse.",
                "Move the receiver onto 5 GHz, or put it on Ethernet. Wall thickness and 2.4 GHz "
                    + "congestion account for most failures here.");
        }

        if (path is not null && path.ThroughputTooLowForMirroring)
        {
            return new ModeVerdict(
                mode,
                ModeStatus.Blocked,
                $"The measured path carries {path.ThroughputMbps:F1} Mbit/s. Mirroring needs at "
                    + $"least {NetworkPath.MinimumMirrorThroughputMbps:F0} Mbit/s.",
                "Move the receiver onto 5 GHz, or put it on Ethernet. Wall thickness and 2.4 GHz "
                    + "congestion account for most failures here.");
        }

        var capacity = path is { ThroughputMeasured: false }
            ? " Throughput has not been measured yet, so capacity is still unproven."
            : string.Empty;

        // Every check above passed: the hardware and the network are both provably ready. What is
        // missing is Flint itself, and that is a fact about this build rather than about anything
        // fixable here or on the TV, so it is reported as its own outcome rather than folded into
        // one of the hardware-shaped Blocked verdicts above — those exist for reasons a person can
        // act on, and "wait for an update" is not one of them.
        if (!MirrorStreamingImplemented)
        {
            return new ModeVerdict(
                mode,
                ModeStatus.NotImplemented,
                "This PC and this receiver are both ready: a hardware encoder is present, screen "
                    + "capture works, and the network path is sufficient." + capacity + " Flint has "
                    + "not written the code that turns a captured frame into a stream yet, though — "
                    + "no setting here or on the TV changes that.",
                Remedy: null);
        }

        return Available(
            mode,
            "This PC and this receiver can both do it: a hardware encoder is present and the "
                + "network path is sufficient." + capacity);
    }

    private static ModeVerdict AssessSecondScreen(
        FireTvDevice? device,
        HostCapabilities host,
        NetworkPath? path,
        bool pairedSessionActive = false)
    {
        const CastMode mode = CastMode.SecondScreen;

        // A second screen is a mirror plus a virtual display, so it inherits every mirror
        // blocker. An impossibility or a genuine hardware/network problem is reported exactly as
        // Mirror reports it — a driver-shaped or network-shaped fix belongs to that mode too.
        var mirror = AssessMirror(device, host, path, pairedSessionActive);
        if (mirror.Status is ModeStatus.Impossible or ModeStatus.Blocked)
        {
            return mirror with { Mode = mode };
        }

        var caveat = host.WindowsBuild >= IddPrimaryDisplayBrokenFromBuild
            ? " On this Windows build the virtual display also cannot be made the primary display, "
                + "which is a platform limitation Flint cannot work around."
            : string.Empty;

        // Second screen needs an indirect display driver on top of everything mirroring needs,
        // and no such driver exists in this build. That stays true after the mirror path itself
        // starts working, so it is judged on its own flag rather than inherited from the mirror's
        // — which would have quietly promised a second screen the moment the mirror shipped.
        if (!SecondScreenImplemented)
        {
            // Says what the mode would do, that the pair is capable of it, and what is missing —
            // in that order. Leading with the missing driver made a mode that is merely unfinished
            // read as one this PC could never run.
            var readiness = mirror.Status is ModeStatus.NotImplemented
                ? string.Empty
                : " This PC and this receiver can already mirror, which is the half of it that "
                    + "matters.";

            return new ModeVerdict(
                mode,
                ModeStatus.NotImplemented,
                "Use the TV as an extra desktop rather than a copy of this one." + readiness
                    + " It is not in this build yet: it needs a display driver Flint has to ship "
                    + "and sign before it can be installed safely." + caveat,
                Remedy: null);
        }

        return Available(
            mode,
            "The underlying mirror path is possible on this pair. This mode additionally needs an "
                + "indirect display driver, which changes how your PC boots and is never installed "
                + "silently." + caveat);
    }

    private static ModeVerdict AssessMediaHandoff(
        FireTvDevice? device,
        NetworkPath? path,
        bool pairedSessionActive = false)
    {
        const CastMode mode = CastMode.MediaHandoff;

        var receiver = AssessReceiverAvailability(device, pairedSessionActive);
        if (receiver is not null)
        {
            return receiver with { Mode = mode };
        }

        if (path is not null && path.ThroughputMeasured && path.ThroughputMbps < MinimumMediaHandoffMbps)
        {
            return new ModeVerdict(
                mode,
                ModeStatus.Blocked,
                $"The measured path carries {path.ThroughputMbps:F1} Mbit/s. Playing a file at its "
                    + $"original quality needs at least {MinimumMediaHandoffMbps:F0} Mbit/s.",
                "Move the receiver closer to the router, or onto 5 GHz.");
        }

        return Available(
            mode,
            "The receiver is reachable and the path is sufficient to play files at original quality.");
    }

    /// <summary>
    /// Judges whether a receiver could exist on this device at all.
    /// </summary>
    /// <returns>
    /// A blocking verdict carrying a placeholder <see cref="ModeVerdict.Mode"/> for the caller to
    /// overwrite, or <see langword="null"/> when nothing about the device blocks Flint.
    /// </returns>
    /// <remarks>
    /// The ambiguity in <see cref="AdbConnectionState.Refused"/> is the important case. A silent ADB
    /// port proves nothing: the device may be Vega, or it may be Fire OS with debugging switched
    /// off. Flint reports both possibilities rather than picking the likelier one.
    /// </remarks>
    private static ModeVerdict? AssessReceiverAvailability(
        FireTvDevice? device,
        bool pairedSessionActive = false)
    {
        if (device is null)
        {
            return new ModeVerdict(
                default,
                ModeStatus.Blocked,
                "No receiver has been found on this network yet.",
                "Check that the television is powered on and on the same network as this PC, then "
                    + "run the probe again. Diagnostics can show whether multicast reached this PC.");
        }

        if (device.Platform is FireTvPlatform.Vega)
        {
            return new ModeVerdict(
                default,
                ModeStatus.Impossible,
                "This device runs Vega OS, which is not Android and cannot install a receiver by "
                    + "any method. Flint cannot run on it, and no future version will change that.",
                Remedy: null);
        }

        // Pairing authenticated against the Flint receiver's own port. That is stronger evidence
        // than ADB identity: the receiver is already installed and answering. Keep blocking only
        // when we have not yet proved a live session.
        if (pairedSessionActive)
        {
            return null;
        }

        if (device.Platform.CanInstallReceiver())
        {
            return device.AdbState switch
            {
                AdbConnectionState.Connected => null,
                AdbConnectionState.Unauthorized => new ModeVerdict(
                    default,
                    ModeStatus.Blocked,
                    "The receiver is reachable but has not authorised this PC.",
                    "Accept the authorisation prompt on the television. If none appeared, "
                        + "disconnect and reconnect to make it show again."),
                _ => new ModeVerdict(
                    default,
                    ModeStatus.Blocked,
                    "The device was identified but is not currently reachable over ADB.",
                    "Check that the television is awake and still on this network, then probe again."),
            };
        }

        // Two different failures wear the same blocked badge, and telling them apart is the whole
        // value of this message.
        //
        // A refusal means something is there and actively closed the port — genuinely ambiguous
        // between Vega and debugging switched off, so the developer-options remedy is right.
        //
        // A timeout means nothing answered at all, which is overwhelmingly a wrong or stale
        // address. Claiming "Flint reached" there is false, and sending someone into Developer
        // Options on a television that was never the problem wastes their time on the one failure
        // that is easiest to fix.
        return device.AdbState is AdbConnectionState.Refused
            ? new ModeVerdict(
                default,
                ModeStatus.Blocked,
                $"Something answered at {device.Address} but refused the ADB port. Either this is "
                    + "not an Android device, or ADB debugging is switched off. Flint cannot "
                    + "identify Fire OS or install the receiver until ADB answers.",
                "On the TV open Settings > My Fire TV > About, select the Fire TV Stick name seven "
                    + "times, press Back, open Developer Options, enable ADB debugging, accept the "
                    + "authorisation prompt, then connect again.")
            : new ModeVerdict(
                default,
                ModeStatus.Blocked,
                $"Nothing answered at {device.Address}. The address may be wrong or stale, or the "
                    + "TV may be on a different network or asleep.",
                "Check the address on the TV under Settings > My Fire TV > About > Network, and "
                    + "that it is on this same network. If it matches, wake the TV and probe "
                    + "again.");
    }

    /// <summary>
    /// Builds the verdict for a mode nothing has blocked.
    /// </summary>
    /// <remarks>
    /// Reached only after every gate above has passed, so availability here is a conclusion drawn
    /// from probed capability rather than a claim made in advance.
    /// </remarks>
    private static ModeVerdict Available(CastMode mode, string reason) =>
        new(mode, ModeStatus.Available, reason);
}
