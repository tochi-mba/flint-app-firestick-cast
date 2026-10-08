using System.Collections.Concurrent;
using Flint.Core;
using Flint.Protocol;
using Shouldly;

namespace Flint.Session.Tests;

/// <summary>The pump that carries sound beside a screen share: order, pause, failure and stopping.</summary>
public sealed class AudioPumpTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheConfiguration_GoesOnceAndFirst_ThenPacketsInOrderWithTheirTimes()
    {
        var session = new FakeAudioSession();
        session.Add([1, 1], 21_333);
        session.Add([2, 2, 2], 42_666);
        session.Add([3], 64_000);
        var transport = new RecordingAudioTransport();
        using var stop = new CancellationTokenSource();

        var run = new AudioPump(new FakeAudioEngine(session)).RunAsync(transport, new AudioShareOptions(), null, stop.Token);
        await Eventually.TrueAsync(() => transport.Packets.Count == 3);
        await stop.CancelAsync();
        var end = await run;

        var sent = transport.Sent.ToArray();
        sent[0].ShouldBe(new AudioConfigMessage(CodecId.AacLc, 48_000, 2, BinaryData.From([0x11, 0x90])));
        sent.OfType<AudioConfigMessage>().ShouldHaveSingleItem();
        transport.Packets.ShouldBe(
        [
            new AudioPacket(21_333, BinaryData.From([1, 1])),
            new AudioPacket(42_666, BinaryData.From([2, 2, 2])),
            new AudioPacket(64_000, BinaryData.From([3])),
        ]);
        end.Problem.ShouldBeNull();
        end.Failure.ShouldBe(AudioStartFailure.None);
        session.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task WhilePaused_PacketsAreDrainedAndNotSent_AndResumingCarriesOn()
    {
        var session = new FakeAudioSession();
        var transport = new RecordingAudioTransport();
        var control = new MirrorControl();
        control.SetPause(MirrorPause.HoldingLastPicture);
        using var stop = new CancellationTokenSource();
        var run = new AudioPump(new FakeAudioEngine(session)).RunAsync(transport, new AudioShareOptions(), control, stop.Token);
        await Eventually.TrueAsync(() => session.PauseCalls.Count == 1);

        // The engine should send nothing while paused; the pump holds the line if it does.
        session.Add([9], 1_000);
        session.Add([9], 2_000);
        await Eventually.TrueAsync(() => session.Waiting == 0, "drained while paused");
        var turns = session.NextCalls;
        await Eventually.TrueAsync(() => session.NextCalls > turns + 5);
        transport.Packets.ShouldBeEmpty();

        control.SetPause(MirrorPause.Black);
        session.Add([8], 3_000);
        await Eventually.TrueAsync(() => session.Waiting == 0);
        transport.Packets.ShouldBeEmpty("a black screen is paused too");

        control.SetPause(MirrorPause.Running);
        session.Add([7], 4_000);
        await Eventually.TrueAsync(() => transport.Packets.Count == 1);
        await stop.CancelAsync();
        await run;

        transport.Packets.ShouldHaveSingleItem().ShouldBe(new AudioPacket(4_000, BinaryData.From([7])));
        session.PauseCalls.ToArray().ShouldBe([true, false], "once per change, not once per turn");
    }

    [Fact]
    public async Task ARunningShare_IsNeverPausedWithoutAControl()
    {
        var session = new FakeAudioSession();
        using var stop = new CancellationTokenSource();
        var run = new AudioPump(new FakeAudioEngine(session)).RunAsync(new RecordingAudioTransport(), new AudioShareOptions(), null, stop.Token);
        await Eventually.TrueAsync(() => session.NextCalls > 3);
        await stop.CancelAsync();
        await run;

        session.PauseCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnEngineThatCannotStart_EndsTheShareWithItsReason_AndNothingIsSent()
    {
        var engine = new FakeAudioEngine(new FakeAudioSession())
        {
            Refusal = new AudioEngineException("No AAC encoder.", AudioStartFailure.NoEncoder),
        };
        var transport = new RecordingAudioTransport();

        var end = await new AudioPump(engine).RunAsync(transport, new AudioShareOptions(), null, Token);

        end.Problem.ShouldBe("No AAC encoder.");
        end.Failure.ShouldBe(AudioStartFailure.NoEncoder);
        end.Stats.State.ShouldBe(AudioShareState.Unavailable);
        transport.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnEngineFailingMidStream_StopsThePump_AndSaysWhy_WithoutThrowing()
    {
        var session = new FakeAudioSession { Stats = new AudioShareStats(40, 2, 0.5f, AudioShareState.Sounding) };
        var transport = new RecordingAudioTransport();
        var run = new AudioPump(new FakeAudioEngine(session)).RunAsync(transport, new AudioShareOptions(), null, Token);
        await Eventually.TrueAsync(() => session.NextCalls > 0);

        session.Failure = new MirrorEngineException("The Flint engine failed while reading sound (status 7).");
        var end = await run;

        end.Problem.ShouldBe("The Flint engine failed while reading sound (status 7).");
        end.Stats.ShouldBe(new AudioShareStats(40, 2, 0.5f, AudioShareState.Unavailable));
        session.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task TheReceiverGoingAway_EndsThePumpQuietly()
    {
        var session = new FakeAudioSession();
        var transport = new RecordingAudioTransport();
        var run = new AudioPump(new FakeAudioEngine(session)).RunAsync(transport, new AudioShareOptions(), null, Token);
        await Eventually.TrueAsync(() => session.NextCalls > 0);

        transport.IsConnected = false;
        var end = await run;

        end.Problem.ShouldBeNull();
        session.Disposed.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ASendThatFindsTheReceiverGone_EndsThePumpQuietly(bool disposed)
    {
        var session = new FakeAudioSession();
        var transport = new RecordingAudioTransport
        {
            SendFailure = disposed
                ? new ObjectDisposedException("CastSession")
                : new IOException("The receiver session is no longer connected."),
        };
        session.Add([1], 1);

        var end = await new AudioPump(new FakeAudioEngine(session)).RunAsync(transport, new AudioShareOptions(), null, Token);

        end.Problem.ShouldBeNull();
        session.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task Stopping_EndsTheShareWithinOneWait()
    {
        var session = new FakeAudioSession { Stats = new AudioShareStats(12, 0, 0, AudioShareState.Ready) };
        using var stop = new CancellationTokenSource();
        var run = new AudioPump(new FakeAudioEngine(session)).RunAsync(new RecordingAudioTransport(), new AudioShareOptions(), null, stop.Token);
        await Eventually.TrueAsync(() => session.NextCalls > 0);

        await stop.CancelAsync();
        var end = await run.WaitAsync(AudioPump.PacketWait * 20, Token);

        end.ShouldBe(new AudioPumpEnd(new AudioShareStats(12, 0, 0, AudioShareState.Ready), null));
        session.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task StoppedWhileWaitingForThePicture_SoundNeverStarts()
    {
        var engine = new FakeAudioEngine(new FakeAudioSession());
        using var stop = new CancellationTokenSource();
        var run = new AudioPump(engine).RunAsync(new RecordingAudioTransport(), new AudioShareOptions(), new MirrorControl(), stop.Token);

        await stop.CancelAsync();
        var end = await run;

        end.ShouldBe(new AudioPumpEnd(default, null));
        engine.Started.ShouldBeNull();
    }

    [Fact]
    public async Task SoundTakesThePicturesClock_WhenThePictureStartsItInTime()
    {
        var engine = new FakeAudioEngine(new FakeAudioSession());
        var time = new SteppedTime();
        var control = new MirrorControl(time);
        using var stop = new CancellationTokenSource();
        var run = new AudioPump(engine).RunAsync(new RecordingAudioTransport(), new AudioShareOptions(StartOffsetUs: 7), control, stop.Token);

        control.StartClock(3_000_000);
        await Eventually.TrueAsync(() => engine.Started is not null);
        await stop.CancelAsync();
        await run;

        engine.Started!.StartOffsetUs.ShouldBe(3_000_000);
    }

    [Fact]
    public async Task WithoutAPicture_SoundKeepsTheOffsetItWasGiven()
    {
        var engine = new FakeAudioEngine(new FakeAudioSession());
        using var stop = new CancellationTokenSource();
        var run = new AudioPump(engine).RunAsync(new RecordingAudioTransport(), new AudioShareOptions(StartOffsetUs: 7), null, stop.Token);

        await Eventually.TrueAsync(() => engine.Started is not null);
        await stop.CancelAsync();
        await run;

        engine.Started!.StartOffsetUs.ShouldBe(7);
    }

    [Fact]
    public async Task TheDelay_IsChangedInARunningShare_OncePerChange()
    {
        var session = new FakeAudioSession();
        var pump = new AudioPump(new FakeAudioEngine(session));
        using var stop = new CancellationTokenSource();
        var run = pump.RunAsync(new RecordingAudioTransport(), new AudioShareOptions(DelayMilliseconds: 40), null, stop.Token);
        await Eventually.TrueAsync(() => session.NextCalls > 0);

        pump.SetDelay(40);
        var turns = session.NextCalls;
        await Eventually.TrueAsync(() => session.NextCalls > turns + 3);
        session.DelayCalls.ShouldBeEmpty("already what the share started with");

        pump.SetDelay(120);
        await Eventually.TrueAsync(() => session.DelayCalls.Count == 1);
        pump.SetDelay(-30);
        await Eventually.TrueAsync(() => session.DelayCalls.Count == 2);
        turns = session.NextCalls;
        await Eventually.TrueAsync(() => session.NextCalls > turns + 3);
        await stop.CancelAsync();
        await run;

        session.DelayCalls.ToArray().ShouldBe([120, 0]);
    }

    [Fact]
    public async Task TheStateAndLevel_AreReportedAtOnceAndThenFourTimesASecond()
    {
        var session = new FakeAudioSession { Stats = new AudioShareStats(1, 0, 0.25f, AudioShareState.Sounding) };
        var time = new SteppedTime();
        var pump = new AudioPump(new FakeAudioEngine(session), time);
        var reports = new ConcurrentQueue<AudioPumpReport>();
        pump.Reported += reports.Enqueue;
        using var stop = new CancellationTokenSource();
        var run = pump.RunAsync(new RecordingAudioTransport(), new AudioShareOptions(), null, stop.Token);
        await Eventually.TrueAsync(() => reports.Count == 1);

        time.Advance(AudioPump.ReportEvery - TimeSpan.FromMilliseconds(1));
        var turns = session.NextCalls;
        await Eventually.TrueAsync(() => session.NextCalls > turns + 3);
        reports.Count.ShouldBe(1);

        session.Stats = new AudioShareStats(9, 0, 0, AudioShareState.Unavailable);
        session.Problem = "The chosen sound output was disconnected.";
        time.Advance(TimeSpan.FromMilliseconds(1));
        await Eventually.TrueAsync(() => reports.Count == 2);
        await stop.CancelAsync();
        await run;

        reports.ShouldBe(
        [
            new AudioPumpReport(new AudioShareStats(1, 0, 0.25f, AudioShareState.Sounding), null),
            new AudioPumpReport(new AudioShareStats(9, 0, 0, AudioShareState.Unavailable), "The chosen sound output was disconnected."),
        ]);
    }

    [Fact]
    public void ThePump_RefusesWhatItCannotRun()
    {
        var pump = new AudioPump(new FakeAudioEngine(new FakeAudioSession()));

        Should.Throw<ArgumentNullException>(() => new AudioPump(null!));
        Should.Throw<ArgumentNullException>(() => new AudioPump(new FakeAudioEngine(new FakeAudioSession()), null!));
        Should.Throw<ArgumentNullException>(() => pump.RunAsync(null!, new AudioShareOptions()));
        Should.Throw<ArgumentNullException>(() => pump.RunAsync(new RecordingAudioTransport(), null!));
    }

    [Fact]
    public async Task StoppingWhileASendWaits_EndsTheShareQuietly()
    {
        var session = new FakeAudioSession();
        var transport = new WaitingTransport();
        using var stop = new CancellationTokenSource();
        var run = new AudioPump(new FakeAudioEngine(session)).RunAsync(transport, new AudioShareOptions(), null, stop.Token);
        await transport.Waiting.Task.WaitAsync(Token);

        await stop.CancelAsync();
        var end = await run;

        end.Problem.ShouldBeNull();
        session.Disposed.ShouldBeTrue();
    }

    /// <summary>A receiver whose first send waits until it is called off.</summary>
    private sealed class WaitingTransport : IAudioTransport
    {
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsConnected => true;

        public async Task SendAudioConfigAsync(IReadOnlyList<byte> config, CancellationToken cancellationToken = default)
        {
            Waiting.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public Task SendAudioAsync(long presentationTimeUs, BinaryData data, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
