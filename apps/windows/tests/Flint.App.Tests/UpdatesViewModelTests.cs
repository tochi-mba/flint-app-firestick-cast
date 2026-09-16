using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.Platform.Windows;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>What the update panel offers, and what it refuses to do.</summary>
public sealed class UpdatesViewModelTests
{
    [Fact]
    public async Task PortableBuildIsToldItDoesNotUpdateItself()
    {
        var source = new FakeUpdateSource { IsInstalled = false, NewVersion = "0.2.0" };
        var updates = new UpdatesViewModel(source, new FakePreference());

        await updates.CheckAsync();

        updates.Status.ShouldContain("portable");
        updates.CanCheck.ShouldBeFalse();
        source.Checks.ShouldBe(0);
    }

    [Fact]
    public async Task AnUpToDateInstallSaysSoAndOffersNoRestart()
    {
        var source = new FakeUpdateSource { IsInstalled = true, NewVersion = null };
        var updates = new UpdatesViewModel(source, new FakePreference());

        await updates.CheckAsync();

        updates.Status.ShouldBe("Flint is up to date.");
        updates.ReadyVersion.ShouldBeNull();
        updates.CanRestart.ShouldBeFalse();
    }

    [Fact]
    public async Task ANewVersionIsDownloadedAndOfferedAsARestart()
    {
        var source = new FakeUpdateSource { IsInstalled = true, NewVersion = "0.2.0" };
        var updates = new UpdatesViewModel(source, new FakePreference());

        await updates.CheckAsync();

        updates.ReadyVersion.ShouldBe("0.2.0");
        updates.DownloadedPercent.ShouldBe(100);
        updates.Status.ShouldBe("Version 0.2.0 is ready. Restart Flint to use it.");
        updates.CanRestart.ShouldBeTrue();
    }

    [Fact]
    public async Task ARunningSessionIsNeverInterruptedByAnUpdate()
    {
        var live = true;
        var source = new FakeUpdateSource { IsInstalled = true, NewVersion = "0.2.0" };
        var updates = new UpdatesViewModel(source, new FakePreference(), () => live);

        await updates.CheckAsync();

        updates.CanRestart.ShouldBeFalse();
        updates.Status.ShouldContain("after this session");
        updates.Restart();
        source.Applied.ShouldBeFalse();

        // The session ends, and the same downloaded version becomes installable.
        live = false;
        updates.SessionStateChanged();

        updates.CanRestart.ShouldBeTrue();
        updates.Restart();
        source.Applied.ShouldBeTrue();
    }

    [Fact]
    public async Task AFailedCheckNamesTheReason()
    {
        var source = new FakeUpdateSource { IsInstalled = true, Failure = new HttpRequestException("no route to host") };
        var updates = new UpdatesViewModel(source, new FakePreference());

        await updates.CheckAsync();

        updates.Status.ShouldContain("no route to host");
        updates.CanCheck.ShouldBeTrue();
    }

    [Fact]
    public async Task NoCheckRunsAtLaunchWhenAutomaticChecksAreTurnedOff()
    {
        var preference = new FakePreference { ChecksAutomatically = false };
        var source = new FakeUpdateSource { IsInstalled = true, NewVersion = "0.2.0" };
        var updates = new UpdatesViewModel(source, preference);

        await updates.CheckAtLaunchAsync();

        source.Checks.ShouldBe(0);
        updates.ReadyVersion.ShouldBeNull();
    }

    [Fact]
    public void TurningAutomaticChecksOffIsRemembered()
    {
        var preference = new FakePreference();
        var updates = new UpdatesViewModel(new FakeUpdateSource { IsInstalled = true }, preference);

        updates.ChecksAutomatically = false;

        preference.ChecksAutomatically.ShouldBeFalse();
    }

    private sealed class FakeUpdateSource : IUpdateSource
    {
        public bool IsInstalled { get; init; }

        public string? NewVersion { get; init; }

        public Exception? Failure { get; init; }

        public int Checks { get; private set; }

        public bool Applied { get; private set; }

        public Task<string?> CheckForNewVersionAsync(CancellationToken cancellationToken)
        {
            Checks++;
            return Failure is not null
                ? Task.FromException<string?>(Failure)
                : Task.FromResult(NewVersion);
        }

        public Task DownloadAsync(IProgress<int>? progress, CancellationToken cancellationToken)
        {
            progress?.Report(50);
            return Task.CompletedTask;
        }

        public void ApplyAndRestart() => Applied = true;
    }

    private sealed class FakePreference : IUpdatePreference
    {
        public bool ChecksAutomatically { get; set; } = true;
    }
}
