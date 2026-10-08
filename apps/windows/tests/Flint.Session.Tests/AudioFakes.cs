using System.Collections.Concurrent;
using Flint.Core;
using Flint.Protocol;
using Shouldly;

namespace Flint.Session.Tests;

/// <summary>A clock that moves only when a test moves it.</summary>
internal sealed class SteppedTime : TimeProvider
{
    private long now = 1_000_000;

    public override long GetTimestamp() => Volatile.Read(ref now);

    public void Advance(TimeSpan by) => Interlocked.Add(ref now, (long)(by.TotalSeconds * TimestampFrequency));
}

/// <summary>An audio engine that starts the one session a test gives it, or refuses.</summary>
internal sealed class FakeAudioEngine(FakeAudioSession session) : IAudioEngine
{
    private AudioShareOptions? started;

    public AudioEngineException? Refusal { get; init; }

    public AudioShareOptions? Started => Volatile.Read(ref started);

    public IReadOnlyList<AudioDevice> ListDevices() => [];

    public IAudioEngineSession Start(AudioShareOptions options)
    {
        Volatile.Write(ref started, options);
        return Refusal is { } refusal ? throw refusal : session;
    }

    public bool? IsMuted(string? deviceId) => null;

    public bool SetMuted(string? deviceId, bool muted) => false;
}

/// <summary>A sound session whose packets, state and failures a test scripts.</summary>
internal sealed class FakeAudioSession : IAudioEngineSession
{
    private readonly ConcurrentQueue<(byte[] Data, long Time)> packets = new();
    private readonly Lock gate = new();
    private int nextCalls;
    private int disposed;
    private AudioShareStats stats = new(0, 0, 0, AudioShareState.Ready);

    public IReadOnlyList<byte> Config { get; } = [0x11, 0x90];

    /// <summary>What the session reports, set whole so the worker never reads half of a change.</summary>
    public AudioShareStats Stats
    {
        get
        {
            lock (gate)
            {
                return stats;
            }
        }

        set
        {
            lock (gate)
            {
                stats = value;
            }
        }
    }

    public string? Problem { get; set; }

    public MirrorEngineException? Failure { get; set; }

    public ConcurrentQueue<bool> PauseCalls { get; } = new();

    public ConcurrentQueue<int> DelayCalls { get; } = new();

    public bool Disposed => Volatile.Read(ref disposed) != 0;

    public int NextCalls => Volatile.Read(ref nextCalls);

    public int Waiting => packets.Count;

    public void Add(byte[] data, long time) => packets.Enqueue((data, time));

    public int Next(Span<byte> buffer, TimeSpan timeout, out long presentationTimeUs)
    {
        Interlocked.Increment(ref nextCalls);
        if (Failure is { } failure)
        {
            throw failure;
        }

        if (packets.TryDequeue(out var packet))
        {
            packet.Data.CopyTo(buffer);
            presentationTimeUs = packet.Time;
            return packet.Data.Length;
        }

        // A short wait stands in for the engine's, so a test loop turns quickly but never spins.
        Thread.Sleep(1);
        presentationTimeUs = 0;
        return 0;
    }

    public AudioShareStats ReadStats() => Stats;

    public string? ReadProblem() => Problem;

    public void SetPaused(bool paused) => PauseCalls.Enqueue(paused);

    public void SetDelay(int milliseconds) => DelayCalls.Enqueue(milliseconds);

    public void Dispose() => Volatile.Write(ref disposed, 1);
}

/// <summary>A receiver that records what sound it was sent, in order.</summary>
internal sealed class RecordingAudioTransport : IAudioTransport
{
    private int connected = 1;

    public ConcurrentQueue<WireMessage> Sent { get; } = new();

    public Exception? SendFailure { get; set; }

    public bool IsConnected
    {
        get => Volatile.Read(ref connected) != 0;
        set => Volatile.Write(ref connected, value ? 1 : 0);
    }

    public IReadOnlyList<AudioPacket> Packets => [.. Sent.OfType<AudioPacket>()];

    public Task SendAudioConfigAsync(IReadOnlyList<byte> config, CancellationToken cancellationToken = default)
    {
        Sent.Enqueue(new AudioConfigMessage(CodecId.AacLc, 48_000, 2, BinaryData.From([.. config])));
        return Task.CompletedTask;
    }

    public Task SendAudioAsync(long presentationTimeUs, BinaryData data, CancellationToken cancellationToken = default)
    {
        if (SendFailure is { } failure)
        {
            throw failure;
        }

        Sent.Enqueue(new AudioPacket(presentationTimeUs, data));
        return Task.CompletedTask;
    }
}

/// <summary>Waiting for a worker thread to get somewhere.</summary>
internal static class Eventually
{
    public static async Task TrueAsync(Func<bool> condition, string? because = null)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, because);
            await Task.Delay(2, TestContext.Current.CancellationToken);
        }
    }
}
