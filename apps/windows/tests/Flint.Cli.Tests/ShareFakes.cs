using System.Collections.Concurrent;
using Flint.Core;
using Flint.Protocol;
using Flint.Session;
using Shouldly;

namespace Flint.Cli.Tests;

/// <summary>Waits for something another thread does, failing rather than hanging.</summary>
internal static class Until
{
    public static async Task TrueAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the condition never came true");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }
}

/// <summary>A screen engine whose sessions encode a small frame a millisecond, or nothing, or refuse.</summary>
internal sealed class ScriptedScreen(bool encodes = true, MirrorEngineException? refusal = null) : IMirrorEngine
{
    private int ticks;

    public ConcurrentQueue<MirrorSessionOptions> Started { get; } = new();

    /// <summary>Each time a session was told whether to draw the pointer.</summary>
    public ConcurrentQueue<bool> Pointer { get; } = new();

    public int Ticks => Volatile.Read(ref ticks);

    public IMirrorEngineSession Start(MirrorSessionOptions options)
    {
        Started.Enqueue(options);
        return refusal is { } refused ? throw refused : new Session(this, encodes);
    }

    private sealed class Session(ScriptedScreen screen, bool encodes) : IMirrorEngineSession, IMirrorClock
    {
        private long frames;

        public VideoCodec Codec => VideoCodec.H264;

        public MirrorEncoderKind EncoderKind => MirrorEncoderKind.Software;

        public int Width => 1280;

        public int Height => 720;

        public IReadOnlyList<byte[]> CodecSpecificData { get; } = [[0, 0, 0, 1, 0x67]];

        public long ReadElapsedUs() => 0;

        public MirrorTick Next(Span<byte> buffer)
        {
            Interlocked.Increment(ref screen.ticks);
            Thread.Sleep(1);
            if (!encodes)
            {
                return new MirrorTick(MirrorTickKind.Nothing, 0, false, 0);
            }

            frames++;
            return new MirrorTick(MirrorTickKind.Encoded, 4, frames == 1, frames * 33_333);
        }

        public void RequestKeyFrame()
        {
        }

        public void SetPause(MirrorPause pause)
        {
        }

        public void SetShowPointer(bool show) => screen.Pointer.Enqueue(show);

        public MirrorSessionStats ReadStats() => new(frames, 0, 0, frames * 4);

        public void Dispose()
        {
        }
    }
}

/// <summary>A sound engine whose one session yields a small packet each time it is asked.</summary>
internal sealed class PacketSound : IAudioEngine
{
    private Session? session;

    public bool Started => Volatile.Read(ref session) is not null;

    /// <summary>Whether a session was started and has not yet been put away.</summary>
    public bool Running => Volatile.Read(ref session) is { Disposed: false };

    public IReadOnlyList<AudioDevice> ListDevices() => [];

    public IAudioEngineSession Start(AudioShareOptions options)
    {
        var started = new Session();
        Volatile.Write(ref session, started);
        return started;
    }

    public bool? IsMuted(string? deviceId) => null;

    public bool SetMuted(string? deviceId, bool muted) => false;

    private sealed class Session : IAudioEngineSession
    {
        private long packets;
        private int disposed;

        public bool Disposed => Volatile.Read(ref disposed) != 0;

        public IReadOnlyList<byte> Config { get; } = [0x11, 0x90];

        public int Next(Span<byte> buffer, TimeSpan timeout, out long presentationTimeUs)
        {
            Thread.Sleep(1);
            presentationTimeUs = Interlocked.Increment(ref packets) * 21_333;
            buffer[..4].Fill(0x21);
            return 4;
        }

        public AudioShareStats ReadStats() => new(Volatile.Read(ref packets), 0, 0.5f, AudioShareState.Sounding);

        public string? ReadProblem() => null;

        public void SetPaused(bool paused)
        {
        }

        public void SetDelay(int milliseconds)
        {
        }

        public void Dispose() => Volatile.Write(ref disposed, 1);
    }
}

/// <summary>A TV that counts the frames and sound packets it is sent.</summary>
internal sealed class CountingTv : IMirrorTransport, IAudioTransport
{
    private int frames;
    private int packets;

    public int Frames => Volatile.Read(ref frames);

    public int Packets => Volatile.Read(ref packets);

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

    public Task SendAudioConfigAsync(IReadOnlyList<byte> config, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SendAudioAsync(long presentationTimeUs, BinaryData data, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref packets);
        return Task.CompletedTask;
    }
}
