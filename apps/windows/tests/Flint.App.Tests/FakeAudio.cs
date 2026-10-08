using System.Collections.Concurrent;
using Flint.Core;

namespace Flint.App.Tests;

/// <summary>This PC's sound as a test scripts it: outputs, their mute, and the shares started.</summary>
internal sealed class FakeAudio : IAudioEngine
{
    private readonly ConcurrentDictionary<string, bool> muted = new(StringComparer.OrdinalIgnoreCase);

    public FakeAudio(params AudioDevice[] outputs)
    {
        Outputs = [.. outputs];
        foreach (var output in outputs)
        {
            muted[output.Id] = false;
        }
    }

    public List<AudioDevice> Outputs { get; }

    /// <summary>Every mute asked for, in order, as "id=true".</summary>
    public ConcurrentQueue<string> MuteCalls { get; } = new();

    /// <summary>The next start fails with this.</summary>
    public AudioEngineException? Refusal { get; set; }

    /// <summary>Whether setting a mute fails.</summary>
    public bool RefusesMute { get; set; }

    /// <summary>Whether reading a mute fails.</summary>
    public bool HidesMute { get; set; }

    public ConcurrentQueue<AudioShareOptions> Started { get; } = new();

    /// <summary>The session the latest start returned.</summary>
    public FakeShare? Share { get; private set; }

    public bool Muted(string id) => muted.GetValueOrDefault(id);

    /// <summary>Mutes or unmutes as the person would, from the taskbar.</summary>
    public void PersonSets(string id, bool value) => muted[id] = value;

    public IReadOnlyList<AudioDevice> ListDevices() => [.. Outputs];

    public IAudioEngineSession Start(AudioShareOptions options)
    {
        Started.Enqueue(options);
        if (Refusal is { } refusal)
        {
            throw refusal;
        }

        Share = new FakeShare();
        return Share;
    }

    public bool? IsMuted(string? deviceId) =>
        HidesMute ? null : muted.TryGetValue(deviceId ?? DefaultId(), out var value) ? value : null;

    public bool SetMuted(string? deviceId, bool value)
    {
        var id = deviceId ?? DefaultId();
        MuteCalls.Enqueue($"{id}={value}");
        if (RefusesMute || !muted.ContainsKey(id))
        {
            return false;
        }

        muted[id] = value;
        return true;
    }

    private string DefaultId() => Outputs.FirstOrDefault(output => output.IsDefault)?.Id ?? string.Empty;
}

/// <summary>A running share whose counters and problems a test sets.</summary>
internal sealed class FakeShare : IAudioEngineSession
{
    private readonly Lock gate = new();
    private AudioShareStats stats = new(0, 0, 0, AudioShareState.Ready);
    private int disposed;

    public IReadOnlyList<byte> Config { get; } = [0x11, 0x90];

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

    public bool Disposed => Volatile.Read(ref disposed) != 0;

    public ConcurrentQueue<bool> Pauses { get; } = new();

    public ConcurrentQueue<int> Delays { get; } = new();

    public int Next(Span<byte> buffer, TimeSpan timeout, out long presentationTimeUs)
    {
        // Nothing to send: the tests here are about what the page says, not what reaches the TV.
        Thread.Sleep(5);
        presentationTimeUs = 0;
        return 0;
    }

    public AudioShareStats ReadStats() => Stats;

    public string? ReadProblem() => Problem;

    public void SetPaused(bool paused) => Pauses.Enqueue(paused);

    public void SetDelay(int milliseconds) => Delays.Enqueue(milliseconds);

    public void Dispose() => Volatile.Write(ref disposed, 1);
}
