using Flint.App.Services;
using Flint.App.ViewModels.Settings;
using Flint.Core;
using Flint.Core.Media;
using Flint.Core.Settings;
using Flint.Discovery;
using Flint.Engine.Interop;
using Flint.Platform.Windows;

namespace Flint.App.ViewModels;

/// <summary>
/// Building the shell: over this PC's own stores and engines, or over stand-ins for tests and the
/// designer.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>Builds the shell with the real Windows probes wired in.</summary>
    public static MainWindowViewModel CreateDefault()
    {
        var sleepBlocker = new SleepBlocker();
        var sound = new NativeAudioEngine();
        var soundMemory = new FileSoundMemory();

        // Before anything else touches sound: a mute an earlier Flint left behind is put back first.
        var leftOver = TvOnlyMute.RestoreLeftOver(sound, soundMemory);
        var shell = new MainWindowViewModel(
            new CastPageViewModel(
                new CapabilityProber(
                    new EngineHostProbe(new WindowsHostProbe()),
                    new FireTvDeviceProbe(),
                    new TcpNetworkProbe()),
                new FileRecentAddressStore(),
                audioEngine: sound,
                soundMemory: soundMemory),
            new FileOnboardingState(),
            new FileAppSettingsStore(),
            sleepBlocker.Apply,
            new ExplorerFolderOpener(),
            new FileMediaHistoryStore(),
            new FileWhatsNewState(),
            new FileKnownTvStore(),
            new DisplayCatalog(new NativeDisplayOutputs(), new DisplayNames()),
            new VelopackUpdateSource(),
            new FileUpdatePreference());

        shell.TellLeftOverMute(leftOver);
        shell.Settings.Sections.OfType<GeneralSettingsViewModel>().Single().UseSignIn(RunAtSignIn.ForThisCopy());

        // In the background and without a prompt. A launch must not wait on GitHub, and the answer
        // belongs on the Settings page rather than in front of somebody who opened Flint to cast.
        _ = shell.Updates.CheckAtLaunchAsync();
        return shell;
    }

    /// <summary>Builds the shell around a supplied prober, for tests and design-time data.</summary>
    /// <param name="prober">The capability probe to drive the Cast page with.</param>
    /// <param name="onboardingState">
    /// Onboarding persistence. Defaults to a store that reports the introduction as already seen,
    /// so a test asking about the shell is not handed the introduction it did not ask for.
    /// </param>
    /// <param name="addressStore">Recent addresses. Defaults to a store that remembers none.</param>
    /// <param name="settingsStore">Settings. Defaults to a store that keeps them only in memory.</param>
    /// <param name="keepAwake">
    /// Where keep-awake requests go. Defaults to one that grants them without asking Windows, so a
    /// test never holds the machine running it awake.
    /// </param>
    /// <param name="folders">Opens folders. Defaults to one that opens nothing.</param>
    /// <param name="mediaHistory">Where played files stopped. Defaults to one kept only in memory.</param>
    /// <param name="whatsNewState">
    /// Which "What's new" highlights were seen. Defaults to all of them, so a test asking about the
    /// shell is not handed a walkthrough it did not ask for.
    /// </param>
    /// <param name="knownTvs">TVs Flint can reach again. Defaults to none, kept only in memory.</param>
    /// <param name="displays">This PC's displays. Defaults to one main 1920 by 1080 display.</param>
    /// <param name="mirrorEngine">Captures and encodes a share. Defaults to the real engine.</param>
    /// <remarks>
    /// The receiver installer is the offline one: a shell built around a supplied prober has no
    /// television to talk to, and the real installer would try the fake device's address on every
    /// probe.
    /// </remarks>
    public static MainWindowViewModel CreateWith(
        CapabilityProber prober,
        IOnboardingState? onboardingState = null,
        IRecentAddressStore? addressStore = null,
        IAppSettingsStore? settingsStore = null,
        Func<KeepAwakeLevel, bool>? keepAwake = null,
        IFolderOpener? folders = null,
        IMediaHistoryStore? mediaHistory = null,
        IWhatsNewState? whatsNewState = null,
        IKnownTvStore? knownTvs = null,
        IDisplayCatalog? displays = null,
        IMirrorEngine? mirrorEngine = null) =>
        new(
            new CastPageViewModel(
                prober,
                addressStore ?? new EmptyRecentAddressStore(),
                mirrorEngine: mirrorEngine,
                receiverInstaller: new OfflineReceiverInstaller()),
            onboardingState ?? new CompletedOnboardingState(),
            settingsStore ?? new InMemoryAppSettingsStore(),
            keepAwake ?? (_ => true),
            folders ?? new NoFolderOpener(),
            mediaHistory ?? new InMemoryMediaHistoryStore(),
            whatsNewState ?? new InMemoryWhatsNewState(WhatsNewViewModel.Catalogue.Select(highlight => highlight.Id)),
            knownTvs ?? new InMemoryKnownTvStore(),
            displays ?? new OneDisplay());

    /// <summary>One main display, for tests and design-time shells, which must not read the real ones.</summary>
    private sealed class OneDisplay : IDisplayCatalog
    {
        public IReadOnlyList<DisplayInfo> List() =>
            [new DisplayInfo(0, 1, "Display 1", "display-1", 0, 0, 1920, 1080, DisplayRotation.Upright, IsMain: true)];
    }

    /// <summary>A folder opener for tests and design-time shells, which must not open windows.</summary>
    private sealed class NoFolderOpener : IFolderOpener
    {
        public bool Open(string path) => true;
    }

    /// <summary>An onboarding store that always reports completion and remembers nothing.</summary>
    private sealed class CompletedOnboardingState : IOnboardingState
    {
        public bool HasCompleted => true;

        public void MarkCompleted()
        {
        }

        public void Reset()
        {
        }
    }

    /// <summary>
    /// The update source a shell built for a test or a designer gets: one that offers nothing.
    /// </summary>
    /// <remarks>
    /// A test must not reach GitHub, and a design-time shell has no installation to update. Both
    /// read as the portable build, which is the honest answer for a copy that cannot update itself.
    /// </remarks>
    private sealed class UninstalledUpdateSource : IUpdateSource
    {
        public bool IsInstalled => false;

        public Task<string?> CheckForNewVersionAsync(CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task DownloadAsync(IProgress<int>? progress, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void ApplyAndRestart()
        {
        }
    }

    /// <summary>A preference that lasts as long as the process, for tests and design-time data.</summary>
    private sealed class SessionUpdatePreference : IUpdatePreference
    {
        public bool ChecksAutomatically { get; set; } = true;
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
}
