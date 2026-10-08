using System.Runtime.InteropServices;
using Flint.Core;
using Shouldly;

namespace Flint.Engine.Interop.Tests;

/// <summary>Sound across the boundary: layouts, codes, and a real share on this PC when it has sound.</summary>
public sealed class NativeAudioEngineTests
{
    [Fact]
    public void TheStructures_MatchTheEngine()
    {
        Marshal.SizeOf<NativeAudioDevice>().ShouldBe(776);
        Marshal.OffsetOf<NativeAudioDevice>("_name").ShouldBe(8);
        Marshal.OffsetOf<NativeAudioDevice>("_id").ShouldBe(8 + 256);
        Marshal.SizeOf<NativeAudioConfig>().ShouldBe(32);
        Marshal.OffsetOf<NativeAudioConfig>(nameof(NativeAudioConfig.BitrateKbps)).ShouldBe(12);
        Marshal.OffsetOf<NativeAudioConfig>(nameof(NativeAudioConfig.StartOffsetUs)).ShouldBe(16);
        Marshal.SizeOf<NativeAudioStats>().ShouldBe(32);
        Marshal.OffsetOf<NativeAudioStats>(nameof(NativeAudioStats.State)).ShouldBe(20);
        Marshal.OffsetOf<NativeAudioStats>(nameof(NativeAudioStats.Meter)).ShouldBe(24);
    }

    [Fact]
    public void Devices_AreRead_AndAMalformedOneIsLeftOut()
    {
        var speakers = new NativeAudioDevice { IsDefault = 1 };
        speakers.Set("Speakers", "{0.0.0.00000000}.{speakers}");
        var nameless = new NativeAudioDevice();
        nameless.Set(string.Empty, "{0.0.0.00000000}.{nameless}");
        var odd = new NativeAudioDevice { IsDefault = 7 };
        odd.Set("Odd", "odd");
        var anonymous = new NativeAudioDevice();
        anonymous.Set("Anonymous", string.Empty);

        var devices = NativeAudioEngine.ToDevices([speakers, nameless, odd, anonymous], 9);

        devices.ShouldHaveSingleItem().ShouldBe(new AudioDevice("{0.0.0.00000000}.{speakers}", "Speakers", true));
    }

    [Theory]
    [InlineData(1u, AudioStartFailure.NoEncoder)]
    [InlineData(2u, AudioStartFailure.UnsupportedBitrate)]
    [InlineData(3u, AudioStartFailure.NoOutput)]
    [InlineData(4u, AudioStartFailure.DeviceMissing)]
    [InlineData(5u, AudioStartFailure.Platform)]
    [InlineData(99u, AudioStartFailure.Platform)]
    public void FailureCodes_AreRead(uint code, AudioStartFailure expected)
    {
        NativeAudioEngine.ToFailure(code).ShouldBe(expected);
        NativeAudioEngine.DescribeFailure(expected).ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(1u, AudioShareState.Ready)]
    [InlineData(2u, AudioShareState.Sounding)]
    [InlineData(3u, AudioShareState.Unavailable)]
    [InlineData(0u, AudioShareState.Unavailable)]
    public void StateCodes_AreRead_AndAnythingUnknownIsAProblem(uint code, AudioShareState expected)
    {
        NativeAudioEngine.ToState(code).ShouldBe(expected);
    }

    [Theory]
    [InlineData(0.25f, 0.25f)]
    [InlineData(-1f, 0f)]
    [InlineData(3f, 1f)]
    [InlineData(float.NaN, 0f)]
    [InlineData(float.PositiveInfinity, 0f)]
    public void Levels_AreKeptBetweenZeroAndOne(float level, float expected)
    {
        NativeAudioEngine.ToUnit(level).ShouldBe(expected);
    }

    [Fact]
    public void Failures_AreToldInWordsThatSayWhatToDo()
    {
        NativeAudioEngine.DescribeFailure(AudioStartFailure.NoEncoder).ShouldContain("Media Feature Pack");
        NativeAudioEngine.DescribeFailure(AudioStartFailure.DeviceMissing).ShouldContain("not connected");
        NativeAudioEngine.DescribeFailure(AudioStartFailure.EngineMissing).ShouldContain("cannot share sound");
        NativeAudioEngine.DescribeFailure(AudioStartFailure.NoOutput).ShouldContain("no sound output");
    }

    [Fact]
    public void AStartThatCannotHappen_SaysWhy()
    {
        var engine = new NativeAudioEngine();

        var refused = Should.Throw<AudioEngineException>(() => engine.Start(new AudioShareOptions(BitrateKbps: 100)));
        refused.Reason.ShouldBeOneOf(AudioStartFailure.UnsupportedBitrate, AudioStartFailure.EngineMissing);

        var missing = Should.Throw<AudioEngineException>(() => engine.Start(new AudioShareOptions(DeviceId: "not a device")));
        missing.Reason.ShouldBeOneOf(AudioStartFailure.DeviceMissing, AudioStartFailure.NoEncoder, AudioStartFailure.EngineMissing);
        Should.Throw<ArgumentNullException>(() => engine.Start(null!));
    }

    [Fact]
    public void ThisPcsOutputs_AndItsMute_AreReadWithoutChangingAnything()
    {
        var engine = new NativeAudioEngine();

        var devices = engine.ListDevices();
        devices.Count(device => device.IsDefault).ShouldBeLessThanOrEqualTo(1);

        if (engine.IsMuted(null) is { } muted)
        {
            engine.SetMuted(null, muted).ShouldBeTrue("set to what it already is");
        }

        engine.IsMuted("not a device").ShouldBeNull();
        engine.SetMuted("not a device", false).ShouldBeFalse();
        engine.SetMuted("not a device", true).ShouldBeFalse();
    }

    [Fact]
    public void ARealShare_GivesItsSetupThenPackets_AndStopsCleanly()
    {
        IAudioEngineSession session;
        try
        {
            session = new NativeAudioEngine().Start(new AudioShareOptions(StartOffsetUs: 2_000_000));
        }
        catch (AudioEngineException)
        {
            return;
        }

        using (session)
        {
            session.Config.ShouldBe(new byte[] { 0x11, 0x90 });
            var buffer = new byte[4096];
            var length = session.Next(buffer, TimeSpan.FromSeconds(2), out var time);
            length.ShouldBeGreaterThan(0);
            time.ShouldBeGreaterThanOrEqualTo(2_000_000);
            Should.Throw<MirrorEngineException>(() => session.Next(new byte[1], TimeSpan.FromSeconds(2), out _))
                .Message.ShouldContain("did not fit");

            var stats = session.ReadStats();
            stats.Packets.ShouldBeGreaterThan(0);
            stats.State.ShouldBeOneOf(AudioShareState.Ready, AudioShareState.Sounding);
            stats.Meter.ShouldBeInRange(0f, 1f);
            session.ReadProblem().ShouldBeNull();
            session.SetPaused(true);
            session.SetDelay(20);
            session.SetPaused(false);
        }

        Should.Throw<ObjectDisposedException>(() => session.ReadStats());
        Should.Throw<ObjectDisposedException>(() => session.Next(new byte[8], TimeSpan.Zero, out _));
        Should.Throw<ObjectDisposedException>(() => session.ReadProblem());
        Should.Throw<ObjectDisposedException>(() => session.SetPaused(true));
        Should.Throw<ObjectDisposedException>(() => session.SetDelay(1));
        Should.NotThrow(session.Dispose);
    }
}
