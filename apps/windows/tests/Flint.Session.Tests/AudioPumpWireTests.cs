using Flint.Core;
using Flint.Protocol;
using Shouldly;

namespace Flint.Session.Tests;

/// <summary>Sound beside the picture on one real receiver session.</summary>
public sealed class AudioPumpWireTests
{
    private const int Packets = 200;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SoundAndPicture_SentAtOnce_ReachTheTvAsWholeMessages()
    {
        await using var tv = await FakeTv.StartAsync();
        await using var session = await tv.PairAsync();
        var sound = new FakeAudioSession();
        for (var index = 1; index <= Packets; index++)
        {
            sound.Add(Filled(300, index), index * 21_333L);
        }

        using var stop = new CancellationTokenSource();
        var pump = new AudioPump(new FakeAudioEngine(sound)).RunAsync(session, new AudioShareOptions(), null, stop.Token);
        var picture = Task.Run(async () =>
        {
            for (var index = 1; index <= Packets; index++)
            {
                await session.SendVideoAsync(index, index == 1, BinaryData.From(Filled(5_000, index)), Token);
            }
        }, Token);

        // Any two messages interleaved on the wire would fail to decode here.
        var audio = new List<AudioPacket>();
        var video = 0;
        while (audio.Count < Packets || video < Packets)
        {
            switch ((await tv.ReadAsync()).Message)
            {
                case AudioConfigMessage config:
                    audio.ShouldBeEmpty("the configuration comes before any sound");
                    config.ShouldBe(new AudioConfigMessage(CodecId.AacLc, 48_000, 2, BinaryData.From([0x11, 0x90])));
                    break;
                case AudioPacket packet:
                    Uniform(packet.Data).ShouldBeTrue();
                    audio.Add(packet);
                    break;
                case VideoPacket frame:
                    Uniform(frame.Data).ShouldBeTrue();
                    video++;
                    break;
            }
        }

        await stop.CancelAsync();
        (await pump).Problem.ShouldBeNull();
        await picture;
        audio.Select(packet => packet.PresentationTimeUs).ShouldBe(Enumerable.Range(1, Packets).Select(index => index * 21_333L));
    }

    private static bool Uniform(BinaryData data)
    {
        var bytes = data.ToArray();
        return bytes.Length > 0 && bytes.All(value => value == bytes[0]);
    }

    private static byte[] Filled(int length, int index)
    {
        var bytes = new byte[length];
        Array.Fill(bytes, (byte)index);
        return bytes;
    }
}
