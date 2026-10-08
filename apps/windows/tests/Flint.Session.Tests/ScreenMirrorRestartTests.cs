using Flint.Core;
using Flint.Protocol;
using Shouldly;

namespace Flint.Session.Tests;

/// <summary>
/// Changing the display or picture while a share runs: the TV must keep its mirror surface, and the
/// native sessions must still start and end on the one worker thread.
/// </summary>
public sealed class ScreenMirrorRestartTests
{
    private static readonly MirrorSessionOptions First = new(OutputIndex: 0, MaxWidth: 1920);
    private static readonly MirrorSessionOptions Second = new(OutputIndex: 1, MaxWidth: 1280);
    private static readonly MirrorSessionOptions Broken = new(OutputIndex: 9);

    [Fact]
    public async Task NewOptions_ReplaceTheSession_WithANewConfiguration_AndNoSecondSurface()
    {
        using var harness = new Harness();
        harness.At(session: 1, tick: 2, () => harness.Control.Change(Second));
        harness.At(session: 2, tick: 2, harness.Stop);

        await harness.RunAsync();

        harness.Engine.Log.ShouldBe(["start:0", "dispose:1", "start:1", "dispose:2"], "the old session ends before the new one starts");
        harness.Transport.Log.Count(entry => entry == "surface:Mirror").ShouldBe(1);
        harness.Transport.Log.Take(harness.Transport.Log.Count - 1).ShouldNotContain("surface:Idle", "the TV never drops to its idle screen in between");
        harness.Transport.Log[^1].ShouldBe("surface:Idle");
        harness.Transport.Log.Count(entry => entry.StartsWith("config:", StringComparison.Ordinal)).ShouldBe(2);
        harness.Transport.Log.ShouldContain("config:1280x720");
        harness.Switches.ShouldHaveSingleItem().ShouldBe(new MirrorSwitch(Second, null));
        harness.Switches[0].Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task EverySessionStartsAndEnds_OnTheSameWorkerThread()
    {
        using var harness = new Harness();
        harness.At(session: 1, tick: 1, () => harness.Control.Change(Second));
        harness.At(session: 2, tick: 1, harness.Stop);

        await harness.RunAsync();

        harness.Engine.ThreadIds.Distinct().ShouldHaveSingleItem("start and dispose of both sessions share one COM thread");
    }

    [Fact]
    public async Task AChangeThatCannotStart_KeepsSharingWhatItWas_AndSaysWhyOnce()
    {
        using var harness = new Harness();
        harness.At(session: 1, tick: 1, () => harness.Control.Change(Broken));
        harness.At(session: 2, tick: 1, harness.Stop);

        await harness.RunAsync();

        harness.Engine.Log.ShouldBe(["start:0", "dispose:1", "refused:9", "start:0", "dispose:2"]);
        var reported = harness.Switches.ShouldHaveSingleItem();
        reported.Options.ShouldBe(First);
        reported.Failure.ShouldBe("display 9 cannot be captured");
        reported.Succeeded.ShouldBeFalse();
        harness.Transport.Log.Count(entry => entry.StartsWith("config:", StringComparison.Ordinal)).ShouldBe(2);
    }

    [Fact]
    public async Task ChangesInQuickSuccession_RestartOnce_WithTheLatest()
    {
        using var harness = new Harness();
        harness.At(session: 1, tick: 1, () =>
        {
            harness.Control.Change(Broken);
            harness.Control.Change(Second);
        });
        harness.At(session: 2, tick: 1, harness.Stop);

        await harness.RunAsync();

        harness.Engine.Log.ShouldBe(["start:0", "dispose:1", "start:1", "dispose:2"]);
        harness.Switches.ShouldHaveSingleItem().Options.ShouldBe(Second);
    }

    [Fact]
    public async Task AskingForWhatIsAlreadyBeingSent_ChangesNothing()
    {
        using var harness = new Harness();
        harness.At(session: 1, tick: 1, () => harness.Control.Change(First with { }));
        harness.At(session: 1, tick: 3, harness.Stop);

        await harness.RunAsync();

        harness.Engine.Log.ShouldBe(["start:0", "dispose:1"]);
        harness.Switches.ShouldBeEmpty();
    }

    [Fact]
    public async Task StoppingDuringARestart_EndsCleanly_AndPutsTheTvBackToIdle()
    {
        using var harness = new Harness();
        harness.Engine.OnStart = options =>
        {
            if (options == Second)
            {
                harness.Stop();
            }
        };
        harness.At(session: 1, tick: 1, () => harness.Control.Change(Second));

        await harness.RunAsync();

        harness.Engine.Log.ShouldBe(["start:0", "dispose:1", "start:1", "dispose:2"]);
        harness.Transport.Log[^1].ShouldBe("surface:Idle");
    }

    [Fact]
    public async Task WhenNeitherTheNewNorTheOldOptionsStart_TheShareEnds_AndTheTvGoesBackToIdle()
    {
        using var harness = new Harness();
        harness.At(session: 1, tick: 1, () =>
        {
            harness.Engine.Refuse = _ => true;
            harness.Control.Change(Second);
        });

        await Should.ThrowAsync<MirrorEngineException>(harness.RunAsync());

        harness.Engine.Log.ShouldBe(["start:0", "dispose:1", "refused:1", "refused:0"], "nothing is disposed twice");
        harness.Transport.Log[^1].ShouldBe("surface:Idle");
    }

    [Fact]
    public async Task TheCounters_CoverEverySessionOfTheShare()
    {
        using var harness = new Harness();
        var updates = new List<MirrorSessionStats>();
        harness.Runner.StatsUpdated += updates.Add;
        harness.At(session: 1, tick: 3, () => harness.Control.Change(Second));
        harness.At(session: 2, tick: 2, harness.Stop);

        var stats = await harness.RunAsync();

        stats.FramesEncoded.ShouldBe(5, "three frames before the change and two after");
        stats.BytesEncoded.ShouldBe(5 * 10);
        stats.FramesHeldBack.ShouldBe(5);
        updates[^1].FramesEncoded.ShouldBe(4, "the last frame that reached the TV was the fourth");
    }

    [Fact]
    public async Task EachSession_SaysWhatPictureItSends()
    {
        using var harness = new Harness();
        var pictures = new List<MirrorPicture>();
        harness.Runner.PictureStarted += pictures.Add;
        harness.At(session: 1, tick: 1, () => harness.Control.Change(Second));
        harness.At(session: 2, tick: 1, harness.Stop);

        await harness.RunAsync();

        pictures.ShouldBe([new MirrorPicture(1920, 720, MirrorEncoderKind.Software), new MirrorPicture(1280, 720, MirrorEncoderKind.Software)]);
    }

    [Fact]
    public async Task AChange_NobodyAskedToHearAbout_IsStillMade()
    {
        using var harness = new Harness(listen: false);
        harness.At(session: 1, tick: 1, () => harness.Control.Change(Second));
        harness.At(session: 2, tick: 1, harness.Stop);

        await harness.RunAsync();

        harness.Engine.Log.ShouldBe(["start:0", "dispose:1", "start:1", "dispose:2"]);
        harness.Switches.ShouldBeEmpty();
    }

    [Fact]
    public void AChange_MustSaySomething()
    {
        Should.Throw<ArgumentNullException>(() => new MirrorControl().Change(null!));
    }

    /// <summary>A runner, an engine that makes a new session per start, and a control to change it with.</summary>
    private sealed class Harness : IDisposable
    {
        private readonly CancellationTokenSource stop = new();
        private readonly Dictionary<(int Session, int Tick), Action> script = [];

        public Harness(bool listen = true)
        {
            Engine = new Engine(this);
            Runner = new ScreenMirrorRunner(Engine);
            if (listen)
            {
                Control.Switched += Switches.Add;
            }
        }

        internal Engine Engine { get; }

        internal ScreenMirrorRunner Runner { get; }

        internal MirrorControl Control { get; } = new();

        internal Transport Transport { get; } = new();

        internal List<MirrorSwitch> Switches { get; } = [];

        internal void At(int session, int tick, Action action) => script[(session, tick)] = action;

        internal void Stop() => stop.Cancel();

        internal void OnTick(int session, int tick)
        {
            if (script.TryGetValue((session, tick), out var action))
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

        internal List<int> ThreadIds { get; } = [];

        internal Func<MirrorSessionOptions, bool> Refuse { get; set; } = options => options.OutputIndex == 9;

        internal Action<MirrorSessionOptions>? OnStart { get; set; }

        public IMirrorEngineSession Start(MirrorSessionOptions options)
        {
            ThreadIds.Add(Environment.CurrentManagedThreadId);
            if (Refuse(options))
            {
                Log.Add($"refused:{options.OutputIndex}");
                throw new MirrorEngineException($"display {options.OutputIndex} cannot be captured");
            }

            Log.Add($"start:{options.OutputIndex}");
            OnStart?.Invoke(options);
            return new Session(this, harness, ++started, (int)options.MaxWidth);
        }
    }

    private sealed class Session(Engine engine, Harness harness, int number, int width) : IMirrorEngineSession
    {
        private int ticks;

        public VideoCodec Codec => VideoCodec.H264;

        public MirrorEncoderKind EncoderKind => MirrorEncoderKind.Software;

        public int Width => width is 0 ? 1920 : width;

        public int Height => 720;

        public IReadOnlyList<byte[]> CodecSpecificData { get; } = [[0, 0, 0, 1, 0x67], [0, 0, 0, 1, 0x68]];

        public MirrorTick Next(Span<byte> buffer)
        {
            ticks++;
            var tick = new MirrorTick(MirrorTickKind.Encoded, 10, ticks == 1, ticks * 33_333L);
            harness.OnTick(number, ticks);
            return tick;
        }

        public void RequestKeyFrame()
        {
        }

        public void SetPause(MirrorPause pause)
        {
        }

        public MirrorSessionStats ReadStats() => new(ticks, 0, 0, ticks * 10L, ticks);

        public void Dispose()
        {
            engine.ThreadIds.Add(Environment.CurrentManagedThreadId);
            engine.Log.Add($"dispose:{number}");
        }
    }

    private sealed class Transport : IMirrorTransport
    {
        internal List<string> Log { get; } = [];

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
