using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using Flint.Core.Media;
using Flint.Protocol;
using Flint.Session;

namespace Flint.Hardware.E2E;

/// <summary>
/// A session with the real TV for one hardware test: paired from what the TV shows, every
/// playback report it sends kept with when it arrived, and a report and screenshots written
/// under artifacts/hardware when the test ends.
/// </summary>
internal sealed class PairedTv : IAsyncDisposable
{
    private readonly ConcurrentQueue<(TimeSpan At, PlaybackStateMessage Report)> reports = new();
    private readonly List<string> notes = [];
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly CancellationTokenSource timeout = new(TimeSpan.FromMinutes(10));

    private PairedTv(string serial, CastSession session, string artifacts)
    {
        Serial = serial;
        Session = session;
        Artifacts = artifacts;
        session.PlaybackStateReceived += report => reports.Enqueue((clock.Elapsed, report));
    }

    public string Serial { get; }

    public CastSession Session { get; }

    public string Artifacts { get; }

    public CancellationToken Token => timeout.Token;

    /// <summary>Every report so far, oldest first.</summary>
    public IReadOnlyList<(TimeSpan At, PlaybackStateMessage Report)> Reports => [.. reports];

    /// <summary>Launches the TV's receiver, reads its code from the screen and pairs with it.</summary>
    public static async Task<PairedTv> OpenAsync(string name)
    {
        if (!HardwareGate.IsEnabled)
        {
            throw new InvalidOperationException(HardwareGate.SkipReason);
        }

        var serial = HardwareGate.Serial ?? throw new InvalidOperationException(HardwareGate.SkipReason);
        using var setup = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await AdbTvSurfaceReader.LaunchInstalledReceiverAsync(serial, setup.Token);
        await Task.Delay(TimeSpan.FromSeconds(3), setup.Token);
        var facts = await AdbTvSurfaceReader.ReadAsync(serial, setup.Token);
        var session = await CastSession.ConnectAsync(
            IPAddress.Parse(facts.Address),
            facts.ReceiverPort,
            facts.PairingCode,
            "Flint hardware test",
            setup.Token);
        var artifacts = Path.Combine(
            CastPairHardwareFlowTests.FindProjectRoot(),
            "artifacts",
            "hardware",
            $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(artifacts);
        var tv = new PairedTv(serial, session, artifacts);
        tv.Note($"Paired with {facts.Address}:{facts.ReceiverPort}.");
        return tv;
    }

    /// <summary>Sends a file and waits for the TV to start it or refuse it; null when it says neither in time.</summary>
    public async Task<PlaybackStateMessage?> PlayAsync(string path, long startPositionMs = 0, long durationMs = -1)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(Token);
        wait.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            return await Session.PushMediaAndWaitForPlaybackStartAsync(
                path,
                Path.GetFileName(path),
                MediaFileTypes.For(path).MimeType,
                durationMs: durationMs,
                startPositionMs: startPositionMs,
                cancellationToken: wait.Token);
        }
        catch (OperationCanceledException) when (!Token.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>The first report after <paramref name="after"/> that <paramref name="matches"/>, or null in time.</summary>
    public async Task<PlaybackStateMessage?> WaitForAsync(
        Func<PlaybackStateMessage, bool> matches,
        TimeSpan within,
        TimeSpan? after = null)
    {
        var from = after ?? clock.Elapsed;
        var deadline = clock.Elapsed + within;
        while (clock.Elapsed < deadline)
        {
            foreach (var (at, report) in reports)
            {
                if (at >= from && matches(report))
                {
                    return report;
                }
            }

            await Task.Delay(100, Token);
        }

        return null;
    }

    /// <summary>Now, on this session's clock, for <see cref="WaitForAsync"/>.</summary>
    public TimeSpan Now => clock.Elapsed;

    /// <summary>Tells the TV to drop what it is playing.</summary>
    public async Task ClearAsync()
    {
        await Session.SendMediaAsync(new MediaCommandMessage(MediaAction.Clear), Token);
        await Task.Delay(TimeSpan.FromSeconds(1), Token);
    }

    /// <summary>Saves what the TV shows as <paramref name="name"/>.png beside the report.</summary>
    public Task ScreenshotAsync(string name) =>
        AdbTvSurfaceReader.ScreenshotAsync(Serial, Path.Combine(Artifacts, name + ".png"), Token);

    /// <summary>Adds a line to the report.</summary>
    public void Note(string line)
    {
        notes.Add(string.Create(CultureInfo.InvariantCulture, $"{clock.Elapsed.TotalSeconds,7:F1}s  {line}"));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Session.SendMediaAsync(new MediaCommandMessage(MediaAction.Clear), CancellationToken.None);
        }
        catch (IOException)
        {
        }

        await Session.DisposeAsync();
        await File.WriteAllLinesAsync(Path.Combine(Artifacts, "report.txt"), notes, CancellationToken.None);
        timeout.Dispose();
    }
}
