using System.Net;
using Avalonia.Threading;
using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.Core;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>A Cast page paired with a loopback receiver, and waiting for it to catch up.</summary>
internal static class CastPageFixtures
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>A Cast page that has probed and paired with <paramref name="receiver"/>.</summary>
    public static async Task<CastPageViewModel> PairedAsync(
        LoopbackReceiver receiver,
        IMirrorEngine? engine = null,
        TimeProvider? time = null)
    {
        var cast = new CastPageViewModel(
            BrowserFixtures.Prober(BrowserFixtures.EligibleDevice() with { Address = IPAddress.Loopback }),
            new NoRecentAddresses(),
            mirrorEngine: engine,
            receiverInstaller: new OfflineReceiverInstaller(),
            time: time);
        await Pair(cast, receiver);
        return cast;
    }

    /// <summary>Probes and pairs an existing Cast page with <paramref name="receiver"/>.</summary>
    public static async Task Pair(CastPageViewModel cast, LoopbackReceiver receiver)
    {
        await cast.ProbeCommand.ExecuteAsync(null);
        cast.PairingCode = "123456";
        cast.ReceiverPort = receiver.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await cast.ConnectCommand.ExecuteAsync(null);
        cast.IsSessionConnected.ShouldBeTrue(cast.Failure ?? "the loopback receiver should have paired");
    }

    /// <summary>Waits for the page to catch up with something the receiver did.</summary>
    /// <remarks>
    /// What the receiver does arrives on the session's receive loop and is handled after an await,
    /// so the page changes shortly after the TV does. On the UI thread the dispatcher has to be
    /// pumped for it.
    /// </remarks>
    public static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the page did not catch up with the TV");
            if (Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.RunJobs();
            }

            await Task.Delay(10, Token);
        }
    }
}

/// <summary>An address store that remembers nothing.</summary>
internal sealed class NoRecentAddresses : IRecentAddressStore
{
    public IReadOnlyList<RecentAddress> Load() => [];

    public void Remember(RecentAddress address)
    {
    }

    public void Clear()
    {
    }
}

/// <summary>An engine whose screen changes about thirty times a second, so frames reach the TV.</summary>
internal sealed class MovingMirrorEngine : IMirrorEngine
{
    public IMirrorEngineSession Start(MirrorSessionOptions options) => new MovingSession();

    private sealed class MovingSession : IMirrorEngineSession
    {
        private long frames;

        public VideoCodec Codec => VideoCodec.H264;

        public MirrorEncoderKind EncoderKind => MirrorEncoderKind.Hardware;

        public int Width => 1280;

        public int Height => 720;

        public IReadOnlyList<byte[]> CodecSpecificData { get; } = [[0, 0, 0, 1, 0x67], [0, 0, 0, 1, 0x68]];

        public MirrorTick Next(Span<byte> buffer)
        {
            Thread.Sleep(30);
            buffer[..16].Fill(0xAB);
            frames++;
            return new MirrorTick(MirrorTickKind.Encoded, 16, frames == 1, frames * 33_333);
        }

        public void RequestKeyFrame()
        {
        }

        public MirrorSessionStats ReadStats() => new(frames, 0, 0, frames * 16);

        public void Dispose()
        {
        }
    }
}

/// <summary>An engine that records what each share asked for and then shows a still screen.</summary>
internal sealed class RecordingMirrorEngine : IMirrorEngine
{
    public List<MirrorSessionOptions> Started { get; } = [];

    public IMirrorEngineSession Start(MirrorSessionOptions options)
    {
        lock (Started)
        {
            Started.Add(options);
        }

        return new StillSession();
    }

    private sealed class StillSession : IMirrorEngineSession
    {
        public VideoCodec Codec => VideoCodec.H264;

        public MirrorEncoderKind EncoderKind => MirrorEncoderKind.Hardware;

        public int Width => 1280;

        public int Height => 720;

        public IReadOnlyList<byte[]> CodecSpecificData { get; } = [[0, 0, 0, 1, 0x67], [0, 0, 0, 1, 0x68]];

        public MirrorTick Next(Span<byte> buffer) => MirrorTick.Nothing;

        public void RequestKeyFrame()
        {
        }

        public MirrorSessionStats ReadStats() => default;

        public void Dispose()
        {
        }
    }
}
