using Flint.App.Services;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// Progress that can be ended, so a report queued before a send failed cannot overwrite the failure.
/// </summary>
public sealed class EndableProgressTests
{
    [Fact]
    public void AReport_RunsOnTheContextItWasCreatedOn()
    {
        var context = new QueuedContext();
        var seen = new List<double>();
        var progress = Under(context, () => new EndableProgress<double>(seen.Add));

        progress.Report(0.5);

        seen.ShouldBeEmpty("nothing runs until the context does");
        context.RunAll();
        seen.ShouldBe([0.5]);
    }

    [Fact]
    public void AReportStillQueued_WhenItIsEnded_IsDropped()
    {
        var context = new QueuedContext();
        var seen = new List<double>();
        var progress = Under(context, () => new EndableProgress<double>(seen.Add));

        progress.Report(0.9);
        progress.End();
        context.RunAll();

        seen.ShouldBeEmpty();
    }

    [Fact]
    public void AReportAfterItIsEnded_IsDropped()
    {
        var context = new QueuedContext();
        var seen = new List<double>();
        var progress = Under(context, () => new EndableProgress<double>(seen.Add));
        progress.End();

        progress.Report(1.0);
        context.RunAll();

        seen.ShouldBeEmpty();
    }

    [Fact]
    public async Task WithNoContext_ReportsStillArrive()
    {
        var arrived = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
        var progress = Under(null, () => new EndableProgress<double>(value => arrived.TrySetResult(value)));

        progress.Report(0.25);

        (await arrived.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).ShouldBe(0.25);
    }

    [Fact]
    public async Task Ending_WaitsForAReportThatIsAlreadyRunning()
    {
        using var running = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var shown = new List<double>();
        var progress = Under(null, () => new EndableProgress<double>(value =>
        {
            running.Set();
            release.Wait(TestContext.Current.CancellationToken);
            shown.Add(value);
        }));

        progress.Report(0.9);
        running.Wait(TestContext.Current.CancellationToken);
        var ending = Task.Run(progress.End, TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        ending.IsCompleted.ShouldBeFalse("the report is still being shown");

        release.Set();
        await ending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        shown.ShouldBe([0.9], "it finished before End returned, so nothing can follow the failure");
    }

    [Fact]
    public void AMissingHandler_IsRefused() =>
        Should.Throw<ArgumentNullException>(() => new EndableProgress<double>(null!));

    private static T Under<T>(SynchronizationContext? context, Func<T> create)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            return create();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    /// <summary>A context that holds every posted callback until the test runs them.</summary>
    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> queued = new();

        public override void Post(SendOrPostCallback d, object? state) => queued.Enqueue((d, state));

        public void RunAll()
        {
            while (queued.TryDequeue(out var next))
            {
                next.Callback(next.State);
            }
        }
    }
}
