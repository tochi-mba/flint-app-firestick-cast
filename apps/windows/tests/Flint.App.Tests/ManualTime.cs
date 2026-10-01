namespace Flint.App.Tests;

/// <summary>A clock that moves only when a test moves it, firing due timers as it goes.</summary>
internal sealed class ManualTime : TimeProvider
{
    private readonly List<ManualTimer> timers = [];
    private DateTimeOffset now = new(2026, 10, 1, 20, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => now;

    /// <summary>Timers that have not been disposed.</summary>
    public int ActiveTimers
    {
        get
        {
            lock (timers)
            {
                return timers.Count;
            }
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        lock (timers)
        {
            timers.Add(timer);
        }

        return timer;
    }

    /// <summary>Moves the clock on, firing each timer as its time comes, in order.</summary>
    public void Advance(TimeSpan by)
    {
        var until = now + by;
        while (true)
        {
            ManualTimer? next;
            lock (timers)
            {
                next = timers.Where(timer => timer.Due is { } due && due <= until).MinBy(timer => timer.Due);
            }

            if (next is null)
            {
                break;
            }

            now = next.Due!.Value;
            next.Fire();
        }

        now = until;
    }

    private void Remove(ManualTimer timer)
    {
        lock (timers)
        {
            timers.Remove(timer);
        }
    }

    private sealed class ManualTimer(ManualTime clock, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan period = Timeout.InfiniteTimeSpan;

        public DateTimeOffset? Due { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            this.period = period;
            Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.now + dueTime;
            return true;
        }

        public void Fire()
        {
            Due = period == Timeout.InfiniteTimeSpan || period == TimeSpan.Zero ? null : Due + period;
            callback(state);
        }

        public void Dispose()
        {
            Due = null;
            clock.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
