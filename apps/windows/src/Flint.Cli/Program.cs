using System.Diagnostics;
using Flint.App.Services;
using Flint.Core;
using Flint.Discovery;
using Flint.Engine.Interop;
using Flint.Protocol;
using Flint.Session;

namespace Flint.Cli;

/// <summary>
/// Runs the capability probe and prints the report.
/// </summary>
/// <remarks>
/// The same probe the Cast page runs, without a window. It exists so the report can be captured
/// into a bug thread or a support message, and so the probe can be exercised on a machine with no
/// display session.
/// </remarks>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var timeout = TimeSpan.FromSeconds(60);

        // The banner and several verdicts use characters outside the console's legacy code page,
        // which otherwise render as replacement marks. Set on a best-effort basis: a redirected or
        // unusual stdout can refuse, and a mangled separator is not worth failing a probe over.
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch (IOException)
        {
        }

        // Parsed before anything is printed, because --json decides which stream the banner goes
        // to. A caller piping stdout into a parser should not have to strip decoration first.
        var wantsJson = args.Any(argument => argument.Equals("--json", StringComparison.OrdinalIgnoreCase));
        var banner = wantsJson ? Console.Error : Console.Out;
        banner.WriteLine();
        banner.WriteLine("  REX TECHNOLOGIES · FLINT");

        if (!CliOptions.TryParse(args, out var options, out var argumentError))
        {
            Console.WriteLine($"  {argumentError}");
            Console.WriteLine();
            PrintUsage();
            return FlintExitCode.UsageError;
        }

        if (options!.ShowVersion)
        {
            Console.WriteLine(FlintVersion.Describe());
            return FlintExitCode.Success;
        }

        if (options.ShowHelp)
        {
            PrintUsage();
            return FlintExitCode.Success;
        }

        // A probe has a deadline; a live session does not — it runs until Ctrl+C.
        var runsUntilStopped = options.MediaPath is not null || options.Mirror || options.BrowseUrl is not null;
        using var cts = new CancellationTokenSource(runsUntilStopped ? Timeout.InfiniteTimeSpan : timeout);

        if (options.ScanServices)
        {
            Console.WriteLine("  Multicast service scan");
            Console.WriteLine();
            await ScanServicesAsync(cts.Token).ConfigureAwait(false);
            return FlintExitCode.Success;
        }

        // A browse run that was given both an address and a browser port needs nothing the probe
        // produces. Probing anyway spent about fifteen seconds before the page opened and printed
        // an ADB verdict about a port that was never ADB — noise on top of a delay, in the one mode
        // where the person is waiting to see something appear on a television.
        if (options.BrowseUrl is { } directUrl
            && options.PairingCode is { } directPairingCode
            && options.Endpoint is { } directEndpoint
            && options.BrowserPort is { } directBrowserPort)
        {
            return await BrowseRunner.RunAsync(
                    directEndpoint.Address,
                    directBrowserPort,
                    directPairingCode,
                    directUrl,
                    cts.Token)
                .ConfigureAwait(false);
        }

        // Progress goes wherever the banner went. With --json, stdout must carry the object and
        // nothing else — a heading above it makes the whole stream unparseable.
        banner.WriteLine("  Capability probe");
        banner.WriteLine();

        var deviceProbe = new FireTvDeviceProbe();
        var prober = new CapabilityProber(
            new EngineHostProbe(new WindowsHostProbe()),
            deviceProbe,
            new TcpNetworkProbe());

        var stopwatch = Stopwatch.StartNew();
        CapabilityReport report;
        try
        {
            report = options.Endpoint is { } endpoint
                ? await prober.ProbeAddressAsync(
                    endpoint.Address,
                    endpoint.Port,
                    cts.Token).ConfigureAwait(false)
                : await prober.ProbeAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            banner.WriteLine($"  The probe did not finish within {timeout.TotalSeconds:F0} seconds.");
            return FlintExitCode.ProbeTimedOut;
        }

        if (options.Json)
        {
            // The whole verdict as one object. Everything a script would otherwise have to scrape
            // back out of the prose above.
            Console.WriteLine(CapabilityReportJson.Render(report, stopwatch.Elapsed));
            return FlintExitCode.Success;
        }

        PrintHost(report.Host);
        PrintDevice(report.Device);
        PrintPath(report.Path);
        PrintVerdicts(report);

        if (options.BrowseUrl is { } browseUrl
            && options.PairingCode is { } browsePairingCode
            && report.Device is { } browseDevice)
        {
            // The receiver binds its browser listener to an ephemeral port and advertises it, so
            // there is no default to fall back on. Saying so is better than guessing a port and
            // reporting a connection refusal that names the wrong cause.
            // An explicit port wins, because the manual-address path never reads the mDNS record
            // that carries the advertised one — and a scripted run against a known receiver should
            // not have to depend on discovery working.
            if ((options.BrowserPort ?? browseDevice.BrowserEvidence?.SecureEndpointPort) is not { } browserPort)
            {
                Console.WriteLine();
                Console.WriteLine("  This receiver is not advertising a browser endpoint.");
                Console.WriteLine("  Open the Flint receiver on the TV and make sure it reports a browser port.");
                return FlintExitCode.ReceiverUnavailable;
            }

            // Its own session, deliberately. The browser channel is separately authenticated and
            // certificate-pinned; sharing the cast session's trust would make one compromise reach
            // further than it should.
            return await BrowseRunner.RunAsync(
                    browseDevice.Address,
                    browserPort,
                    browsePairingCode,
                    browseUrl,
                    cts.Token)
                .ConfigureAwait(false);
        }

        // Pairing by code never needed ADB: the receiver's own handshake accepts or rejects the
        // code independently of whether adb authorized this host. Gating on IsReachable here would
        // refuse the exact case the manual-address flow exists for — a TV whose adbd is offline or
        // unauthorized but whose receiver app is reachable and showing a code on screen.
        if (options.PairingCode is { } pairingCode && report.Device is { } device)
        {
            await using var session = await CastSession.ConnectAsync(
                device.Address,
                options.ReceiverPort,
                pairingCode,
                cancellationToken: cts.Token).ConfigureAwait(false);
            Console.WriteLine();
            Console.WriteLine($"  Receiver session: {session.VideoCodec} connected");

            if (options.MediaPath is { } mediaPath)
            {
                await PlayMediaAsync(session, mediaPath, cts.Token).ConfigureAwait(false);
                return FlintExitCode.Success;
            }

            if (options.Mirror)
            {
                return await MirrorScreenAsync(session, options.MirrorMaxWidth, cts.Token).ConfigureAwait(false);
            }
        }

        Console.WriteLine($"  Probed in {stopwatch.Elapsed.TotalSeconds:F1} s.");
        Console.WriteLine();

        // A report always succeeds. "Nothing found" is an answer, not a failure.
        return FlintExitCode.Success;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("  Usage:");
        Console.WriteLine("    flint                         Discover and probe an advertised Fire TV");
        Console.WriteLine("    flint --address <ip>          Probe one address across ADB ports 5555–5585");
        Console.WriteLine("    flint --address <ip> --port <port>");
        Console.WriteLine("                                  Probe one explicit ADB endpoint");
        Console.WriteLine("    flint --services              List multicast service types");
        Console.WriteLine("    flint --address <ip> --pairing-code <code>");
        Console.WriteLine("                                  Connect to a running Flint receiver");
        Console.WriteLine("    flint --address <ip> --port <receiver-port> --pairing-code <code>");
        Console.WriteLine("                                  Connect on a non-default receiver port");
        Console.WriteLine("    flint --address <ip> --pairing-code <code> --media <path>");
        Console.WriteLine("                                  Play a local file and keep serving it until Ctrl+C");
        Console.WriteLine("    flint --address <ip> --pairing-code <code> --mirror [--mirror-width <px>]");
        Console.WriteLine("                                  Mirror this screen until Ctrl+C");
        Console.WriteLine("    flint --address <ip> --pairing-code <code> --browse <https url>");
        Console.WriteLine("                                  Open a page in the TV's own browser until Ctrl+C");
        Console.WriteLine("    flint ... --browse <https url> --browser-port <port>");
        Console.WriteLine("                                  Use an explicit browser port instead of the advertised one");
        Console.WriteLine();
        Console.WriteLine("  Output:");
        Console.WriteLine("    --json                        Print the probe verdict as JSON on stdout");
        Console.WriteLine("                                  (the banner moves to stderr, so stdout pipes cleanly)");
        Console.WriteLine("    --version                     Print the version and exit");
        Console.WriteLine("    --help, -h                    Print this and exit");
        Console.WriteLine();
        Console.WriteLine("  Exit codes:");
        Console.WriteLine($"    {FlintExitCode.Success,-3} success");
        Console.WriteLine($"    {FlintExitCode.ProbeTimedOut,-3} the probe did not finish in time");
        Console.WriteLine($"    {FlintExitCode.MirrorProducedNoFrames,-3} the mirror encoded no frames");
        Console.WriteLine($"    {FlintExitCode.MirrorUnsupported,-3} this PC cannot mirror");
        Console.WriteLine($"    {FlintExitCode.UsageError,-3} bad command line");
        Console.WriteLine($"    {FlintExitCode.ReceiverUnavailable,-3} the receiver is not offering that service");
        Console.WriteLine();
    }

    /// <summary>
    /// Mirrors this screen onto a connected receiver until Ctrl+C.
    /// </summary>
    /// <remarks>
    /// This is the path that proves the host half of a mirror on real hardware, so it prints what
    /// the engine actually produced rather than only whether it finished — a mirror that reports
    /// success while sending nothing is the failure worth catching here.
    /// </remarks>
    private static async Task<int> MirrorScreenAsync(
        CastSession session,
        uint maxWidth,
        CancellationToken cancellationToken)
    {
        var runner = new ScreenMirrorRunner(new NativeMirrorEngine());
        var lastReported = 0L;
        runner.StatsUpdated += stats =>
        {
            // Every thirtieth frame, so a minute of mirroring is a readable log rather than
            // eighteen hundred lines.
            if (stats.FramesEncoded - lastReported < 30)
            {
                return;
            }

            lastReported = stats.FramesEncoded;
            Console.WriteLine(
                $"  {stats.FramesEncoded} frames, {stats.BytesEncoded / 1024} KiB, "
                + $"{stats.FramesUnchanged} idle ticks, {stats.Recoveries} recoveries");
        };

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stop.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            Console.WriteLine($"  Mirroring this screen (capped at {maxWidth}px wide). Ctrl+C to stop.");
            var stats = await runner.RunAsync(
                session,
                new MirrorSessionOptions(MaxWidth: maxWidth),
                Environment.MachineName,
                stop.Token).ConfigureAwait(false);

            Console.WriteLine();
            Console.WriteLine($"  Mirror stopped: {stats.FramesEncoded} frames, {stats.BytesEncoded / 1024} KiB sent.");
            Console.WriteLine();

            // Zero frames is a failure even though nothing threw: the television saw a mirror
            // surface and never a picture, which is exactly the outcome that must not read as
            // success.
            if (stats.FramesEncoded == 0)
            {
                Console.WriteLine("  No frames were encoded, so the television never showed anything.");
                return FlintExitCode.MirrorProducedNoFrames;
            }

            return FlintExitCode.Success;
        }
        catch (MirrorEngineException exception)
        {
            Console.WriteLine();
            Console.WriteLine($"  This PC cannot mirror: {exception.Message}");
            Console.WriteLine();
            return FlintExitCode.MirrorUnsupported;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static async Task PlayMediaAsync(CastSession session, string mediaPath, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(mediaPath);
        var mimeType = GetMimeType(mediaPath);

        // Pushed over the already-open control connection rather than fetched by the receiver over
        // HTTP: several Fire OS builds silently drop an outbound connection the receiver app itself
        // opens to a private LAN address, even though this exact connection — opened by this host —
        // works fine. Kept well ahead of the receiver's own 30s decode timeout so its own, more
        // specific error wins the race if playback still fails after the file arrives.
        using var playbackTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        Console.WriteLine($"  Sending {fileName} to the receiver...");
        var lastReported = -1;
        var progress = new Progress<double>(fraction =>
        {
            var percent = (int)(fraction * 100);
            if (percent == lastReported)
            {
                return;
            }

            lastReported = percent;
            Console.WriteLine($"  Pushed {percent}%");
        });

        var playback = await session.PushMediaAndWaitForPlaybackStartAsync(
            mediaPath,
            fileName,
            mimeType,
            progress: progress,
            cancellationToken: playbackTimeout.Token).ConfigureAwait(false);
        if (playback.State is not PlaybackState.Playing)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(playback.Detail)
                    ? "The receiver did not start playback."
                    : $"The receiver could not play the media: {playback.Detail}");
        }

        Console.WriteLine($"  Receiver confirmed playback: {playback.Detail}");
        Console.WriteLine("  Keeping the connection open until Ctrl+C (closing it stops playback).");
        var cancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.TrySetResult();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            await cancellation.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static string GetMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp4" => "video/mp4",
        ".mkv" => "video/x-matroska",
        ".webm" => "video/webm",
        ".mp3" => "audio/mpeg",
        ".m4a" => "audio/mp4",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        _ => "application/octet-stream",
    };

    /// <summary>
    /// Lists every service type advertised on the local link.
    /// </summary>
    /// <remarks>
    /// The diagnostic that separates "the television is off or on another network" from "multicast
    /// is not reaching this machine at all". An empty list means the second, which is usually
    /// client isolation on the access point or a firewall rule rather than anything about Fire TV.
    /// </remarks>
    private static async Task ScanServicesAsync(CancellationToken cancellationToken)
    {
        Section("INTERFACES QUERIED");
        foreach (var address in MulticastServiceScanner.LocalIPv4Addresses())
        {
            Console.WriteLine($"  {address}");
        }

        Console.WriteLine();

        var types = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var gate = new Lock();

        await MulticastServiceScanner.QueryAsync(
            MulticastDnsCodec.ServiceEnumerationType,
            TimeSpan.FromSeconds(6),
            datagram =>
            {
                try
                {
                    var targets = MulticastDnsCodec.ReadPointerTargets(datagram.Span);
                    lock (gate)
                    {
                        foreach (var target in targets)
                        {
                            types.Add(target);
                        }
                    }
                }
                catch (DnsFormatException)
                {
                    // Not readable as DNS. Another responder's business, not Flint's.
                }
            },
            cancellationToken).ConfigureAwait(false);

        Section("SERVICE TYPES SEEN");
        if (types.Count == 0)
        {
            Console.WriteLine("  Nothing answered.");
            Console.WriteLine();
            Console.WriteLine(Wrap(
                "No multicast responder replied at all. That usually means client isolation on the "
                + "access point, a firewall blocking UDP 5353, or a guest network — not that the "
                + "television is missing. Check that this PC and the television are on the same "
                + "SSID, and that it is not a guest network.",
                "  "));
        }
        else
        {
            foreach (var type in types)
            {
                var isFireTv = type.Contains("amzn", StringComparison.OrdinalIgnoreCase);
                Console.WriteLine($"  {(isFireTv ? "*" : " ")} {type}");
            }

            Console.WriteLine();
            Console.WriteLine("  Entries marked * are Amazon services.");
        }

        Console.WriteLine();
    }

    private static void PrintHost(HostCapabilities host)
    {
        Section("THIS PC");
        Row("Windows build", host.WindowsBuild.ToString());
        Row("Graphics adapters", host.Adapters.Count.ToString());
        foreach (var adapter in host.Adapters)
        {
            Row("  " + adapter.Description, adapter.DrivesDisplay ? "drives a display" : "no display");
        }

        Row("Hybrid graphics", host.IsHybridGraphics ? "yes" : "no");
        Row(
            "Screen capture",
            host.ScreenCaptureBackend is { } backend
                ? backend switch
                {
                    CaptureApi.DesktopDuplication => "Desktop Duplication",
                    CaptureApi.WindowsGraphicsCapture => "Windows Graphics Capture",
                    _ => backend.ToString(),
                }
                : "unavailable on this host");
        Row(
            "Hardware encoders",
            host.EncodersProbed ? $"{host.Encoders.Count} found" : "not probed (needs flint-engine)");
        Console.WriteLine();
    }

    private static void PrintDevice(FireTvDevice? device)
    {
        Section("RECEIVER");
        if (device is null)
        {
            Row("Found", "nothing advertised itself");
            Console.WriteLine();
            return;
        }

        Row("Name", device.FriendlyName);
        Row("Address", device.AdbPort is { } port ? $"{device.Address}:{port}" : device.Address.ToString());
        Row("Model", device.Model ?? "not reported");
        Row("Platform", device.Platform.ToDisplayLabel());
        Row("Android API", device.AndroidApiLevel?.ToString() ?? "not reported");
        Row("Android release", device.AndroidRelease ?? "not reported");
        Row("ADB", device.AdbState.ToString());
        Console.WriteLine();
    }

    private static void PrintPath(NetworkPath? path)
    {
        Section("NETWORK PATH");
        if (path is null)
        {
            Row("Measured", "no — nothing reachable to measure against");
            Console.WriteLine();
            return;
        }

        Row("Round trip", $"{path.RoundTripMs:F1} ms");
        Row("Jitter", $"{path.JitterMs:F1} ms");
        Row("Throughput", path.ThroughputLabel);
        Row("Loss", $"{path.PacketLossPercent:F0}%");
        Console.WriteLine();
    }

    private static void PrintVerdicts(CapabilityReport report)
    {
        Section("WHAT THIS PAIR CAN DO");
        foreach (var verdict in report.Verdicts)
        {
            Console.WriteLine($"  {verdict.Mode,-13} {verdict.Status.ToString().ToUpperInvariant()}");
            Console.WriteLine(Wrap(verdict.Reason, "      "));
            if (verdict.Remedy is { } remedy)
            {
                Console.WriteLine(Wrap("What to do: " + remedy, "      "));
            }

            Console.WriteLine();
        }
    }

    private static void Section(string title)
    {
        Console.WriteLine($"  {title}");
        Console.WriteLine("  " + new string('-', title.Length));
    }

    private static void Row(string label, string value) =>
        Console.WriteLine($"  {label,-26}  {value}");

    /// <summary>Wraps prose to a readable width so a long verdict stays legible in a terminal.</summary>
    private static string Wrap(string text, string indent, int width = 88)
    {
        var lines = new List<string>();
        var current = indent;

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.Length + word.Length + 1 > width && current.Length > indent.Length)
            {
                lines.Add(current);
                current = indent;
            }

            current += current.Length > indent.Length ? " " + word : word;
        }

        if (current.Length > indent.Length)
        {
            lines.Add(current);
        }

        return string.Join(Environment.NewLine, lines);
    }
}
