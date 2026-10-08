using System.Net;
using Avalonia.Headless.XUnit;
using Flint.App.ViewModels;
using Flint.Core;
using Flint.Core.Settings;
using Flint.Protocol;
using Flint.Session;
using Shouldly;
using static Flint.App.Tests.CastPageFixtures;

namespace Flint.App.Tests;

/// <summary>What a share sends: the picture mode, the chosen display, and never more than the TV can show.</summary>
public sealed class CastPageMirrorOptionsTests : IDisposable
{
    private static readonly DisplayInfo SideDisplay =
        new(2, 2, "Side", "side", 1920, 0, 2560, 1440, DisplayRotation.Upright, IsMain: false);

    private readonly SettingsService settings = new(new InMemoryAppSettingsStore());
    private readonly RecordingMirrorEngine engine = new();

    [Fact]
    public async Task AShare_SendsThePictureModeTheSettingsName_NoWiderThanTheTv()
    {
        await using var tv = new LoopbackReceiver { ScreenWidth = 1280 };
        var cast = await PairedPageAsync(tv);
        settings.Update(current => current with { Screen = current.Screen with { PictureMode = PictureMode.Game } });

        cast.TvScreenWidth.ShouldBe(1280);
        cast.MirrorOptionsFor(null).ShouldBe(new MirrorSessionOptions(0, 60, 16_000_000, 1280));
    }

    [Fact]
    public async Task AChosenDisplay_IsCapturedByItsIndex_AndItsOwnSizeIsTheLimitForNative()
    {
        await using var tv = new LoopbackReceiver { ScreenWidth = 3840 };
        var cast = await PairedPageAsync(tv);
        settings.Update(current => current with
        {
            Screen = current.Screen with { PictureMode = PictureMode.Custom, CustomSizeLimit = SizeLimit.Native },
        });

        var options = cast.MirrorOptionsFor(SideDisplay);

        options.OutputIndex.ShouldBe(2u);
        options.MaxWidth.ShouldBe(2560u);
    }

    [Fact]
    public void WithoutSettingsOrATv_TheEverydayModeIsSent()
    {
        var cast = new CastPageViewModel(
            BrowserFixtures.Prober(BrowserFixtures.EligibleDevice() with { Address = IPAddress.Loopback }),
            new NoRecentAddresses());

        cast.TvScreenWidth.ShouldBeNull("nothing is connected");
        cast.MirrorOptionsFor(null).ShouldBe(new MirrorSessionOptions(0, 30, 12_000_000, 1920));
    }

    [Fact]
    public async Task NothingBeingShared_HasNothingToChange()
    {
        await using var tv = new LoopbackReceiver();
        var cast = await PairedPageAsync(tv);

        cast.ChangeMirror(new MirrorSessionOptions(OutputIndex: 1)).ShouldBeFalse();
        engine.Started.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task ARunningShare_ChangesInPlace_AndSaysSoOnThePagesThread()
    {
        await using var tv = new LoopbackReceiver();
        var cast = await PairedPageAsync(tv);
        var switches = new List<MirrorSwitch>();
        cast.MirrorSwitched += switches.Add;
        var first = cast.MirrorOptionsFor(null);
        var sharing = cast.StartMirrorAsync(first);
        await tv.WaitForAsync<VideoConfigMessage>();

        var second = first with { OutputIndex = 1 };
        cast.ChangeMirror(second).ShouldBeTrue();
        await Until(() => switches.Count == 1);

        switches[0].ShouldBe(new MirrorSwitch(second, null));
        engine.Started.ShouldBe([first, second]);
        await cast.StopMirrorAsync(TestContext.Current.CancellationToken);
        await sharing;
        cast.ChangeMirror(first).ShouldBeFalse("the share has ended");
    }

    public void Dispose() => settings.Dispose();

    [AvaloniaFact]
    public async Task ARunningShare_PassesItsCountersOn_AndCarriesOnWithNobodyListening()
    {
        await using var tv = new LoopbackReceiver();
        var cast = await PairedAsync(tv, new MovingMirrorEngine());
        var sharing = cast.StartMirrorAsync(cast.MirrorOptionsFor(null));
        await tv.WaitForAsync<VideoConfigMessage>();

        // Nobody listening: a change, the TV's counters and frames all pass without anyone to tell.
        cast.ChangeMirror(cast.MirrorOptionsFor(null) with { OutputIndex = 1 }).ShouldBeTrue();
        await tv.SendAsync(new StatsMessage(1, 0, 0, 0));
        await Until(() => tv.Received.OfType<VideoConfigMessage>().Count() == 2);

        var counted = new List<MirrorSessionStats>();
        cast.MirrorStatsUpdated += counted.Add;
        await Until(() => counted.Count > 0);
        counted[^1].FramesEncoded.ShouldBeGreaterThan(0);
        cast.MirrorStatus!.ShouldStartWith("Mirroring: ");

        await cast.StopMirrorAsync(TestContext.Current.CancellationToken);
        await sharing;
    }

    private async Task<CastPageViewModel> PairedPageAsync(LoopbackReceiver tv)
    {
        var cast = await PairedAsync(tv, engine);
        cast.UseReconnect(new InMemoryKnownTvStore(), settings);
        return cast;
    }
}
