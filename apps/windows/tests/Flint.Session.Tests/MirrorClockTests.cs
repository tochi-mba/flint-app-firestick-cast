using System.Collections.Concurrent;
using Flint.Core;
using Flint.Protocol;
using Shouldly;

namespace Flint.Session.Tests;

/// <summary>The picture's clock: read on the share's worker, published for sound to share.</summary>
public sealed class MirrorClockTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void ThePicturesClock_IsUnknownUntilStarted_ThenRunsOnTheControlsTime()
    {
        var time = new SteppedTime();
        var control = new MirrorControl(time);

        control.ReadPictureTimeUs().ShouldBeNull();
        control.StartClock(2_000_000);
        control.ReadPictureTimeUs().ShouldBe(2_000_000);
        time.Advance(TimeSpan.FromMilliseconds(1_500));

        control.ReadPictureTimeUs().ShouldBe(3_500_000);
    }

    [Fact]
    public void WaitingForThePicturesClock_GivesUpInTime_OrAnswersAtOnceWhenItRuns()
    {
        var time = new SteppedTime();
        var control = new MirrorControl(time);

        control.WaitForPictureTimeUs(TimeSpan.FromMilliseconds(10), Token).ShouldBeNull();
        control.StartClock(40);
        control.WaitForPictureTimeUs(TimeSpan.FromHours(1), Token).ShouldBe(40);
    }

    [Fact]
    public void WaitingForThePicturesClock_StopsWhenCancelled()
    {
        var control = new MirrorControl();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Should.Throw<OperationCanceledException>(() => control.WaitForPictureTimeUs(TimeSpan.FromHours(1), cancelled.Token));
        Should.Throw<ArgumentNullException>(() => new MirrorControl(null!));
    }

    [Fact]
    public async Task TheRunner_StartsTheClockFromTheSessionsOwnTime_OnTheSessionsThread()
    {
        var engine = new ClockedEngine(5_000_000);
        var control = new MirrorControl(new SteppedTime());
        var transport = new CountingTransport();
        using var stop = new CancellationTokenSource();

        var run = new ScreenMirrorRunner(engine).RunAsync(transport, new MirrorSessionOptions(), "Screen", control, stop.Token);
        await Eventually.TrueAsync(() => transport.Frames > 2);
        await stop.CancelAsync();
        await run;

        control.ReadPictureTimeUs().ShouldBe(5_000_000);
        engine.ClockThreads.ShouldHaveSingleItem().ShouldBe(engine.StartThreads.ShouldHaveSingleItem());
    }

    [Fact]
    public async Task ARestartedPicture_RestartsTheClock()
    {
        var engine = new ClockedEngine(5_000_000, 100);
        var control = new MirrorControl(new SteppedTime());
        var transport = new CountingTransport();
        var switched = new TaskCompletionSource<MirrorSwitch>(TaskCreationOptions.RunContinuationsAsynchronously);
        control.Switched += result => switched.TrySetResult(result);
        using var stop = new CancellationTokenSource();
        var run = new ScreenMirrorRunner(engine).RunAsync(transport, new MirrorSessionOptions(), "Screen", control, stop.Token);
        await Eventually.TrueAsync(() => transport.Frames > 0);

        control.Change(new MirrorSessionOptions(OutputIndex: 1));
        (await switched.Task.WaitAsync(Token)).Succeeded.ShouldBeTrue();
        await stop.CancelAsync();
        await run;

        control.ReadPictureTimeUs().ShouldBe(100);
    }

    [Fact]
    public async Task AClockThatCannotBeRead_LeavesSoundToTimeItself_AndThePictureCarriesOn()
    {
        var engine = new ClockedEngine((long?)null);
        var control = new MirrorControl();
        var transport = new CountingTransport();
        using var stop = new CancellationTokenSource();

        var run = new ScreenMirrorRunner(engine).RunAsync(transport, new MirrorSessionOptions(), "Screen", control, stop.Token);
        await Eventually.TrueAsync(() => transport.Frames > 2);
        await stop.CancelAsync();
        await run;

        control.ReadPictureTimeUs().ShouldBeNull();
        engine.ClockThreads.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ASessionWithoutAClock_LeavesThePicturesClockUnknown()
    {
        var control = new MirrorControl();
        var transport = new CountingTransport();
        using var stop = new CancellationTokenSource();

        var run = new ScreenMirrorRunner(new ClocklessEngine()).RunAsync(transport, new MirrorSessionOptions(), "Screen", control, stop.Token);
        await Eventually.TrueAsync(() => transport.Frames > 2);
        await stop.CancelAsync();
        await run;

        control.ReadPictureTimeUs().ShouldBeNull();
    }

    /// <summary>Starts sessions whose clocks read the given times in turn; null cannot be read.</summary>
    private sealed class ClockedEngine(params long?[] elapsed) : IMirrorEngine
    {
        private int started;

        public ConcurrentQueue<int> StartThreads { get; } = new();

        public ConcurrentQueue<int> ClockThreads { get; } = new();

        public IMirrorEngineSession Start(MirrorSessionOptions options)
        {
            StartThreads.Enqueue(Environment.CurrentManagedThreadId);
            return new ClockedSession(this, elapsed[Math.Min(started++, elapsed.Length - 1)]);
        }

        private sealed class ClockedSession(ClockedEngine engine, long? elapsed) : FrameSession, IMirrorClock
        {
            public long ReadElapsedUs()
            {
                engine.ClockThreads.Enqueue(Environment.CurrentManagedThreadId);
                return elapsed ?? throw new MirrorEngineException("The Flint engine failed while reading how long it has been capturing.");
            }
        }
    }

    private sealed class ClocklessEngine : IMirrorEngine
    {
        public IMirrorEngineSession Start(MirrorSessionOptions options) => new FrameSession();
    }

    /// <summary>A session that encodes a small frame on every tick.</summary>
    private class FrameSession : IMirrorEngineSession
    {
        private long ticks;

        public VideoCodec Codec => VideoCodec.H264;

        public MirrorEncoderKind EncoderKind => MirrorEncoderKind.Software;

        public int Width => 1280;

        public int Height => 720;

        public IReadOnlyList<byte[]> CodecSpecificData { get; } = [[0, 0, 0, 1, 0x67]];

        public MirrorTick Next(Span<byte> buffer)
        {
            ticks++;
            Thread.Sleep(1);
            return new MirrorTick(MirrorTickKind.Encoded, 4, ticks == 1, ticks * 33_333);
        }

        public void RequestKeyFrame()
        {
        }

        public void SetPause(MirrorPause pause)
        {
        }

        public MirrorSessionStats ReadStats() => new(ticks, 0, 0, ticks * 4, 0);

        public void Dispose()
        {
        }
    }

    private sealed class CountingTransport : IMirrorTransport
    {
        private int frames;

        public int Frames => Volatile.Read(ref frames);

        public bool IsConnected => true;

        public Task SendSurfaceAsync(SurfaceMessage surface, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendVideoConfigAsync(
            CodecId codec,
            int width,
            int height,
            IEnumerable<BinaryData> codecSpecificData,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendVideoAsync(long presentationTimeUs, bool keyFrame, BinaryData data, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref frames);
            return Task.CompletedTask;
        }
    }
}
