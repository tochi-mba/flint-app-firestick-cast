using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flint.Core.Media;
using Flint.Core.Settings;
using Flint.Protocol;

namespace Flint.Cli;

/// <summary>One measured path: its name, and how long a run of it took.</summary>
/// <param name="Name">What was measured, stable across builds so two reports can be lined up.</param>
/// <param name="MedianMicroseconds">The middle run.</param>
/// <param name="P95Microseconds">The run that 95 in 100 runs beat.</param>
/// <param name="Runs">How many runs were timed.</param>
internal sealed record PerfMetric(string Name, double MedianMicroseconds, double P95Microseconds, int Runs);

/// <summary>
/// <c>flint perf</c>: times the paths Flint runs on every frame, every wire message and every change
/// to the queue, so two builds can be compared on one machine.
/// </summary>
/// <remarks>
/// <para>
/// These are measurements of this machine on this day, not claims about Flint. The report names the
/// machine profile, its processor count, the runtime and the build, and
/// <c>./dev.ps1 perf compare</c> fails only against a baseline from the same machine profile. Capture
/// and encoding are left to the engine's hardware-gated tests, because they cannot be timed honestly
/// without a graphics card; everything here runs anywhere.
/// </para>
/// <para>
/// Each path is warmed up, then timed run by run. The median and the 95th percentile are kept rather
/// than the mean, because one garbage collection inside one run would otherwise move the answer.
/// </para>
/// <para>
/// Every path is timed in <see cref="Rounds"/> rounds, taken in turn, and its best round is kept. A
/// laptop's clock speed moves with heat and power from one minute to the next; a path measured only
/// while the processor was slowed down would otherwise look slower than the code it ran.
/// </para>
/// </remarks>
internal static class PerfCommand
{
    /// <summary>The report's shape. Raised when a field changes meaning, never when one is added.</summary>
    internal const int Schema = 1;

    /// <summary>Times every path, and writes a table, or with <paramref name="json"/> the report.</summary>
    /// <param name="output">Where the table or the report goes.</param>
    /// <param name="progress">Where to say that measuring has begun: stderr with --json, so stdout stays parseable.</param>
    /// <param name="json">Whether to write the machine-readable report instead of the table.</param>
    /// <param name="runs">Runs per path; zero for each path's own count. Tests pass a few.</param>
    /// <param name="warmUp">How long each path runs before it is timed; null for <see cref="WarmUp"/>.</param>
    internal static int Run(TextWriter output, TextWriter progress, bool json, int runs = 0, TimeSpan? warmUp = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(progress);
        progress.WriteLine("  Timing the paths Flint runs most often. This takes about twenty seconds.");
        progress.WriteLine();
        var metrics = Measure(runs, Rounds, warmUp ?? WarmUp);
        output.WriteLine(json ? Render(metrics) : Table(metrics, Build));
        return FlintExitCode.Success;
    }

    /// <summary>How many times every path is timed; the best is kept.</summary>
    internal const int Rounds = 3;

    /// <summary>
    /// How long each path runs before it is timed. The runtime compiles a method again, faster, once
    /// it has been called often enough and a moment has passed; a fixed handful of warm-up runs
    /// sometimes timed the first version and sometimes the second, and one path read ten times
    /// slower from one run to the next.
    /// </summary>
    internal static readonly TimeSpan WarmUp = TimeSpan.FromMilliseconds(300);

    /// <summary>Times every path, in rounds, and keeps each path's best round.</summary>
    /// <param name="runs">Runs per path per round; zero for each path's own count.</param>
    /// <param name="rounds">How many rounds.</param>
    /// <param name="warmUp">How long each path runs before each round of it is timed.</param>
    internal static IReadOnlyList<PerfMetric> Measure(int runs, int rounds, TimeSpan warmUp)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rounds, 1);
        (string Name, Action Path, int Runs)[] paths =
        [
            ("wire.encode-video-frame", WireEncode(), Or(runs, 400)),
            ("wire.decode-video-frame", WireDecode(), Or(runs, 400)),
            ("playlist.add-2000", PlaylistAdd, Or(runs, 50)),
            ("playlist.advance-through-2000", PlaylistAdvance, Or(runs, 50)),
            ("playlist.shuffle-cycle-2000", PlaylistShuffle, Or(runs, 50)),
            ("drop.expand-2000-of-6000-files", DropExpand, Or(runs, 50)),
            ("order.natural-sort-2000-names", NaturalSort, Or(runs, 50)),
            ("types.judge-10000-files", JudgeTypes, Or(runs, 50)),
            ("settings.update-10-times", SettingsUpdate, Or(runs, 400)),
            ("playback.position-1000-ticks", PositionMath, Or(runs, 400)),
        ];

        var best = new PerfMetric?[paths.Length];
        for (var round = 0; round < rounds; round++)
        {
            for (var index = 0; index < paths.Length; index++)
            {
                var measured = Measure(paths[index].Name, paths[index].Path, paths[index].Runs, warmUp);
                best[index] = best[index] is { } kept ? Better(kept, measured) : measured;
            }
        }

        return [.. best.OfType<PerfMetric>()];
    }

    /// <summary>The faster of two rounds of one path, by median; the earlier one when they tie.</summary>
    internal static PerfMetric Better(PerfMetric earlier, PerfMetric later) =>
        later.MedianMicroseconds < earlier.MedianMicroseconds ? later : earlier;

    /// <summary>
    /// Times <paramref name="path"/> over <paramref name="runs"/> runs, after running it for at least
    /// <paramref name="warmUp"/> and at least three times.
    /// </summary>
    internal static PerfMetric Measure(string name, Action path, int runs, TimeSpan warmUp)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(runs, 1);
        var warming = Stopwatch.GetTimestamp();
        for (var warm = 0; warm < 3 || Stopwatch.GetElapsedTime(warming) < warmUp; warm++)
        {
            path();
        }

        var samples = new double[runs];
        for (var run = 0; run < runs; run++)
        {
            var start = Stopwatch.GetTimestamp();
            path();
            samples[run] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
        }

        Array.Sort(samples);
        return new PerfMetric(name, samples[runs / 2], samples[Math.Min(runs - 1, (int)(runs * 0.95))], runs);
    }

    /// <summary>The report, as <c>./dev.ps1 perf</c> reads it.</summary>
    internal static string Render(IReadOnlyList<PerfMetric> metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        var list = new JsonArray();
        foreach (var metric in metrics)
        {
            list.Add(new JsonObject
            {
                ["name"] = metric.Name,
                ["medianMicroseconds"] = Math.Round(metric.MedianMicroseconds, 2),
                ["p95Microseconds"] = Math.Round(metric.P95Microseconds, 2),
                ["runs"] = metric.Runs,
            });
        }

        var root = new JsonObject
        {
            ["schema"] = Schema,
            ["measuredAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["profile"] = Profile,
            ["processors"] = Environment.ProcessorCount,
            ["runtime"] = RuntimeInformation.FrameworkDescription,
            ["build"] = Build,
            ["metrics"] = list,
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>The table a person reads.</summary>
    /// <param name="metrics">What was measured.</param>
    /// <param name="build">Which build measured it, so a Debug build's numbers come with a warning.</param>
    internal static string Table(IReadOnlyList<PerfMetric> metrics, string build)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        var table = new System.Text.StringBuilder();
        table.AppendLine($"  profile {Profile}, {Environment.ProcessorCount} processors, {RuntimeInformation.FrameworkDescription}, {build}");
        table.AppendLine();
        table.AppendLine($"  {"path",-36} {"median",11} {"p95",11}  runs");
        foreach (var metric in metrics)
        {
            table.AppendLine($"  {metric.Name,-36} {metric.MedianMicroseconds,9:F1}us {metric.P95Microseconds,9:F1}us  {metric.Runs}");
        }

        if (build != "Release")
        {
            table.AppendLine();
            table.Append("  This is a Debug build, so these numbers say little. ./dev.ps1 perf measures Release.");
        }

        return table.ToString().TrimEnd();
    }

    /// <summary>Which build is measuring: a Debug build's numbers are not comparable with Release.</summary>
    internal static string Build =>
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    /// <summary>
    /// An opaque, project-specific identity for comparable runs on this PC. The report never writes
    /// the Windows machine name, which can contain a person's or company's name.
    /// </summary>
    internal static string Profile
    {
        get
        {
            var identity = string.Join(
                '\n',
                "flint-perf-v1",
                Environment.MachineName,
                Environment.ProcessorCount,
                RuntimeInformation.OSDescription,
                RuntimeInformation.ProcessArchitecture);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16].ToLowerInvariant();
        }
    }

    /// <summary>The runs asked for, or the path's own count when none were.</summary>
    internal static int Or(int runs, int own) => runs > 0 ? runs : own;

    private static WireFrame Frame() =>
        new(ProtocolVersion.Current, new VideoPacket(1_234_567, KeyFrame: false, BinaryData.From(new byte[64 * 1024])));

    private static Action WireEncode()
    {
        var frame = Frame();
        return () => WireCodec.Encode(frame);
    }

    private static Action WireDecode()
    {
        var bytes = WireCodec.Encode(Frame());
        return () => WireCodec.Decode(bytes);
    }

    private static readonly string[] TwoThousandFiles =
        [.. Enumerable.Range(0, 2_000).Select(i => $@"D:\films\Episode {i}.mp4")];

    private static void PlaylistAdd() => new Playlist().Add(TwoThousandFiles);

    private static void PlaylistAdvance()
    {
        var playlist = new Playlist();
        playlist.Add(TwoThousandFiles);
        while (playlist.Advance(RepeatMode.Off) is not null)
        {
        }
    }

    private static void PlaylistShuffle()
    {
        var playlist = new Playlist(new Random(1)) { Shuffle = true };
        playlist.Add(TwoThousandFiles);
        while (playlist.Advance(RepeatMode.Off) is not null)
        {
        }
    }

    private static void DropExpand() =>
        DropExpander.Expand([@"D:\films"], SyntheticDisk.Instance, includeSubfolders: true, DropOrder.ByName);

    /// <summary>As many names as one drop queues, many sharing their start, as a season of episodes does.</summary>
    private static readonly string[] TwoThousandNames =
        [.. Enumerable.Range(0, 2_000).Select(i => $"Episode {i % 500} take {i}.mp4")];

    private static void NaturalSort() => _ = SortNames(TwoThousandNames, NaturalOrder.Instance);

    /// <summary>Forces a complete sort; kept separate so a test proves the comparer is exercised.</summary>
    internal static string[] SortNames(IEnumerable<string> names, IComparer<string> comparer) =>
        [.. names.Order(comparer)];

    private static readonly string[] TenThousandPaths =
        [.. Enumerable.Range(0, 10_000).Select(i => $@"D:\mixed\file {i}{(i % 3) switch { 0 => ".mkv", 1 => ".txt", _ => ".JPG" }}")];

    private static void JudgeTypes()
    {
        foreach (var path in TenThousandPaths)
        {
            _ = MediaFileTypes.For(path);
        }
    }

    private static void SettingsUpdate()
    {
        using var settings = new SettingsService(new InMemoryAppSettingsStore(), delay: (_, _) => Task.CompletedTask);
        for (var step = 1; step <= 10; step++)
        {
            var percent = step;
            settings.Update(current => current with { Media = current.Media with { VolumeStepPercent = percent } });
        }
    }

    private static void PositionMath()
    {
        var arrived = DateTimeOffset.UnixEpoch;
        var snapshot = new PlaybackSnapshot(PlaybackPhase.Playing, 10_000, 3_600_000, "", arrived);
        for (var tick = 0; tick < 1_000; tick++)
        {
            _ = snapshot.PositionAt(arrived.AddMilliseconds(tick * 250));
        }
    }

    /// <summary>
    /// Twenty folders of three hundred files, a third of them something other than media, made of
    /// names only, so the walk is timed rather than the disk.
    /// </summary>
    internal sealed class SyntheticDisk : IMediaFileSystem
    {
        internal const string Root = @"D:\films";

        public static SyntheticDisk Instance { get; } = new();

        public bool IsFolder(string path) => path == Root;

        public MediaFileFacts? Facts(string path) => new(1, DateTimeOffset.UnixEpoch);

        public bool IsHiddenOrSystem(string path) => false;

        public IEnumerable<string> FilesIn(string folder) =>
            folder == Root
                ? []
                : Enumerable.Range(0, 300).Select(i => $@"{folder}\Episode {i}{(i % 3 == 2 ? ".nfo" : ".mp4")}");

        public IEnumerable<string> FoldersIn(string folder) =>
            folder == Root ? Enumerable.Range(0, 20).Select(i => $@"{Root}\Season {i}") : [];
    }
}
