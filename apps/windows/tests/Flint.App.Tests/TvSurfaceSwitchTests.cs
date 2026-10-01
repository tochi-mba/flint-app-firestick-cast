using System.Net;
using System.Reflection;
using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.Core;
using Flint.Protocol;
using Flint.Session.Browser;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// Nothing on the TV is replaced without the person saying so. Every way of claiming it asks first
/// when something else is showing, keeping it leaves the TV exactly as it was, and switching asks
/// once however many steps the switch takes.
/// </summary>
public sealed class TvSurfaceSwitchTests
{
    private const string StartPage = "https://www.google.com/";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Current_IsWhatThisPcHasOnTheTv()
    {
        var tv = await TvAsync();
        tv.Coordinator.Current.ShouldBe(TvSurfaceKind.None);

        SetMirroring(tv.Cast, true);
        tv.Coordinator.Current.ShouldBe(TvSurfaceKind.Mirror);
        SetMirroring(tv.Cast, false);

        SetMediaPlaying(tv.Cast, true);
        tv.Coordinator.Current.ShouldBe(TvSurfaceKind.Media);
        SetMediaPlaying(tv.Cast, false);

        await OpenPageAsync(tv.Browser);
        tv.Coordinator.Current.ShouldBe(TvSurfaceKind.Browser);
    }

    [Fact]
    public async Task TakingAnEmptyTv_AsksNothing()
    {
        var tv = await TvAsync();

        (await tv.Coordinator.TakeAsync(TvSurfaceKind.Mirror, Token)).ShouldBeTrue();

        tv.Questions.ShouldBe(0);
    }

    [Fact]
    public async Task TakingWhatTheTvAlreadyShows_AsksNothingAndKeepsIt()
    {
        var tv = await TvAsync();
        await OpenPageAsync(tv.Browser);

        (await tv.Coordinator.TakeAsync(TvSurfaceKind.Browser, Token)).ShouldBeTrue();

        tv.Questions.ShouldBe(0);
        tv.Browser.HasOpenBrowserSurface.ShouldBeTrue();
    }

    [Fact]
    public async Task TakingWhileSomethingElseShows_Asks_AndKeepingChangesNothing()
    {
        var tv = await TvAsync();
        SetMirroring(tv.Cast, true);

        var taking = tv.Coordinator.TakeAsync(TvSurfaceKind.Browser, Token);

        tv.Prompt.IsOpen.ShouldBeTrue();
        tv.Prompt.Body.ShouldBe("Living Room is showing your screen. Opening the browser stops the mirror.");
        tv.Prompt.KeepCommand.Execute(null);
        (await taking).ShouldBeFalse();
        tv.Cast.IsMirroring.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TakingBeforeATvIsKnown_UsesTheGenericTvName(bool reportWithoutDevice)
    {
        var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        if (reportWithoutDevice)
        {
            await shell.Cast.ProbeCommand.ExecuteAsync(null);
            var report = shell.Cast.Report! with { Device = null };
            typeof(CastPageViewModel).GetProperty(nameof(CastPageViewModel.Report))!.SetValue(shell.Cast, report);
        }

        var prompt = new SurfaceSwitchPrompt();
        var coordinator = new ModeSessionCoordinator(shell.Cast, shell.Browser, prompt);
        SetMirroring(shell.Cast, true);

        var taking = coordinator.TakeAsync(TvSurfaceKind.Browser, Token);

        prompt.Body.ShouldBe("The TV is showing your screen. Opening the browser stops the mirror.");
        prompt.KeepCommand.Execute(null);
        (await taking).ShouldBeFalse();
    }

    [Fact]
    public async Task TakingWhileSomethingElseShows_AndSwitching_StopsIt()
    {
        var tv = await TvAsync();
        SetMediaPlaying(tv.Cast, true);

        var taking = tv.Coordinator.TakeAsync(TvSurfaceKind.Mirror, Token);

        tv.Prompt.Title.ShouldBe("Mirror your screen instead?");
        tv.Prompt.ConfirmCommand.Execute(null);
        (await taking).ShouldBeTrue();
        tv.Cast.IsMediaPlaying.ShouldBeFalse();
    }

    [Fact]
    public async Task WithoutAPrompt_TakingProceedsWithoutAsking()
    {
        var tv = await TvAsync();
        var silent = new ModeSessionCoordinator(tv.Cast, tv.Browser);
        SetMirroring(tv.Cast, true);

        (await silent.TakeAsync(TvSurfaceKind.Browser, Token)).ShouldBeTrue();

        tv.Cast.IsMirroring.ShouldBeFalse();
        silent.Prompt.ShouldBeNull();
    }

    [Fact]
    public async Task OpeningAPageWhileMirroring_Asks_AndKeepingSendsNothingAndKeepsTheAddress()
    {
        var tv = await TvAsync();
        SetMirroring(tv.Cast, true);
        tv.Browser.Address = "https://example.test/next";
        var sent = tv.Remote.Commands.Count;

        var opening = tv.Browser.NavigateCommand.ExecuteAsync(null);
        tv.Prompt.Title.ShouldBe("Switch the TV to the browser?");
        tv.Prompt.KeepCommand.Execute(null);
        await opening;

        tv.Remote.Commands.Count.ShouldBe(sent);
        tv.Cast.IsMirroring.ShouldBeTrue();
        tv.Browser.HasOpenBrowserSurface.ShouldBeFalse();
        tv.Browser.Address.ShouldBe("https://example.test/next");
    }

    [Fact]
    public async Task OpeningAPageWhileMirroring_AndSwitching_StopsTheMirrorAndOpensIt()
    {
        var tv = await TvAsync();
        SetMirroring(tv.Cast, true);
        tv.Browser.Address = "https://example.test/next";

        var opening = tv.Browser.NavigateCommand.ExecuteAsync(null);
        tv.Prompt.ConfirmCommand.Execute(null);
        await opening;

        tv.Cast.IsMirroring.ShouldBeFalse();
        tv.Browser.HasOpenBrowserSurface.ShouldBeTrue();
        tv.Remote.Commands.ShouldContain(command =>
            command.Action == BrowserCommandAction.Open && command.Url == "https://example.test/next");
    }

    [Fact]
    public async Task BrowserAction_WhenTheSurfaceClosesDuringTheQuestion_SendsNothingToTheGonePage()
    {
        var tv = await TvAsync();
        await OpenPageAsync(tv.Browser);
        SetMirroring(tv.Cast, true);
        var before = tv.Remote.Commands.Count;

        var reloading = tv.Browser.ReloadCommand.ExecuteAsync(null);
        tv.Prompt.IsOpen.ShouldBeTrue();
        await tv.Browser.StopBrowserSurfaceAsync(Token);
        tv.Prompt.ConfirmCommand.Execute(null);
        await reloading;

        tv.Cast.IsMirroring.ShouldBeFalse("switching was still accepted");
        tv.Browser.HasOpenBrowserSurface.ShouldBeFalse();
        tv.Remote.Commands.Skip(before).ShouldContain(command => command.Action == BrowserCommandAction.Close);
        tv.Remote.Commands.Skip(before).ShouldNotContain(command => command.Action == BrowserCommandAction.Reload);
    }

    [Fact]
    public async Task BrowserAction_WhenMirrorAlsoAppearsActive_KeepingSendsNothing()
    {
        var tv = await TvAsync();
        await OpenPageAsync(tv.Browser);
        SetMirroring(tv.Cast, true);
        var before = tv.Remote.Commands.Count;

        var reloading = tv.Browser.ReloadCommand.ExecuteAsync(null);
        tv.Prompt.KeepCommand.Execute(null);
        await reloading;

        tv.Cast.IsMirroring.ShouldBeTrue();
        tv.Browser.HasOpenBrowserSurface.ShouldBeTrue();
        tv.Remote.Commands.Count.ShouldBe(before);
    }

    [Fact]
    public async Task TabsHeldFromBeforeTheMirror_AskOnce_AndKeepingOpensNothing()
    {
        var tv = await TvAsync();
        await HoldTwoTabsUnderAMirrorAsync(tv);
        var sent = tv.Remote.Commands.Count;
        var tabRequests = tv.Remote.Cockpit!.TabRequests.Count;

        var selecting = tv.Browser.Tabs.SelectTabCommand.ExecuteAsync(1);
        tv.Prompt.KeepCommand.Execute(null);
        await selecting;

        tv.Questions.ShouldBe(1);
        tv.Prompt.IsOpen.ShouldBeFalse();
        tv.Remote.Commands.Count.ShouldBe(sent);
        tv.Remote.Cockpit.TabRequests.Count.ShouldBe(tabRequests);
        tv.Cast.IsMirroring.ShouldBeTrue();
    }

    [Fact]
    public async Task TabsHeldFromBeforeTheMirror_AndSwitching_ComeBackAfterOneQuestion()
    {
        var tv = await TvAsync();
        await HoldTwoTabsUnderAMirrorAsync(tv);

        var selecting = tv.Browser.Tabs.SelectTabCommand.ExecuteAsync(1);
        tv.Prompt.ConfirmCommand.Execute(null);
        await selecting;

        tv.Questions.ShouldBe(1, "one question for the whole session, not one per tab");
        tv.Cast.IsMirroring.ShouldBeFalse();
        tv.Remote.Commands.ShouldContain(command =>
            command.Action == BrowserCommandAction.Open && command.Url == "https://example.test/one");
        tv.Remote.Cockpit!.TabRequests.ShouldContain(request =>
            request.Operation == BrowserTabOperation.New && request.Url == "https://example.test/two");
    }

    [Fact]
    public async Task ShowingTheBrowserWithNothingHeld_OpensTheStartPage()
    {
        var tv = await TvAsync();

        await tv.Browser.ShowOnTvAsync(Token);

        tv.Questions.ShouldBe(0);
        tv.Remote.Commands.ShouldContain(command =>
            command.Action == BrowserCommandAction.Open && command.Url == StartPage);
    }

    [Fact]
    public async Task StartingTheMirrorWhileTheBrowserShows_Asks_AndKeepingLeavesTheBrowser()
    {
        await using var receiver = new LoopbackReceiver();
        var tv = await PairedTvAsync(receiver);
        await OpenPageAsync(tv.Browser);

        var starting = tv.Cast.StartScreenSessionCommand.ExecuteAsync(null);
        tv.Prompt.Body.ShouldBe(
            "Living Room is showing the browser. Mirroring closes it; your tabs are kept for when you come back.");
        tv.Prompt.KeepCommand.Execute(null);
        await starting;

        tv.Cast.IsMirroring.ShouldBeFalse();
        tv.Browser.HasOpenBrowserSurface.ShouldBeTrue();
        tv.Remote.Commands.ShouldNotContain(command => command.Action == BrowserCommandAction.Close);
    }

    [Fact]
    public async Task PlayingAFileWhileTheBrowserShows_Asks_AndKeepingSendsNothing()
    {
        await using var receiver = new LoopbackReceiver();
        var tv = await PairedTvAsync(receiver);
        await OpenPageAsync(tv.Browser);
        var status = tv.Cast.MediaStatus;

        var playing = tv.Cast.LoadMediaFileCommand.ExecuteAsync(@"C:\videos\holiday.mp4");
        tv.Prompt.Title.ShouldBe("Play this on the TV instead?");
        tv.Prompt.KeepCommand.Execute(null);
        await playing;

        tv.Cast.IsMediaPlaying.ShouldBeFalse();
        tv.Cast.MediaStatus.ShouldBe(status);
        tv.Browser.HasOpenBrowserSurface.ShouldBeTrue();
    }

    [Fact]
    public async Task ArrivingAtWebWhileMirroring_AsksToSwitch_AndSwitchingPutsTheBrowserUp()
    {
        var tv = await TvAsync();
        SetMirroring(tv.Cast, true);

        var arriving = tv.Coordinator.OfferOnArrivalAsync(TvSurfaceKind.Browser, () => true);
        await tv.AskedAsync();
        tv.Prompt.Title.ShouldBe("Switch the TV to the browser?");
        tv.Prompt.ConfirmCommand.Execute(null);
        await arriving;

        tv.Cast.IsMirroring.ShouldBeFalse();
        tv.Remote.Commands.ShouldContain(command =>
            command.Action == BrowserCommandAction.Open && command.Url == StartPage);
    }

    [Fact]
    public async Task ArrivingAtWebWhileMirroring_AndKeeping_LeavesTheMirror()
    {
        var tv = await TvAsync();
        SetMirroring(tv.Cast, true);
        var sent = tv.Remote.Commands.Count;

        var arriving = tv.Coordinator.OfferOnArrivalAsync(TvSurfaceKind.Browser, () => true);
        await tv.AskedAsync();
        tv.Prompt.KeepCommand.Execute(null);
        await arriving;

        tv.Cast.IsMirroring.ShouldBeTrue();
        tv.Remote.Commands.Count.ShouldBe(sent);
    }

    [Fact]
    public async Task ArrivingAtWebWithNothingOnTheTv_AsksNothingAndOpensNothing()
    {
        var tv = await TvAsync();
        var sent = tv.Remote.Commands.Count;

        await tv.Coordinator.OfferOnArrivalAsync(TvSurfaceKind.Browser, () => true);

        tv.Questions.ShouldBe(0);
        tv.Remote.Commands.Count.ShouldBe(sent);
    }

    [Fact]
    public async Task LeavingWebBeforeTheBrowserIsBack_AsksNothing()
    {
        var tv = await TvAsync();
        SetMirroring(tv.Cast, true);

        await tv.Coordinator.OfferOnArrivalAsync(TvSurfaceKind.Browser, () => false);

        tv.Questions.ShouldBe(0);
        tv.Cast.IsMirroring.ShouldBeTrue();
    }

    [Fact]
    public async Task ArrivingAtScreenWhileTheBrowserShows_AsksToMirror()
    {
        await using var receiver = new LoopbackReceiver();
        var tv = await PairedTvAsync(receiver);
        await OpenPageAsync(tv.Browser);

        var arriving = tv.Coordinator.OfferOnArrivalAsync(TvSurfaceKind.Mirror, () => true);
        tv.Prompt.Title.ShouldBe("Mirror your screen instead?");
        tv.Prompt.KeepCommand.Execute(null);
        await arriving;

        tv.Cast.IsMirroring.ShouldBeFalse();
        tv.Browser.HasOpenBrowserSurface.ShouldBeTrue();
    }

    [Fact]
    public async Task ArrivingAtScreenWhenNothingCanMirror_AsksNothing()
    {
        var tv = await TvAsync();
        await OpenPageAsync(tv.Browser);
        tv.Cast.CanStartMirrorNow.ShouldBeFalse("no session is paired");

        await tv.Coordinator.OfferOnArrivalAsync(TvSurfaceKind.Mirror, () => true);

        tv.Questions.ShouldBe(0);
    }

    [Theory]
    [InlineData(TvSurfaceKind.Media)]
    [InlineData(TvSurfaceKind.None)]
    public async Task ArrivingAtAPageThatPutsNothingUpOnArrival_AsksNothing(TvSurfaceKind page)
    {
        var tv = await TvAsync();
        SetMirroring(tv.Cast, true);

        await tv.Coordinator.OfferOnArrivalAsync(page, () => true);

        tv.Questions.ShouldBe(0);
        tv.Cast.IsMirroring.ShouldBeTrue();
    }

    [Fact]
    public async Task TheNotice_SaysWhatTheTvShows_OnlyWhereItIsNotThatPagesOwn()
    {
        var tv = await TvAsync();
        tv.Coordinator.NoticeFor(TvSurfaceKind.Browser).ShouldBeNull("nothing is on the TV");

        SetMirroring(tv.Cast, true);
        tv.Coordinator.NoticeFor(TvSurfaceKind.Browser).ShouldBe("Living Room is showing your screen.");
        tv.Coordinator.NoticeFor(TvSurfaceKind.Media).ShouldBe("Living Room is showing your screen.");
        tv.Coordinator.NoticeFor(TvSurfaceKind.Mirror).ShouldBeNull("it is that page's own");
        tv.Coordinator.NoticeFor(TvSurfaceKind.None).ShouldBeNull("that page puts nothing on the TV");
        SetMirroring(tv.Cast, false);

        SetMediaPlaying(tv.Cast, true);
        tv.Coordinator.NoticeFor(TvSurfaceKind.Browser).ShouldBe("Living Room is playing a file from this PC.");
        SetMediaPlaying(tv.Cast, false);

        await OpenPageAsync(tv.Browser);
        tv.Coordinator.NoticeFor(TvSurfaceKind.Mirror).ShouldBe("Living Room is showing the browser.");
    }

    [Fact]
    public void TheNotice_ForATvNotFoundYet_SaysTheTv()
    {
        var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        var coordinator = new ModeSessionCoordinator(shell.Cast, shell.Browser);
        SetMirroring(shell.Cast, true);

        coordinator.NoticeFor(TvSurfaceKind.Browser).ShouldBe("The TV is showing your screen.");
    }

    [Fact]
    public async Task TheNotice_ForATvWithoutAReadableName_SaysTheTv()
    {
        var device = BrowserFixtures.EligibleDevice() with { FriendlyName = string.Empty };
        var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(device));
        await shell.Cast.ProbeCommand.ExecuteAsync(null);
        var coordinator = new ModeSessionCoordinator(shell.Cast, shell.Browser);
        SetMirroring(shell.Cast, true);

        coordinator.NoticeFor(TvSurfaceKind.Browser).ShouldBe("The TV is showing your screen.");
    }

    [Fact]
    public async Task TheNotice_ForAReportWithoutADevice_SaysTheTv()
    {
        var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        await shell.Cast.ProbeCommand.ExecuteAsync(null);
        typeof(CastPageViewModel).GetProperty(nameof(CastPageViewModel.Report))!
            .SetValue(shell.Cast, shell.Cast.Report! with { Device = null });
        var coordinator = new ModeSessionCoordinator(shell.Cast, shell.Browser);
        SetMirroring(shell.Cast, true);

        coordinator.NoticeFor(TvSurfaceKind.Browser).ShouldBe("The TV is showing your screen.");
    }

    [Fact]
    public async Task SwitchingToTheBrowser_AsksNothing_AndPutsTheHeldTabsBack()
    {
        var tv = await TvAsync();
        await HoldTwoTabsUnderAMirrorAsync(tv);
        tv.Coordinator.CanSwitchTo(TvSurfaceKind.Browser).ShouldBeTrue();

        await tv.Coordinator.SwitchToAsync(TvSurfaceKind.Browser, Token);

        tv.Questions.ShouldBe(0, "the button that says switch is the answer");
        tv.Cast.IsMirroring.ShouldBeFalse();
        tv.Remote.Commands.ShouldContain(command =>
            command.Action == BrowserCommandAction.Open && command.Url == "https://example.test/one");
    }

    [Fact]
    public async Task SwitchingWhereItCannot_DoesNothing()
    {
        var tv = await TvAsync();
        await OpenPageAsync(tv.Browser);
        tv.Coordinator.CanSwitchTo(TvSurfaceKind.Mirror).ShouldBeFalse("no session is paired");
        tv.Coordinator.CanSwitchTo(TvSurfaceKind.Media).ShouldBeFalse("there is no file to play");
        tv.Coordinator.CanSwitchTo(TvSurfaceKind.Browser).ShouldBeFalse("the browser is already up");

        await tv.Coordinator.SwitchToAsync(TvSurfaceKind.Mirror, Token);

        tv.Browser.HasOpenBrowserSurface.ShouldBeTrue();
        tv.Cast.IsMirroring.ShouldBeFalse();
    }

    [Fact]
    public async Task CanSwitchToMirror_OnceASessionIsPaired()
    {
        await using var receiver = new LoopbackReceiver();
        var tv = await PairedTvAsync(receiver);
        await OpenPageAsync(tv.Browser);

        tv.Coordinator.CanSwitchTo(TvSurfaceKind.Mirror).ShouldBeTrue();
    }

    [Fact]
    public async Task SwitchingToMirror_ClosesTheBrowserAndStartsWithoutASecondQuestion()
    {
        await using var receiver = new LoopbackReceiver();
        var engine = new FailingMirrorEngine();
        var cast = await PairedStandaloneCastAsync(receiver, engine);
        var remote = new RecordingBrowserRemote();
        using var browser = new BrowserPageViewModel(
            cast,
            new RecordingBrowserSessionConnector(remote),
            new InMemoryBrowserTrustStore(),
            new BrowserFixtures.ImmediateDispatcher(),
            new InMemoryBrowserProfileLibraryStore(),
            new BrowserHelpViewModel(false));
        await browser.VerifySecureReceiverCommand.ExecuteAsync(null);
        browser.Address = "https://example.test/";
        await browser.NavigateCommand.ExecuteAsync(null);
        browser.HasOpenBrowserSurface.ShouldBeTrue();
        var prompt = new SurfaceSwitchPrompt();
        var coordinator = new ModeSessionCoordinator(cast, browser, prompt);

        await coordinator.SwitchToAsync(TvSurfaceKind.Mirror, Token);

        prompt.IsOpen.ShouldBeFalse("the notice's switch button is already the answer");
        browser.HasOpenBrowserSurface.ShouldBeFalse();
        remote.Commands.ShouldContain(command => command.Action == BrowserCommandAction.Close);
        engine.Starts.ShouldBe(1);
    }

    [Fact]
    public async Task CastCommands_WithoutAShellCoordinator_StillReachTheirOwnWork()
    {
        await using var receiver = new LoopbackReceiver();
        var engine = new FailingMirrorEngine();
        var cast = await PairedStandaloneCastAsync(receiver, engine);

        await cast.LoadMediaFileCommand.ExecuteAsync(@"C:\missing\video.mp4");
        cast.MediaStatus.ShouldBe("Media was not sent.");

        await cast.StartScreenSessionCommand.ExecuteAsync(null);
        cast.Failure.ShouldBe("test engine stopped before capture");
        cast.IsMirroring.ShouldBeFalse();
        engine.Starts.ShouldBe(1);
    }

    [Fact]
    public async Task AnArrivalOfferWithoutAWayToTellWhereThePersonIs_IsRefused()
    {
        var tv = await TvAsync();

        await Should.ThrowAsync<ArgumentNullException>(() => tv.Coordinator.OfferOnArrivalAsync(TvSurfaceKind.Browser, null!));
    }

    private static async Task<Tv> TvAsync(bool onLoopback = false)
    {
        var remote = new RecordingBrowserRemote();
        var browser = await BrowserFixtures.ReadyViewModelAsync(remote);
        var device = onLoopback
            ? BrowserFixtures.EligibleDevice() with { Address = IPAddress.Loopback }
            : BrowserFixtures.EligibleDevice();
        var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(device));
        await shell.Cast.ProbeCommand.ExecuteAsync(null);
        var prompt = new SurfaceSwitchPrompt();
        return new Tv(shell.Cast, browser, remote, new ModeSessionCoordinator(shell.Cast, browser, prompt), prompt);
    }

    private static async Task<Tv> PairedTvAsync(LoopbackReceiver receiver)
    {
        var tv = await TvAsync(onLoopback: true);
        tv.Cast.PairingCode = "123456";
        tv.Cast.ReceiverPort = receiver.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await tv.Cast.ConnectCommand.ExecuteAsync(null);
        tv.Cast.IsSessionConnected.ShouldBeTrue(tv.Cast.Failure ?? "the loopback receiver should have paired");
        tv.Cast.CanStartMirrorNow.ShouldBeTrue(tv.Cast.MirrorVerdict?.Reason ?? "no mirror verdict");
        return tv;
    }

    private static async Task OpenPageAsync(BrowserPageViewModel browser)
    {
        browser.Address = "https://example.test/";
        await browser.NavigateCommand.ExecuteAsync(null);
        browser.HasOpenBrowserSurface.ShouldBeTrue();
    }

    /// <summary>Two tabs open, then a mirror takes the TV: the tabs are held for later.</summary>
    private static async Task HoldTwoTabsUnderAMirrorAsync(Tv tv)
    {
        await OpenPageAsync(tv.Browser);
        tv.Remote.Cockpit!.PublishTabs(new BrowserTabsSnapshot(
            Epoch: tv.Browser.BrowserEpochForTests,
            Revision: 1,
            ActiveTabId: 2,
            Tabs:
            [
                new BrowserTabSnapshotItem(1, "One", "https://example.test/one", 100, false, false, false, false),
                new BrowserTabSnapshotItem(2, "Two", "https://example.test/two", 100, false, false, false, false),
            ]));
        await tv.Coordinator.PrepareForAsync(TvSurfaceKind.Mirror, Token);
        SetMirroring(tv.Cast, true);
        tv.Browser.HasOpenBrowserSurface.ShouldBeFalse();
    }

    private static void SetMirroring(CastPageViewModel cast, bool value) =>
        typeof(CastPageViewModel)
            .GetField("_isMirroring", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(cast, value);

    private static void SetMediaPlaying(CastPageViewModel cast, bool value) =>
        typeof(CastPageViewModel).GetProperty(nameof(CastPageViewModel.IsMediaPlaying))!.SetValue(cast, value);

    private static async Task<CastPageViewModel> PairedStandaloneCastAsync(
        LoopbackReceiver receiver,
        IMirrorEngine mirrorEngine)
    {
        var device = BrowserFixtures.EligibleDevice() with { Address = IPAddress.Loopback };
        var cast = new CastPageViewModel(
            BrowserFixtures.Prober(device),
            new EmptyRecentAddressStore(),
            mirrorEngine: mirrorEngine,
            receiverInstaller: new OfflineReceiverInstaller());
        await cast.ProbeCommand.ExecuteAsync(null);
        cast.PairingCode = "123456";
        cast.ReceiverPort = receiver.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await cast.ConnectCommand.ExecuteAsync(null);
        cast.IsSessionConnected.ShouldBeTrue(cast.Failure ?? "the loopback receiver should have paired");
        return cast;
    }

    private sealed class EmptyRecentAddressStore : IRecentAddressStore
    {
        public IReadOnlyList<RecentAddress> Load() => [];

        public void Remember(RecentAddress address)
        {
        }

        public void Clear()
        {
        }
    }

    private sealed class FailingMirrorEngine : IMirrorEngine
    {
        public int Starts { get; private set; }

        public IMirrorEngineSession Start(MirrorSessionOptions options) =>
            Throw();

        private IMirrorEngineSession Throw()
        {
            Starts++;
            throw new MirrorEngineException("test engine stopped before capture");
        }
    }

    /// <summary>A shell's Cast page, a ready browser, and the coordinator between them.</summary>
    private sealed class Tv
    {
        private readonly TaskCompletionSource asked = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Tv(
            CastPageViewModel cast,
            BrowserPageViewModel browser,
            RecordingBrowserRemote remote,
            ModeSessionCoordinator coordinator,
            SurfaceSwitchPrompt prompt)
        {
            Cast = cast;
            Browser = browser;
            Remote = remote;
            Coordinator = coordinator;
            Prompt = prompt;
            prompt.PropertyChanged += (_, change) =>
            {
                if (change.PropertyName == nameof(SurfaceSwitchPrompt.IsOpen) && prompt.IsOpen)
                {
                    Questions++;
                    asked.TrySetResult();
                }
            };
        }

        public CastPageViewModel Cast { get; }

        public BrowserPageViewModel Browser { get; }

        public RecordingBrowserRemote Remote { get; }

        public ModeSessionCoordinator Coordinator { get; }

        public SurfaceSwitchPrompt Prompt { get; }

        /// <summary>How many times the question has been asked.</summary>
        public int Questions { get; private set; }

        /// <summary>Completes once the question is showing, for offers that reconnect first.</summary>
        public Task AskedAsync() => Prompt.IsOpen
            ? Task.CompletedTask
            : asked.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
    }
}
