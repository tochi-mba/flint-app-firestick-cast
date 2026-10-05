using System.Text.Json;
using Shouldly;

namespace Flint.Cli.Tests;

/// <summary>`flint perf`: what it accepts, what it measures, and the report `./dev.ps1 perf` reads.</summary>
public sealed class PerfCommandTests
{
    [Fact]
    public void PerfIsAWord_AndTakesOnlyJson()
    {
        CliOptions.TryParse(["perf"], out var options, out _).ShouldBeTrue();
        options!.Verb.ShouldBe(CliVerb.Perf);
        options.Json.ShouldBeFalse();

        CliOptions.TryParse(["perf", "--json"], out options, out _).ShouldBeTrue();
        options!.Json.ShouldBeTrue();

        CliOptions.TryParse(["perf", "--address", "10.0.0.8"], out _, out var error).ShouldBeFalse();
        error.ShouldBe("'perf' takes only --json.");
    }

    [Fact]
    public void TheReport_NamesTheMachineAndBuild_AndEveryPath()
    {
        using var output = new StringWriter();
        using var progress = new StringWriter();

        PerfCommand.Run(output, progress, json: true, runs: 1, warmUp: TimeSpan.Zero).ShouldBe(FlintExitCode.Success);

        progress.ToString().ShouldContain("Timing the paths Flint runs most often");
        using var report = JsonDocument.Parse(output.ToString());
        var root = report.RootElement;
        root.GetProperty("schema").GetInt32().ShouldBe(PerfCommand.Schema);
        root.GetProperty("profile").GetString().ShouldBe(PerfCommand.Profile);
        root.TryGetProperty("machine", out _).ShouldBeFalse("a report that may be committed must not expose the PC name");
        root.GetProperty("processors").GetInt32().ShouldBe(Environment.ProcessorCount);
        root.GetProperty("runtime").GetString().ShouldStartWith(".NET");
        root.GetProperty("build").GetString().ShouldBe(PerfCommand.Build);
        var metrics = root.GetProperty("metrics").EnumerateArray().ToList();
        metrics.Select(metric => metric.GetProperty("name").GetString()).ShouldBe(
        [
            "wire.encode-video-frame",
            "wire.decode-video-frame",
            "playlist.add-2000",
            "playlist.advance-through-2000",
            "playlist.shuffle-cycle-2000",
            "drop.expand-2000-of-6000-files",
            "order.natural-sort-2000-names",
            "types.judge-10000-files",
            "settings.update-10-times",
            "playback.position-1000-ticks",
        ]);
        metrics.ShouldAllBe(metric =>
            metric.GetProperty("medianMicroseconds").GetDouble() > 0
            && metric.GetProperty("p95Microseconds").GetDouble() >= metric.GetProperty("medianMicroseconds").GetDouble()
            && metric.GetProperty("runs").GetInt32() == 1);
    }

    [Fact]
    public void TheTable_IsForAPerson_AndSaysWhenTheBuildIsNotRelease()
    {
        using var output = new StringWriter();
        using var progress = new StringWriter();

        PerfCommand.Run(output, progress, json: false, runs: 1, warmUp: TimeSpan.Zero);

        var table = output.ToString();
        table.ShouldContain($"profile {PerfCommand.Profile}");
        table.ShouldNotContain(Environment.MachineName);
        table.ShouldContain("wire.encode-video-frame");
        var one = new[] { new PerfMetric("a.path", 12.5, 20, 3) };
        PerfCommand.Table(one, "Debug").ShouldContain("This is a Debug build");
        PerfCommand.Table(one, "Release").ShouldNotContain("Debug");
        PerfCommand.Table(one, "Release").ShouldContain("a.path");
    }

    [Fact]
    public void OfSeveralRounds_TheFasterIsKept_AndATieKeepsTheFirst()
    {
        var slow = new PerfMetric("a", 30, 40, 5);
        var fast = new PerfMetric("a", 10, 90, 5);
        var tie = new PerfMetric("a", 30, 10, 5);

        PerfCommand.Better(slow, fast).ShouldBeSameAs(fast);
        PerfCommand.Better(fast, slow).ShouldBeSameAs(fast);
        PerfCommand.Better(slow, tie).ShouldBeSameAs(slow);
        Should.Throw<ArgumentOutOfRangeException>(() => PerfCommand.Measure(runs: 1, rounds: 0, TimeSpan.Zero));
    }

    [Fact]
    public void APath_RunsForTheWholeWarmUp_BeforeItIsTimed()
    {
        var calls = 0;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();

        PerfCommand.Measure("warm", () => calls++, runs: 1, TimeSpan.FromMilliseconds(40));

        System.Diagnostics.Stopwatch.GetElapsedTime(started).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(40));
        calls.ShouldBeGreaterThan(4, "three warm-ups at the least, and as many more as fit in the time");
        PerfCommand.WarmUp.ShouldBe(TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public void EachPath_KeepsItsOwnRunCount_UnlessFewerAreAskedFor()
    {
        PerfCommand.Or(0, 50).ShouldBe(50);
        PerfCommand.Or(3, 50).ShouldBe(3);
    }

    [Fact]
    public void TheDropBenchmark_OpensSixThousandFiles_OfWhichFourThousandAreMedia()
    {
        var disk = PerfCommand.SyntheticDisk.Instance;
        var folders = disk.FoldersIn(PerfCommand.SyntheticDisk.Root).ToList();
        var files = folders.SelectMany(disk.FilesIn).ToList();

        disk.IsFolder(PerfCommand.SyntheticDisk.Root).ShouldBeTrue();
        disk.FilesIn(PerfCommand.SyntheticDisk.Root).ShouldBeEmpty();
        disk.FoldersIn(folders[0]).ShouldBeEmpty();
        files.Count.ShouldBe(6_000);
        files.Count(Flint.Core.Media.MediaFileTypes.IsPlayable).ShouldBe(4_000, "so the 2,000 cap is reached");
        disk.IsHiddenOrSystem(files[0]).ShouldBeFalse();
        disk.Facts(files[0]).ShouldNotBeNull();
    }

    [Fact]
    public void Measuring_KeepsTheMiddleAndTheSlowTail_NotTheAverage()
    {
        var run = 0;
        var metric = PerfCommand.Measure("steps", () => Thread.SpinWait(++run > 21 ? 2_000_000 : 1), runs: 20, TimeSpan.Zero);

        metric.Name.ShouldBe("steps");
        metric.Runs.ShouldBe(20);
        metric.P95Microseconds.ShouldBeGreaterThan(metric.MedianMicroseconds, "three warm-up runs, then twenty timed: only the last two are slow");
        Should.Throw<ArgumentOutOfRangeException>(() => PerfCommand.Measure("none", () => { }, runs: 0, TimeSpan.Zero));
    }

    [Fact]
    public void TheNaturalSortMeasurement_ActuallyRunsTheComparer()
    {
        var comparisons = 0;
        var comparer = Comparer<string>.Create((left, right) =>
        {
            comparisons++;
            return StringComparer.Ordinal.Compare(left, right);
        });

        var sorted = PerfCommand.SortNames(["c", "a", "b"], comparer);

        sorted.ShouldBe(["a", "b", "c"]);
        comparisons.ShouldBeGreaterThan(0);
    }
}
