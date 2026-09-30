using System.Diagnostics;
using System.Text;
using Flint.App.Services;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// Runs <see cref="DevFileLogTests"/> alone. They swap the process-wide <see cref="Trace"/>
/// listener and assert on exact file contents and sizes, so another class's breadcrumbs must not
/// land in their file mid-test.
/// </summary>
[CollectionDefinition(nameof(DevFileLogIsolation), DisableParallelization = true)]
public sealed class DevFileLogIsolation;

[Collection(nameof(DevFileLogIsolation))]
public sealed class DevFileLogTests : IDisposable
{
    private static readonly TimeSpan DeadlockTimeout = TimeSpan.FromSeconds(10);

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "flint-devfilelog-tests",
        Guid.NewGuid().ToString("N"));
    private readonly string path;

    public DevFileLogTests()
    {
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "windows-latest.log");
        DevFileLog.ResetForTests();
    }

    public void Dispose()
    {
        DevFileLog.ResetForTests();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void IsAvaloniaBindingNoise_FiltersKnownBindingSpam()
    {
        DevFileLog.IsAvaloniaBindingNoise("An error occurred binding ... [Binding] ...").ShouldBeTrue();
        DevFileLog.IsAvaloniaBindingNoise("Value is null.").ShouldBeTrue();
        DevFileLog.IsAvaloniaBindingNoise("FlintDiag|INFO|FlintUi|ok").ShouldBeFalse();
    }

    [Fact]
    public void TryParseDiag_AcceptsFlintDiagLinesOnly()
    {
        DevFileLog.TryParseDiag(
            "FlintDiag|WARN|FlintTrust|tls rejected",
            out var level,
            out var tag,
            out var body).ShouldBeTrue();
        level.ShouldBe("WARN");
        tag.ShouldBe("FlintTrust");
        body.ShouldBe("tls rejected");

        DevFileLog.TryParseDiag("ordinary trace", out _, out _, out _).ShouldBeFalse();
        DevFileLog.TryParseDiag("FlintDiag|INFO|only-two", out _, out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void StartingAgainWhileRunning_KeepsTheFirstFile()
    {
        var second = Path.Combine(directory, "second.log");
        DevFileLog.StartForTests(path, maximumBytes: 64 * 1024, retainBytes: 32 * 1024);

        DevFileLog.StartForTests(second, maximumBytes: 64 * 1024, retainBytes: 32 * 1024);
        DevFileLog.Info("FlintBrowser", "after-second-start");

        File.Exists(second).ShouldBeFalse();
        ReadShared(path).ShouldContain("after-second-start");
    }

    [Fact]
    public void Start_WritesSessionHeaderAndInfoLines()
    {
        DevFileLog.StartForTests(path, maximumBytes: 64 * 1024, retainBytes: 32 * 1024);
        DevFileLog.Info("FlintBrowser", "command Open");
        DevFileLog.Warn("FlintCast", "probe failed");
        DevFileLog.Error("FlintTrust", "connect failed");

        var text = ReadShared(path);
        text.ShouldContain("[INFO] DevFileLog: session start");
        text.ShouldContain("[INFO] FlintBrowser: command Open");
        text.ShouldContain("[WARN] FlintCast: probe failed");
        text.ShouldContain("[ERROR] FlintTrust: connect failed");
        text.ShouldContain("maxBytes=");
        text.ShouldContain("retainBytes=");
    }

    [Fact]
    public void TraceListener_RoutesFlintDiagAndDropsBindingNoise()
    {
        DevFileLog.StartForTests(path, maximumBytes: 64 * 1024, retainBytes: 32 * 1024);
        Trace.WriteLine("FlintDiag|INFO|FlintSession|surface ready for Browser");
        Trace.WriteLine("An error occurred binding Property 'X' [Binding]");
        Trace.WriteLine("harmless non-diag line");

        var text = ReadShared(path);
        text.ShouldContain("[INFO] FlintSession: surface ready for Browser");
        text.ShouldContain("[TRACE] harmless non-diag line");
        text.ShouldNotContain("[Binding]");
        text.ShouldNotContain("An error occurred binding");
    }

    [Fact]
    public void Write_TrimsOldestWhenFileExceedsMaximum()
    {
        const long maximumBytes = 2500;
        const long retainBytes = 1200;
        DevFileLog.StartForTests(path, maximumBytes, retainBytes);

        for (var i = 0; i < 120; i++)
        {
            DevFileLog.Info("FlintBrowser", $"OLD-{i:D3}-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");
        }

        for (var i = 0; i < 20; i++)
        {
            DevFileLog.Info("FlintBrowser", $"NEW-{i:D3}-keep-newest-lines-please-xxxxxx");
        }

        // One more write forces the size check after the file has grown past the cap.
        DevFileLog.Info("FlintBrowser", "NEW-FINAL-marker-line");

        SharedLength(path).ShouldBeLessThanOrEqualTo(maximumBytes);

        var text = ReadShared(path);
        text.ShouldContain("NEW-FINAL-marker-line");
        text.ShouldContain("trimmed oldest lines");
        text.ShouldNotContain("OLD-000");
    }

    [Fact]
    public void StartWithoutTruncate_TrimsExistingOversizedFile()
    {
        var builder = new StringBuilder();
        for (var i = 0; i < 200; i++)
        {
            builder.AppendLine($"seed-OLD-{i:D3}-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");
        }

        builder.AppendLine("seed-NEW-keep");
        File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
        new FileInfo(path).Length.ShouldBeGreaterThan(2048);

        DevFileLog.StartForTests(path, maximumBytes: 2048, retainBytes: 1024, truncateExisting: false);
        DevFileLog.Info("FlintBrowser", "after-start");

        var text = ReadShared(path);
        text.ShouldContain("after-start");
        text.ShouldContain("seed-NEW-keep");
        text.ShouldNotContain("seed-OLD-000");
        SharedLength(path).ShouldBeLessThanOrEqualTo(2048 + 512);
    }

    [Fact]
    public void ResetForTests_StopsFurtherWrites()
    {
        DevFileLog.StartForTests(path, maximumBytes: 64 * 1024, retainBytes: 32 * 1024);
        DevFileLog.ResetForTests();
        DevFileLog.Info("FlintBrowser", "should-not-appear");

        if (File.Exists(path))
        {
            ReadShared(path).ShouldNotContain("should-not-appear");
        }
    }

    [Fact]
    public void ResetForTests_WhileTraceIsCallingListeners_DoesNotDeadlock()
    {
        // Regression test for a CI hang. Trace holds its global lock while it calls listeners, and
        // ours then takes the file lock. Reset used to hold the file lock while removing the
        // listener, which needs the Trace lock, so a breadcrumb logged by another test at that
        // moment left each thread waiting on the other forever.
        Trace.UseGlobalLock.ShouldBeTrue();
        DevFileLog.StartForTests(path, maximumBytes: 64 * 1024, retainBytes: 32 * 1024);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = new HoldingTraceListener(entered, release);

        // First in the list, so the tracing thread parks holding the Trace lock before it reaches ours.
        Trace.Listeners.Insert(0, holder);
        var tracing = new Thread(() => Trace.WriteLine("FlintDiag|INFO|FlintTest|racing reset")) { IsBackground = true };
        var resetting = new Thread(DevFileLog.ResetForTests) { IsBackground = true };
        try
        {
            tracing.Start();
            entered.Wait(DeadlockTimeout, TestContext.Current.CancellationToken).ShouldBeTrue();
            resetting.Start();

            // Wait for the reset to block on the Trace lock: the point where the old code also held the file lock.
            SpinWait.SpinUntil(
                () => (resetting.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0,
                DeadlockTimeout).ShouldBeTrue();
        }
        finally
        {
            // Never leave the tracing thread parked on Trace's lock; every later trace would hang.
            release.Set();
        }

        var finished = tracing.Join(DeadlockTimeout) && resetting.Join(DeadlockTimeout);
        if (finished)
        {
            // Removing the listener needs the Trace lock, which a deadlocked thread would never release.
            Trace.Listeners.Remove(holder);
        }

        finished.ShouldBeTrue("reset and a concurrent Trace.WriteLine deadlocked on each other's lock");
        ReadShared(path).ShouldContain("[INFO] FlintTest: racing reset");
    }

    /// <summary>
    /// Reads while the append writer is still open — same share mode agents use when copying the live log.
    /// </summary>
    private static string ReadShared(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static long SharedLength(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return stream.Length;
    }

    /// <summary>Parks the first line it receives until released, holding Trace's global lock meanwhile.</summary>
    private sealed class HoldingTraceListener(ManualResetEventSlim entered, ManualResetEventSlim release) : TraceListener
    {
        private int held;

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
            if (Interlocked.Exchange(ref held, 1) == 0)
            {
                entered.Set();
                release.Wait();
            }
        }
    }
}
