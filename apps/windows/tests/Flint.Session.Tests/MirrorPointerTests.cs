using System.Collections.Concurrent;
using Flint.Core;
using Flint.Protocol;
using Shouldly;

namespace Flint.Session.Tests;

/// <summary>Drawing the mouse pointer into a share: applied on the share's thread, once per change.</summary>
public sealed class MirrorPointerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void ThePointer_IsShownUnlessSwitchedOff()
    {
        var control = new MirrorControl();

        control.ShowPointer.ShouldBeTrue();
        control.SetShowPointer(false);
        control.ShowPointer.ShouldBeFalse();
    }

    [Fact]
    public async Task TheRunner_TellsEachSessionOnce_AndAgainWhenItChanges_OnTheSessionsThread()
    {
        var engine = new PointerEngine();
        var control = new MirrorControl();
        var transport = new FrameCounter();
        using var stop = new CancellationTokenSource();
        var run = new ScreenMirrorRunner(engine).RunAsync(transport, new MirrorSessionOptions(), "Screen", control, stop.Token);
        await Eventually.TrueAsync(() => transport.Frames > 3);

        control.SetShowPointer(false);
        await Eventually.TrueAsync(() => engine.Calls.Count == 2);
        var switched = new TaskCompletionSource<MirrorSwitch>(TaskCreationOptions.RunContinuationsAsynchronously);
        control.Switched += result => switched.TrySetResult(result);
        control.Change(new MirrorSessionOptions(OutputIndex: 1));
        await switched.Task.WaitAsync(Token);
        var frames = transport.Frames;
        await Eventually.TrueAsync(() => transport.Frames > frames + 3);
        await stop.CancelAsync();
        await run;

        engine.Calls.ShouldBe(["1:True", "1:False", "2:False"]);
        engine.Threads.Distinct().Count().ShouldBe(1, "every call on the share's own thread");
    }

    [Fact]
    public async Task WithoutAControl_ThePointerIsShown()
    {
        var engine = new PointerEngine();
        var transport = new FrameCounter();
        using var stop = new CancellationTokenSource();
        var run = new ScreenMirrorRunner(engine).RunAsync(transport, new MirrorSessionOptions(), cancellationToken: stop.Token);
        await Eventually.TrueAsync(() => transport.Frames > 3);
        await stop.CancelAsync();
        await run;

        engine.Calls.ShouldBe(["1:True"]);
    }

    /// <summary>Starts sessions that record each time they are told about the pointer.</summary>
    private sealed class PointerEngine : IMirrorEngine
    {
        private int started;

        public ConcurrentQueue<string> Calls { get; } = new();

        public ConcurrentQueue<int> Threads { get; } = new();

        public IMirrorEngineSession Start(MirrorSessionOptions options) => new Session(this, ++started);

        private sealed class Session(PointerEngine engine, int number) : IMirrorEngineSession
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

            public void SetShowPointer(bool show)
            {
                engine.Calls.Enqueue($"{number}:{show}");
                engine.Threads.Enqueue(Environment.CurrentManagedThreadId);
            }

            public MirrorSessionStats ReadStats() => new(ticks, 0, 0, ticks * 4, 0);

            public void Dispose()
            {
            }
        }
    }

    private sealed class FrameCounter : IMirrorTransport
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
