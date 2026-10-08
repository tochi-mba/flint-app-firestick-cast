using Flint.Core;
using Flint.Protocol;
using Shouldly;

namespace Flint.Session.Tests;

/// <summary>
/// Pausing a running share. The runner holds its own line on what reaches the TV, because a picture
/// leaking from a paused share is a privacy failure, whatever the engine does.
/// </summary>
public sealed class ScreenMirrorPauseTests
{
    private static readonly MirrorSessionOptions First = new(OutputIndex: 0, MaxWidth: 1920);
    private static readonly MirrorSessionOptions Second = new(OutputIndex: 1, MaxWidth: 1280);

    [Fact]
    public async Task APause_ReachesTheEngineOnItsOwnThread_OncePerChange()
    {
        using var harness = new Harness();
        harness.At(tick: 2, () => harness.Control.SetPause(MirrorPause.HoldingLastPicture));
        harness.At(tick: 6, () => harness.Control.SetPause(MirrorPause.Running));
        harness.At(tick: 8, harness.Stop);

        await harness.RunAsync();

        harness.Engine.Pauses.ShouldBe([MirrorPause.HoldingLastPicture, MirrorPause.Running]);
        harness.Engine.ThreadIds.Distinct().ShouldHaveSingleItem("start, pause and dispose share one thread");
    }

    [Fact]
    public async Task HoldingThePicture_SendsNothing_EvenIfTheEngineMisbehaves()
    {
        using var harness = new Harness();
        harness.At(tick: 2, () => harness.Control.SetPause(MirrorPause.HoldingLastPicture));
        harness.At(tick: 10, harness.Stop);

        await harness.RunAsync();

        harness.Transport.Videos.ShouldBe(2, "the two frames before the pause, and none of the eight after");
    }

    [Fact]
    public async Task ABlackScreen_LetsTheBlackFrameThrough_AndNoMore()
    {
        using var harness = new Harness();
        harness.At(tick: 2, () => harness.Control.SetPause(MirrorPause.Black));
        harness.At(tick: 12, harness.Stop);

        await harness.RunAsync();

        harness.Transport.Videos.ShouldBe(2 + MirrorControl.BlackFrameAllowance);
    }

    [Fact]
    public async Task Resuming_SendsTheScreenAgain()
    {
        using var harness = new Harness();
        harness.At(tick: 1, () => harness.Control.SetPause(MirrorPause.HoldingLastPicture));
        harness.At(tick: 4, () => harness.Control.SetPause(MirrorPause.Running));
        harness.At(tick: 7, harness.Stop);

        await harness.RunAsync();

        harness.Transport.Videos.ShouldBe(3, "the first frame, then the fifth and sixth: a pause or resume asked for during a tick applies from the next");
    }

    [Fact]
    public async Task AChangeMadeWhilePaused_WaitsForResume()
    {
        using var harness = new Harness();
        harness.At(tick: 1, () =>
        {
            harness.Control.SetPause(MirrorPause.HoldingLastPicture);
            harness.Control.Change(Second);
        });
        harness.At(tick: 5, () =>
        {
            harness.Engine.Log.ShouldBe(["start:0"], "nothing restarted while paused");
            harness.Control.SetPause(MirrorPause.Running);
        });
        harness.At(tick: 7, harness.Stop);

        await harness.RunAsync();

        harness.Engine.Log.ShouldBe(["start:0", "dispose:1", "start:1", "dispose:2"]);
        harness.Transport.Log.Count(entry => entry == "surface:Mirror").ShouldBe(1);
    }

    [Fact]
    public async Task StoppingWhilePaused_PutsTheTvBackToIdle()
    {
        using var harness = new Harness();
        harness.At(tick: 1, () => harness.Control.SetPause(MirrorPause.Black));
        harness.At(tick: 3, harness.Stop);

        await harness.RunAsync();

        harness.Transport.Log[^1].ShouldBe("surface:Idle");
    }

    [Fact]
    public void OnlyKnownPauses_CanBeAskedFor()
    {
        var control = new MirrorControl();

        control.Pause.ShouldBe(MirrorPause.Running);
        control.SetPause(MirrorPause.Black);
        control.Pause.ShouldBe(MirrorPause.Black);
        Should.Throw<ArgumentOutOfRangeException>(() => control.SetPause((MirrorPause)9));
    }

    /// <summary>A runner over an engine whose sessions produce a frame on every tick, paused or not.</summary>
    private sealed class Harness : IDisposable
    {
        private readonly CancellationTokenSource stop = new();
        private readonly Dictionary<int, Action> script = [];
        private int ticks;

        public Harness()
        {
            Engine = new Engine(this);
            Runner = new ScreenMirrorRunner(Engine);
        }

        internal Engine Engine { get; }

        internal ScreenMirrorRunner Runner { get; }

        internal MirrorControl Control { get; } = new();

        internal Transport Transport { get; } = new();

        /// <summary>Runs <paramref name="action"/> during the share's <paramref name="tick"/>th tick, counted across sessions.</summary>
        internal void At(int tick, Action action) => script[tick] = action;

        internal void Stop() => stop.Cancel();

        internal void OnTick()
        {
            ticks++;
            if (script.TryGetValue(ticks, out var action))
            {
                action();
            }
        }

        internal Task<MirrorSessionStats> RunAsync() =>
            Runner.RunAsync(Transport, First, control: Control, cancellationToken: stop.Token);

        public void Dispose() => stop.Dispose();
    }

    private sealed class Engine(Harness harness) : IMirrorEngine
    {
        private int started;

        internal List<string> Log { get; } = [];

        internal List<MirrorPause> Pauses { get; } = [];

        internal List<int> ThreadIds { get; } = [];

        public IMirrorEngineSession Start(MirrorSessionOptions options)
        {
            ThreadIds.Add(Environment.CurrentManagedThreadId);
            Log.Add($"start:{options.OutputIndex}");
            return new Session(this, harness, ++started);
        }

        private sealed class Session(Engine engine, Harness harness, int number) : IMirrorEngineSession
        {
            private int ticks;

            public VideoCodec Codec => VideoCodec.H264;

            public MirrorEncoderKind EncoderKind => MirrorEncoderKind.Software;

            public int Width => 1280;

            public int Height => 720;

            public IReadOnlyList<byte[]> CodecSpecificData { get; } = [[0, 0, 0, 1, 0x67], [0, 0, 0, 1, 0x68]];

            public MirrorTick Next(Span<byte> buffer)
            {
                ticks++;
                harness.OnTick();
                return new MirrorTick(MirrorTickKind.Encoded, 10, ticks == 1, ticks * 33_333L);
            }

            public void RequestKeyFrame()
            {
            }

            public void SetPause(MirrorPause pause)
            {
                engine.ThreadIds.Add(Environment.CurrentManagedThreadId);
                engine.Pauses.Add(pause);
            }

            public MirrorSessionStats ReadStats() => new(ticks, 0, 0, ticks * 10L);

            public void Dispose()
            {
                engine.ThreadIds.Add(Environment.CurrentManagedThreadId);
                engine.Log.Add($"dispose:{number}");
            }
        }
    }

    private sealed class Transport : IMirrorTransport
    {
        internal List<string> Log { get; } = [];

        internal int Videos => Log.Count(entry => entry == "video");

        public bool IsConnected => true;

        public Task SendSurfaceAsync(SurfaceMessage surface, CancellationToken cancellationToken = default)
        {
            Log.Add($"surface:{surface.Mode}");
            return Task.CompletedTask;
        }

        public Task SendVideoConfigAsync(
            CodecId codec,
            int width,
            int height,
            IEnumerable<BinaryData> codecSpecificData,
            CancellationToken cancellationToken = default)
        {
            Log.Add($"config:{width}x{height}");
            return Task.CompletedTask;
        }

        public Task SendVideoAsync(
            long presentationTimeUs,
            bool keyFrame,
            BinaryData data,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Log.Add("video");
            return Task.CompletedTask;
        }
    }
}
