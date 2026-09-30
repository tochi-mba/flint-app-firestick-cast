using System.Diagnostics;
using Shouldly;

namespace Flint.Platform.Windows.Tests;

/// <summary>
/// The sweep that leaves exactly one Flint running: ask first, wait once, end only what ignored
/// the ask, and never touch this process or anything that is not Flint.
/// </summary>
public sealed class FlintProcessCleanupTests
{
    [Fact]
    public void OtherFlintCopies_AreAskedToCloseBeforeAnyWaitStarts()
    {
        var log = new List<string>();
        var first = new FakeProcess(10, isFlint: true, exitsWhenAsked: true, log);
        var second = new FakeProcess(11, isFlint: true, exitsWhenAsked: true, log);

        FlintProcessCleanup.StopOtherCopies([first, second], currentProcessId: 1, TimeSpan.FromSeconds(4));

        log.ShouldBe(["close 10", "close 11", "wait 10", "wait 11"]);
    }

    [Fact]
    public void ACopyThatIgnoresTheClose_IsEnded()
    {
        var log = new List<string>();
        var stubborn = new FakeProcess(10, isFlint: true, exitsWhenAsked: false, log);

        FlintProcessCleanup.StopOtherCopies([stubborn], currentProcessId: 1, TimeSpan.FromMilliseconds(50));

        log.ShouldBe(["close 10", "wait 10", "kill 10"]);
    }

    [Fact]
    public void OnceTheSharedGraceIsSpent_TheRestAreEndedWithoutWaiting()
    {
        var log = new List<string>();
        var slow = new FakeProcess(10, isFlint: true, exitsWhenAsked: false, log, waitCost: TimeSpan.FromMilliseconds(80));
        var next = new FakeProcess(11, isFlint: true, exitsWhenAsked: true, log);

        FlintProcessCleanup.StopOtherCopies([slow, next], currentProcessId: 1, TimeSpan.FromMilliseconds(40));

        log.ShouldBe(["close 10", "close 11", "wait 10", "kill 10", "kill 11"]);
    }

    [Fact]
    public void ThisProcessAndProcessesThatAreNotFlint_AreNeverTouched()
    {
        var log = new List<string>();
        var self = new FakeProcess(1, isFlint: true, exitsWhenAsked: true, log);
        var stranger = new FakeProcess(20, isFlint: false, exitsWhenAsked: true, log);

        FlintProcessCleanup.StopOtherCopies([self, stranger], currentProcessId: 1, TimeSpan.FromSeconds(4));

        log.ShouldBeEmpty();
    }

    [Fact]
    public void TheRealSweep_LeavesAProcessThatIsNotFlintRunning()
    {
        // Aimed at ping rather than Flint.App, so it can never close the Flint a developer is using.
        using var ping = StartPing();
        try
        {
            FlintProcessCleanup.StopOtherCopies("PING");

            ping.HasExited.ShouldBeFalse();
        }
        finally
        {
            ping.Kill();
        }
    }

    [Fact]
    public void TheRealSweep_WithNothingRunning_DoesNothing()
    {
        Should.NotThrow(() => FlintProcessCleanup.StopOtherCopies($"flint-test-{Guid.NewGuid():N}"));
    }

    [Fact]
    public void ARealProcess_IsNotFlintAndCanBeEnded()
    {
        using var ping = StartPing();
        var running = new FlintProcessCleanup.RunningProcess(ping);

        running.Id.ShouldBe(ping.Id);
        running.IsFlint.ShouldBeFalse("ping is a Microsoft executable");
        Should.NotThrow(running.RequestClose, "a process with no window just declines the close");
        running.WaitForExit(TimeSpan.FromMilliseconds(50)).ShouldBeFalse();

        running.Kill();

        running.WaitForExit(TimeSpan.FromSeconds(10)).ShouldBeTrue();
    }

    [Fact]
    public void AProcessThatHasExited_IsNotFlintAndIgnoresEveryCall()
    {
        using var ping = StartPing();
        ping.Kill();
        ping.WaitForExit();
        var exited = new FlintProcessCleanup.RunningProcess(ping);

        exited.IsFlint.ShouldBeFalse();
        Should.NotThrow(exited.RequestClose);
        Should.NotThrow(exited.Kill);
    }

    [Fact]
    public void AProcessWhoseModulesCannotBeRead_IsNotFlint()
    {
        // The System process (id 4) refuses module enumeration to an ordinary user.
        using var system = Process.GetProcessById(4);

        new FlintProcessCleanup.RunningProcess(system).IsFlint.ShouldBeFalse();
    }

    [Fact]
    public void AnExecutableCarryingFlintsMetadata_IsRecognised()
    {
        // Every assembly in this repository is stamped Flint / REX Technologies, this one included.
        FlintProcessCleanup.IsFlintExecutable(typeof(FlintProcessCleanup).Assembly.Location).ShouldBeTrue();
    }

    [Fact]
    public void AnExecutableFromAnotherPublisher_IsNotFlint()
    {
        var ping = Path.Combine(Environment.SystemDirectory, "PING.EXE");

        FlintProcessCleanup.IsFlintExecutable(ping).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void NoExecutablePath_IsNotFlint(string? path)
    {
        FlintProcessCleanup.IsFlintExecutable(path).ShouldBeFalse();
    }

    [Fact]
    public void AProcessObjectThatWasNeverStarted_IsNotFlintAndIgnoresEveryCall()
    {
        // Asking an unassociated Process anything throws InvalidOperationException; the sweep must
        // treat that as "not a copy it can act on", not as a crash inside the installer.
        using var unstarted = new Process();
        var process = new FlintProcessCleanup.RunningProcess(unstarted);

        process.IsFlint.ShouldBeFalse();
        Should.NotThrow(process.RequestClose);
        process.WaitForExit(TimeSpan.FromMilliseconds(10)).ShouldBeFalse();
        Should.NotThrow(process.Kill);
    }

    private static Process StartPing() =>
        Process.Start(new ProcessStartInfo("ping", "-n 60 127.0.0.1")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        }) ?? throw new InvalidOperationException("ping did not start.");

    private sealed class FakeProcess(
        int id,
        bool isFlint,
        bool exitsWhenAsked,
        List<string> log,
        TimeSpan waitCost = default) : FlintProcessCleanup.IRunningProcess
    {
        public int Id => id;

        public bool IsFlint => isFlint;

        public void RequestClose() => log.Add($"close {id}");

        public bool WaitForExit(TimeSpan timeout)
        {
            log.Add($"wait {id}");
            if (waitCost > TimeSpan.Zero)
            {
                Thread.Sleep(waitCost);
            }

            return exitsWhenAsked;
        }

        public void Kill() => log.Add($"kill {id}");
    }
}
