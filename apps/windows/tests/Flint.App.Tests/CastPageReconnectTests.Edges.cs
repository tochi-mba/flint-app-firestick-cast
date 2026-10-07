using System.Net;
using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.Core;
using Shouldly;
using static Flint.App.Tests.CastPageFixtures;

namespace Flint.App.Tests;

/// <summary>Reconnecting at its edges: a TV found where it was, an attempt cut short, and failures that must not escape.</summary>
public sealed partial class CastPageReconnectTests
{
    [Fact]
    public async Task AsFlintStarts_ATvStillWhereItWas_IsReachedThere_WithoutSearching()
    {
        await using var tv = new LoopbackReceiver { Name = "Living Room", Login = Login };
        store.Save(new KnownTv("Living Room", "127.0.0.1", tv.Port, Login, clock.GetUtcNow()));
        var cast = AddressablePage();

        (await cast.ReconnectOnStartAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();

        cast.IsSessionConnected.ShouldBeTrue();
        tv.LoginsAccepted.ShouldBe(1);
        cast.ManualAddress.ShouldBe("127.0.0.1");
    }

    [Fact]
    public async Task AStartThatIsCancelled_StopsQuietly()
    {
        await using var tv = new LoopbackReceiver { Name = "Living Room", Login = Login };
        store.Save(new KnownTv("Living Room", "127.0.0.1", tv.Port, Login, clock.GetUtcNow()));
        var cast = AddressablePage();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        (await cast.ReconnectOnStartAsync(cancelled.Token)).ShouldBeFalse();

        cast.IsSessionConnected.ShouldBeFalse();
        cast.HasReconnectBanner.ShouldBeFalse();
        cast.IsReconnecting.ShouldBeFalse();
    }

    [Fact]
    public async Task ANewAttempt_TakesOverFromOneStillWaiting_AndKeepsItsOwnWords()
    {
        await using var tv = new LoopbackReceiver { Name = "Living Room", Login = Login };
        var cast = await PairedPageAsync(tv);
        await tv.CloseAsync();
        await Until(() => cast.IsReconnecting);
        var waiting = cast.ReconnectTask;

        (await cast.ReconnectOnStartAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();
        await waiting.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        cast.IsSessionConnected.ShouldBeTrue();
        tv.LoginsAccepted.ShouldBe(1, "the waiting attempt was cancelled, not run as well");
        cast.HasReconnectBanner.ShouldBeFalse();
    }

    [Fact]
    public async Task AFailureNoOneExpected_StopsTheAttempt_AndSaysSo()
    {
        await using var tv = new LoopbackReceiver { Name = "Living Room", Login = Login };
        var throwing = new ThrowingStore(store);
        var cast = new CastPageViewModel(
            BrowserFixtures.Prober(BrowserFixtures.EligibleDevice() with { Address = IPAddress.Loopback }),
            new NoRecentAddresses(),
            receiverLauncher: new RecordingLauncher(),
            receiverInstaller: new OfflineReceiverInstaller(),
            time: clock);
        cast.UseReconnect(throwing, settings);
        await Pair(cast, tv);
        throwing.Throws = true;

        await tv.CloseAsync();
        await Until(() => cast.IsReconnecting);
        clock.Advance(TimeSpan.FromSeconds(1));
        await cast.ReconnectTask.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        cast.ReconnectBanner.ShouldBe("Reconnecting to Living Room stopped. Connect on the Cast page to try again.");
        cast.IsReconnecting.ShouldBeFalse();
    }

    [Fact]
    public async Task KeepTrying_WithNoTvFound_OrOneWithoutALogin_DoesNothing()
    {
        var cast = Page();
        await cast.KeepTryingAsync(TestContext.Current.CancellationToken);
        cast.IsReconnecting.ShouldBeFalse();

        await using var tv = new LoopbackReceiver { Name = "Living Room" };
        var paired = await PairedPageAsync(tv);
        paired.KnownTvs.ShouldHaveSingleItem().HasLogin.ShouldBeFalse("this TV grants no login");
        await tv.CloseAsync();
        await Until(() => !paired.IsSessionConnected);
        await paired.ReconnectTask;
        paired.IsReconnecting.ShouldBeFalse();
    }

    [Fact]
    public async Task ATvThatGivesNoName_IsNotRemembered()
    {
        await using var tv = new LoopbackReceiver { Name = "   ", Login = Login };

        var cast = await PairedPageAsync(tv);

        cast.IsSessionConnected.ShouldBeTrue();
        cast.KnownTvs.ShouldBeEmpty();
    }

    [Fact]
    public async Task PairingAgain_WhileConnected_ReplacesTheSession()
    {
        await using var tv = new LoopbackReceiver { Name = "Living Room", Login = Login };
        var cast = await PairedPageAsync(tv);

        // Several times, because the replaced session's end arrives on its own schedule: when it
        // was taken for the TV leaving, it cleared the new connection and reconnected over it.
        for (var again = 0; again < 3; again++)
        {
            await Pair(cast, tv);
            await cast.ReconnectTask;

            cast.IsSessionConnected.ShouldBeTrue();
            cast.IsReconnecting.ShouldBeFalse();
            cast.PairingStatus.ShouldBe("Paired and ready to cast.");
        }

        tv.LoginsAccepted.ShouldBe(0, "nothing reconnected over the new session");
        cast.KnownTvs.ShouldHaveSingleItem().Name.ShouldBe("Living Room");
    }

    [Fact]
    public async Task PairingAgain_WithACodeAndNoNewLogin_KeepsTheLoginAlreadyHeld()
    {
        store.Save(new KnownTv("Living Room", "127.0.0.1", 47855, Login, clock.GetUtcNow().AddDays(-1)));
        await using var tv = new LoopbackReceiver { Name = "Living Room", BrowserPort = 41234 };

        var cast = await PairedPageAsync(tv);

        var known = cast.KnownTvs.ShouldHaveSingleItem();
        known.Token.ShouldBe(Login, "a TV that grants nothing new leaves the login Flint holds");
        known.ReceiverPort.ShouldBe(tv.Port);
    }

    [Fact]
    public void Wiring_NeedsAStoreAndSettings()
    {
        var cast = Page();
        Should.Throw<ArgumentNullException>(() => cast.UseReconnect(null!, settings));
        Should.Throw<ArgumentNullException>(() => cast.UseReconnect(store, null!));
    }

    private CastPageViewModel AddressablePage()
    {
        var cast = new CastPageViewModel(
            new CapabilityProber(new StillHost(), new AddressableTv(), new StillNetwork()),
            new NoRecentAddresses(),
            receiverLauncher: new RecordingLauncher(),
            receiverInstaller: new OfflineReceiverInstaller(),
            time: clock);
        cast.UseReconnect(store, settings);
        return cast;
    }

    /// <summary>Answers an address probe with the loopback TV, as a TV still where it was would.</summary>
    private sealed class AddressableTv : IAddressableDeviceProbe
    {
        private static FireTvDevice Device => BrowserFixtures.EligibleDevice() with { Address = IPAddress.Loopback };

        public Task<IReadOnlyList<FireTvDevice>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FireTvDevice>>([Device]);

        public Task<FireTvDevice> ProbeAddressAsync(IPAddress address, int? port = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Device);
    }

    private sealed class StillHost : IHostProbe
    {
        public Task<HostCapabilities> ProbeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new HostCapabilities([], [], 0, 26100));
    }

    private sealed class StillNetwork : INetworkProbe
    {
        public Task<NetworkPath?> MeasureAsync(FireTvDevice device, CancellationToken cancellationToken = default) =>
            Task.FromResult<NetworkPath?>(new NetworkPath(4.0, 1.0, 120.0, 0.0));
    }

    /// <summary>A store that starts throwing when told to, as nothing in Flint expects.</summary>
    private sealed class ThrowingStore(IKnownTvStore inner) : IKnownTvStore
    {
        public bool Throws { get; set; }

        public IReadOnlyList<KnownTv> Load() => inner.Load();

        public void Save(KnownTv tv)
        {
            if (Throws)
            {
                throw new InvalidOperationException("A store that cannot save.");
            }

            inner.Save(tv);
        }

        public void Forget(string name) => inner.Forget(name);

        public void ForgetAll() => inner.ForgetAll();
    }
}
