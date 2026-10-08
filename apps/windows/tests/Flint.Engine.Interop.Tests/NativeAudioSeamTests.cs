using System.Runtime.InteropServices;
using Flint.Core;
using Shouldly;

namespace Flint.Engine.Interop.Tests;

/// <summary>Sound across the boundary with an engine that refuses, answers oddly, or is missing.</summary>
public sealed class NativeAudioSeamTests
{
    public static TheoryData<string> Missing => ["missing", "export", "image", "marshal"];

    [Theory]
    [MemberData(nameof(Missing))]
    public void AnEngineThatCannotBeCalled_HasNoSound_AndSaysSoWithoutThrowing(string kind)
    {
        var engine = new NativeAudioEngine(new FakeExports { Throw = Thrown(kind) });

        engine.ListDevices().ShouldBeEmpty();
        engine.IsMuted(null).ShouldBeNull();
        engine.SetMuted(null, true).ShouldBeFalse();
        var refused = Should.Throw<AudioEngineException>(() => engine.Start(new AudioShareOptions()));
        refused.Reason.ShouldBe(AudioStartFailure.EngineMissing);
        refused.InnerException.ShouldBe(Thrown(kind), "the cause is kept");
    }

    [Fact]
    public void AFailureThatIsNotAMissingEngine_IsNotHidden()
    {
        var engine = new NativeAudioEngine(new FakeExports { Throw = new InvalidOperationException("a bug") });

        Should.Throw<InvalidOperationException>(engine.ListDevices);
        Should.Throw<InvalidOperationException>(() => engine.IsMuted(null));
    }

    [Fact]
    public void AnEngineThatRefuses_ListsNothing_AndKnowsNoMute()
    {
        var engine = new NativeAudioEngine(new FakeExports { Status = (int)FlintStatus.PlatformError });

        engine.ListDevices().ShouldBeEmpty();
        engine.IsMuted("speakers").ShouldBeNull();
        engine.SetMuted("speakers", true).ShouldBeFalse();
    }

    [Fact]
    public void TheMute_IsAskedOfTheNamedOutput_OrTheDefaultWhenNoneIsNamed()
    {
        var exports = new FakeExports { Muted = true };
        var engine = new NativeAudioEngine(exports);

        engine.IsMuted(null).ShouldBe(true);
        engine.SetMuted("speakers", false).ShouldBeTrue();

        exports.Calls.ShouldBe(["muted:", "set-muted:speakers=False"]);
    }

    [Theory]
    [InlineData((int)FlintStatus.Ok, 1u, AudioStartFailure.NoEncoder)]
    [InlineData((int)FlintStatus.PlatformError, 4u, AudioStartFailure.DeviceMissing)]
    public void AStartThatComesBackWithoutASession_SaysWhy(int status, uint failure, AudioStartFailure expected)
    {
        var exports = new FakeExports
        {
            StartStatus = status,
            Handle = 0,
            Failure = failure,
        };

        var refused = Should.Throw<AudioEngineException>(() => new NativeAudioEngine(exports).Start(new AudioShareOptions(DeviceId: "usb", DelayMilliseconds: -5)));

        refused.Reason.ShouldBe(expected);
        refused.Message.ShouldBe(NativeAudioEngine.DescribeFailure(expected));
        exports.Calls.ShouldBe(["start:usb kbps=128 offset=0 delay=0"]);
    }

    [Theory]
    [InlineData((int)FlintStatus.PlatformError, 2u)]
    [InlineData((int)FlintStatus.Ok, 0u)]
    [InlineData((int)FlintStatus.Ok, 17u)]
    public void AShareWithoutItsSetupData_IsStoppedAndRefused(int status, uint length)
    {
        var exports = new FakeExports { ConfigStatus = status, ConfigLength = length };

        var refused = Should.Throw<AudioEngineException>(() => new NativeAudioEngine(exports).Start(new AudioShareOptions()));

        refused.Reason.ShouldBe(AudioStartFailure.Platform);
        exports.Calls.ShouldContain("stop");
    }

    [Fact]
    public void AShare_ReadsPacketsCountersAndProblems_AndStopsOnce()
    {
        var exports = new FakeExports
        {
            NativeStats = new NativeAudioStats
            {
                Packets = ulong.MaxValue,
                Dropped = 3,
                Level = float.NaN,
                State = 3,
                Meter = 2,
            },
            ProblemText = "The chosen sound output was disconnected.",
        };
        var session = new NativeAudioEngine(exports).Start(new AudioShareOptions());

        session.Config.ShouldBe(new byte[] { 0x11, 0x90 });
        var buffer = new byte[8];
        session.Next(buffer, TimeSpan.FromMilliseconds(40), out var time).ShouldBe(3);
        time.ShouldBe(21_333);
        buffer[..3].ShouldBe(new byte[] { 1, 2, 3 });
        session.ReadStats().ShouldBe(new AudioShareStats(long.MaxValue, 3, 0, AudioShareState.Unavailable, 1));
        session.ReadProblem().ShouldBe("The chosen sound output was disconnected.");
        session.SetPaused(true);
        session.SetDelay(-10);
        session.Dispose();
        session.Dispose();

        exports.Calls.ShouldBe(["start: kbps=128 offset=0 delay=0", "config", "next:40", "stats", "problem", "paused:True", "delay:0", "stop"]);
    }

    [Fact]
    public void AProblemLongerThanItsRoom_IsCutShort_AndNoProblemIsNull()
    {
        var exports = new FakeExports { ProblemText = new string('x', 600) };
        using var session = new NativeAudioEngine(exports).Start(new AudioShareOptions());

        session.ReadProblem()!.Length.ShouldBe(512);
        exports.ProblemText = null;
        session.ReadProblem().ShouldBeNull();
    }

    [Fact]
    public void APacketThatDoesNotFit_OrAFailedRead_IsAnEngineFailure()
    {
        var exports = new FakeExports { NextStatus = (int)FlintStatus.BufferTooSmall, NextLength = 900 };
        using var session = new NativeAudioEngine(exports).Start(new AudioShareOptions());

        Should.Throw<MirrorEngineException>(() => session.Next(new byte[4], TimeSpan.Zero, out _))
            .Message.ShouldBe("A sound packet of 900 bytes did not fit a 4-byte buffer.");
        exports.NextStatus = (int)FlintStatus.InternalError;
        Should.Throw<MirrorEngineException>(() => session.Next(new byte[4], TimeSpan.Zero, out _))
            .Message.ShouldContain($"status {(int)FlintStatus.InternalError}");
    }

    private static Exception Thrown(string kind) => kind switch
    {
        "missing" => Cached.Missing,
        "export" => Cached.Export,
        "image" => Cached.Image,
        _ => Cached.Marshal,
    };

    private static class Cached
    {
        internal static readonly DllNotFoundException Missing = new();
        internal static readonly EntryPointNotFoundException Export = new();
        internal static readonly BadImageFormatException Image = new();
        internal static readonly MarshalDirectiveException Marshal = new();
    }

    /// <summary>An engine whose every answer a test sets, and which records what it was asked.</summary>
    private sealed class FakeExports : IAudioExports
    {
        public Exception? Throw { get; init; }

        public int Status { get; init; }

        public int StartStatus { get; init; }

        public nint Handle { get; init; } = 7;

        public uint Failure { get; init; }

        public int ConfigStatus { get; init; }

        public uint ConfigLength { get; init; } = 2;

        public int NextStatus { get; set; }

        public uint NextLength { get; init; } = 3;

        public bool Muted { get; init; }

        public NativeAudioStats NativeStats { get; init; }

        public string? ProblemText { get; set; }

        public List<string> Calls { get; } = [];

        public int Devices(Span<NativeAudioDevice> devices, out uint found)
        {
            Fail();
            found = 0;
            return Status;
        }

        public int Start(string deviceId, uint bitrateKbps, long startOffsetUs, uint delayMs, out nint handle, out uint failure)
        {
            Fail();
            Calls.Add($"start:{deviceId} kbps={bitrateKbps} offset={startOffsetUs} delay={delayMs}");
            handle = Handle;
            failure = Failure;
            return StartStatus;
        }

        public int Config(nint handle, Span<byte> buffer, out uint length)
        {
            Calls.Add("config");
            buffer[0] = 0x11;
            buffer[1] = 0x90;
            length = ConfigLength;
            return ConfigStatus;
        }

        public int Next(nint handle, Span<byte> buffer, uint timeoutMs, out uint length, out long timeUs)
        {
            Calls.Add($"next:{timeoutMs}");
            if (NextStatus == 0)
            {
                buffer[0] = 1;
                buffer[1] = 2;
                buffer[2] = 3;
            }

            length = NextLength;
            timeUs = 21_333;
            return NextStatus;
        }

        public int Stats(nint handle, out NativeAudioStats stats)
        {
            Calls.Add("stats");
            stats = NativeStats;
            return 0;
        }

        public int Problem(nint handle, Span<byte> buffer, out uint length)
        {
            Calls.Add("problem");
            var bytes = System.Text.Encoding.UTF8.GetBytes(ProblemText ?? string.Empty);
            length = (uint)bytes.Length;
            if (bytes.Length > buffer.Length)
            {
                bytes.AsSpan(0, buffer.Length).CopyTo(buffer);
                return (int)FlintStatus.BufferTooSmall;
            }

            bytes.CopyTo(buffer);
            return 0;
        }

        public int SetPaused(nint handle, bool paused)
        {
            Calls.Add($"paused:{paused}");
            return 0;
        }

        public int SetDelay(nint handle, uint delayMs)
        {
            Calls.Add($"delay:{delayMs}");
            return 0;
        }

        public int Stop(nint handle)
        {
            Calls.Add("stop");
            return 0;
        }

        int IAudioExports.Muted(string deviceId, out bool muted)
        {
            Fail();
            Calls.Add($"muted:{deviceId}");
            muted = Muted;
            return Status;
        }

        public int SetMuted(string deviceId, bool muted)
        {
            Fail();
            Calls.Add($"set-muted:{deviceId}={muted}");
            return Status;
        }

        private void Fail()
        {
            if (Throw is { } thrown)
            {
                throw thrown;
            }
        }
    }
}
