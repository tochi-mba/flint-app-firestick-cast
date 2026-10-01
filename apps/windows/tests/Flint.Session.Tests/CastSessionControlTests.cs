using Flint.Protocol;
using Shouldly;

namespace Flint.Session.Tests;

/// <summary>Playback controls and volume, as the TV's player receives them.</summary>
public sealed class CastSessionControlTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(TransportAction.Play)]
    [InlineData(TransportAction.Pause)]
    [InlineData(TransportAction.Stop)]
    [InlineData(TransportAction.Next)]
    [InlineData(TransportAction.Previous)]
    public async Task EachAction_ReachesTheTv_WithNoPosition(TransportAction action)
    {
        await using var tv = await FakeTv.StartAsync();
        await using var session = await tv.PairAsync();

        // A position given with an action that does not seek is not passed on.
        await session.SendTransportAsync(action, 5_000, Token);

        var control = (await tv.ReadAsync()).Message.ShouldBeOfType<ControlMessage>();
        control.Event.ShouldBe(new TransportControl(action, -1));
    }

    [Fact]
    public async Task ASeek_CarriesItsPosition()
    {
        await using var tv = await FakeTv.StartAsync();
        await using var session = await tv.PairAsync();

        await session.SendTransportAsync(TransportAction.SeekTo, 760_000, Token);

        var control = (await tv.ReadAsync()).Message.ShouldBeOfType<ControlMessage>();
        control.Event.ShouldBe(new TransportControl(TransportAction.SeekTo, 760_000));
    }

    [Fact]
    public async Task ASeekToZero_IsAllowed()
    {
        await using var tv = await FakeTv.StartAsync();
        await using var session = await tv.PairAsync();

        await session.SendTransportAsync(TransportAction.SeekTo, 0, Token);

        (await tv.ReadAsync()).Message.ShouldBeOfType<ControlMessage>()
            .Event.ShouldBe(new TransportControl(TransportAction.SeekTo, 0));
    }

    [Fact]
    public async Task ANegativeSeek_IsRefusedBeforeAnythingIsSent()
    {
        await using var tv = await FakeTv.StartAsync();
        await using var session = await tv.PairAsync();

        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => session.SendTransportAsync(TransportAction.SeekTo, -1, Token));
        await session.SendTransportAsync(TransportAction.Pause, cancellationToken: Token);

        // The first thing on the wire is the pause: the refused seek never left this PC.
        (await tv.ReadAsync()).Message.ShouldBeOfType<ControlMessage>()
            .Event.ShouldBe(new TransportControl(TransportAction.Pause));
    }

    [Theory]
    [InlineData(0.4f, 0.4f)]
    [InlineData(0f, 0f)]
    [InlineData(1f, 1f)]
    [InlineData(-0.5f, 0f)]
    [InlineData(1.7f, 1f)]
    [InlineData(float.PositiveInfinity, 1f)]
    [InlineData(float.NegativeInfinity, 0f)]
    public async Task Volume_IsClampedToTheRangeTheTvTakes(float asked, float sent)
    {
        await using var tv = await FakeTv.StartAsync();
        await using var session = await tv.PairAsync();

        await session.SetVolumeAsync(asked, Token);

        (await tv.ReadAsync()).Message.ShouldBeOfType<ControlMessage>()
            .Event.ShouldBe(new VolumeControl(sent));
    }

    [Fact]
    public async Task AVolumeThatIsNotANumber_IsRefused()
    {
        await using var tv = await FakeTv.StartAsync();
        await using var session = await tv.PairAsync();

        await Should.ThrowAsync<ArgumentException>(() => session.SetVolumeAsync(float.NaN, Token));
    }

    [Fact]
    public async Task SequenceNumbers_RiseByOne_AcrossTransportAndVolume_UnderConcurrentSenders()
    {
        await using var tv = await FakeTv.StartAsync();
        await using var session = await tv.PairAsync();

        await Task.WhenAll(Enumerable.Range(0, 50).Select(index => index % 2 == 0
            ? session.SendTransportAsync(TransportAction.Play, cancellationToken: Token)
            : session.SetVolumeAsync(0.5f, Token)));

        var numbers = new List<long>();
        for (var read = 0; read < 50; read++)
        {
            numbers.Add((await tv.ReadAsync()).Message.ShouldBeOfType<ControlMessage>().SequenceNumber);
        }

        numbers.Order().ShouldBe(Enumerable.Range(1, 50).Select(number => (long)number));
    }

    [Fact]
    public async Task OnAClosedSession_ControlsFailWithTheUsualError()
    {
        await using var tv = await FakeTv.StartAsync();
        var session = await tv.PairAsync();
        await session.DisposeAsync();

        (await Should.ThrowAsync<IOException>(() => session.SendTransportAsync(TransportAction.Play, cancellationToken: Token)))
            .Message.ShouldBe("The receiver session is no longer connected.");
        (await Should.ThrowAsync<IOException>(() => session.SetVolumeAsync(0.5f, Token)))
            .Message.ShouldBe("The receiver session is no longer connected.");
    }
}
