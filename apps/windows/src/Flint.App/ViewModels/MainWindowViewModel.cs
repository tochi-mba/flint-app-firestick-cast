using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
/// The shell: the left rail, the brand lockup, and whichever page is selected.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    /// <summary>The narrowest the window may be at 100% interface size.</summary>
    public const double BaseMinimumWidth = 900;

    /// <summary>The shortest the window may be at 100% interface size.</summary>
    public const double BaseMinimumHeight = 620;

    private readonly SettingsService settingsService;
    private readonly KeepAwakeCoordinator keepAwake;

    /// <summary>The product name shown under the REX mark.</summary>
    public const string ProductName = "Flint";

    /// <summary>The maker, shown as the tracked eyebrow above the product name.</summary>
    public const string MakerName = "REX TECHNOLOGIES";

    [ObservableProperty]
    private NavigationDestination _selected;

    private MainWindowViewModel(
        CastPageViewModel cast,
        IOnboardingState onboardingState,
        IAppSettingsStore settingsStore,
        Func<KeepAwakeLevel, bool> keepAwakeApply,
        IFolderOpener folders,
        IMediaHistoryStore mediaHistory,
        IWhatsNewState whatsNewState,
        IKnownTvStore knownTvs,
        IDisplayCatalog displays,
        IUpdateSource? updateSource = null,
        IUpdatePreference? updatePreference = null)
    {
        Cast = cast;
        settingsService = new SettingsService(settingsStore);
        settingsService.Changed += (_, change) =>
        {
            if (change.Previous.General.InterfaceScalePercent != change.Current.General.InterfaceScalePercent)
            {
                OnPropertyChanged(nameof(InterfaceScale));
                OnPropertyChanged(nameof(MinimumWidth));
                OnPropertyChanged(nameof(MinimumHeight));
            }
        };
        Cast.UseReconnect(knownTvs, settingsService);
        Browser = new BrowserPageViewModel(cast);
        Media = new MediaPageViewModel(cast, settingsService, mediaHistory, new LocalMediaFileSystem());
        Screen = new ScreenPageViewModel(cast, settingsService, displays);
        Coordinator = new ModeSessionCoordinator(Cast, Browser, SwitchPrompt, settingsService);
        keepAwake = new KeepAwakeCoordinator(Cast, settingsService, keepAwakeApply);
        Onboarding = new OnboardingViewModel(onboardingState);
        WhatsNew = new WhatsNewViewModel(whatsNewState, Onboarding.HasCompleted);
        Onboarding.PropertyChanged += (_, change) => RaiseWhatsNewShown(change.PropertyName);
        WhatsNew.PropertyChanged += (_, change) => RaiseWhatsNewShown(change.PropertyName);

        // What the TV is showing changes under every page: a mirror ends, a video stops, the
        // browser closes. The notice follows it rather than a page switch.
        Cast.PropertyChanged += (_, _) => RaiseTvNotice();
        Browser.PropertyChanged += (_, _) => RaiseTvNotice();

        // The update panel is told what a live session is rather than working it out: mirroring and
        // a browser session both end when this process exits, and neither may be cut short by an
        // update the person did not ask for at that moment.
        Updates = new UpdatesViewModel(
            updateSource ?? new UninstalledUpdateSource(),
            updatePreference ?? new SessionUpdatePreference(),
            () => Cast.IsSessionConnected || Cast.IsMirroring || Browser.HasLiveSession);
        Settings = new SettingsPageViewModel(
        [
            new GeneralSettingsViewModel(settingsService),
            new MediaSettingsViewModel(settingsService),
            new ScreenSettingsViewModel(settingsService),
            new TvSettingsViewModel(Cast),
            new PrivacySettingsViewModel(settingsService, folders, FlintDataFolder.Path, FlintDataFolder.LogsPath, mediaHistory),
            new UpdatesSectionViewModel(Updates),
            new AboutSettingsViewModel(Cast, VersionLabel, EngineVersion.Read(), Environment.OSVersion.VersionString, WhatsNew),
        ]);
        Cast.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName is nameof(CastPageViewModel.IsSessionConnected)
                or nameof(CastPageViewModel.IsMirroring))
            {
                Updates.SessionStateChanged();
            }

            // The window gets out of the way so the TV shows the person's work rather than Flint.
            if (changed.PropertyName is nameof(CastPageViewModel.IsMirroring)
                && Cast.IsMirroring
                && settingsService.Current.Screen.MinimiseWhenSharing)
            {
                MinimiseRequested?.Invoke(this, EventArgs.Empty);
            }
        };

        // Finishing the introduction runs the first probe, so the walkthrough ends on the answer it
        // spent five steps preparing the user for rather than on an empty screen.
        Onboarding.Completed += (_, _) => Cast.ProbeCommand.Execute(null);

        // Someone who has just been introduced to Flint has nothing to catch up on: everything in
        // "What's new" was there when they started.
        Onboarding.Completed += (_, _) => WhatsNew.MarkAllSeen();
        if (!Onboarding.IsVisible)
        {
            _ = StartAsync();
        }
        Destinations =
        [
            new NavigationDestination("C", "Cast"),
                new NavigationDestination("M", "Media"),
                new NavigationDestination("S", "Screen"),
                new NavigationDestination("W", "Web"),
            new NavigationDestination("D", "Diagnostics"),
                new NavigationDestination("R", "Settings"),
        ];
        _selected = Destinations[0];
    }

    /// <summary>
    /// Reaches the last TV again without its code when the setting is on; otherwise, or when that
    /// does not work, probes the address typed last, as Flint always did.
    /// </summary>
    internal async Task StartAsync()
    {
        if (!await Cast.ReconnectOnStartAsync().ConfigureAwait(true) && !string.IsNullOrWhiteSpace(Cast.ManualAddress)
            && !Cast.HasDevice)
        {
            await Cast.ProbeCommand.ExecuteAsync(null).ConfigureAwait(true);
        }
    }

    /// <summary>The rail's destinations.</summary>
    public ObservableCollection<NavigationDestination> Destinations { get; }

    /// <summary>The Cast page and local receiver connection screen.</summary>
    public CastPageViewModel Cast { get; }

    /// <summary>The Media page: choosing a file, and what is playing.</summary>
    public MediaPageViewModel Media { get; }

    /// <summary>The Screen page: which display, which picture, and the live numbers.</summary>
    public ScreenPageViewModel Screen { get; }

    /// <summary>Raised when a share starts and the settings say Flint should minimise itself.</summary>
    public event EventHandler? MinimiseRequested;

    /// <summary>The independent browser eligibility page.</summary>
    public BrowserPageViewModel Browser { get; }

    /// <summary>The question asked before the TV is switched from one thing to another.</summary>
    public SurfaceSwitchPrompt SwitchPrompt { get; } = new();

    /// <summary>Keeps one thing on the TV at a time, and asks before replacing it.</summary>
    public ModeSessionCoordinator Coordinator { get; }

    /// <summary>What the TV is showing, when that is not what the selected page puts there.</summary>
    public string? TvNotice => Coordinator.NoticeFor(SelectedSurface);

    /// <summary>Whether <see cref="TvNotice"/> has anything to say.</summary>
    public bool ShowTvNotice => TvNotice is not null;

    /// <summary>Whether the selected page can take the TV over right now.</summary>
    public bool CanSwitchHere => Coordinator.CanSwitchTo(SelectedSurface);

    /// <summary>The label on the button that takes the TV over for the selected page.</summary>
    public string SwitchHereLabel => SelectedSurface is TvSurfaceKind.Mirror ? "MIRROR INSTEAD" : "SWITCH TO BROWSER";

    /// <summary>The surface the selected page puts on the TV, if any.</summary>
    private TvSurfaceKind SelectedSurface => Selected.Label switch
    {
        "Web" => TvSurfaceKind.Browser,
        "Screen" => TvSurfaceKind.Mirror,
        "Media" => TvSurfaceKind.Media,
        _ => TvSurfaceKind.None,
    };

    /// <summary>The first-run introduction, shown over the shell until it is completed.</summary>
    public OnboardingViewModel Onboarding { get; }

    /// <summary>What has changed since this person last used Flint, shown once after an update.</summary>
    public WhatsNewViewModel WhatsNew { get; }

    /// <summary>Whether "What's new" covers the window: only when it is open and the introduction is not.</summary>
    public bool ShowsWhatsNew => WhatsNew.IsVisible && !Onboarding.IsVisible;

    /// <summary>Finding and installing a newer Flint, shown on the Settings page.</summary>
    public UpdatesViewModel Updates { get; }

    /// <summary>The Settings page: what Flint remembers, and how it updates itself.</summary>
    public SettingsPageViewModel Settings { get; }

    /// <summary>The live settings.</summary>
    public ISettingsService SettingsService => settingsService;

    /// <summary>How large the window draws its contents, where 1 is Flint's own size.</summary>
    public double InterfaceScale => settingsService.Current.General.InterfaceScalePercent / 100.0;

    /// <summary>The narrowest the window may be at the current interface size.</summary>
    public double MinimumWidth => Math.Round(BaseMinimumWidth * InterfaceScale);

    /// <summary>The shortest the window may be at the current interface size.</summary>
    public double MinimumHeight => Math.Round(BaseMinimumHeight * InterfaceScale);

    /// <summary>The level this PC is currently kept awake at.</summary>
    public KeepAwakeLevel KeepAwakeLevel => keepAwake.Level;

    private void RaiseWhatsNewShown(string? changed)
    {
        if (changed == nameof(WalkthroughViewModel.IsVisible))
        {
            OnPropertyChanged(nameof(ShowsWhatsNew));
        }
    }

    /// <summary>Writes any settings change still waiting and lets the PC sleep normally.</summary>
    /// <remarks>Called as Flint exits, so a change made in the last moment is not lost.</remarks>
    public void Dispose()
    {
        Media.Dispose();
        keepAwake.Dispose();
        settingsService.Dispose();
    }

    /// <summary>The version shown in the rail foot.</summary>
    public static string VersionLabel =>
        typeof(MainWindowViewModel).Assembly.GetName().Version is { } version
            ? $"v{version.Major}.{version.Minor}.{version.Build}"
            : "v0.1.0";

    /// <summary>Whether the Cast page is showing.</summary>
    public bool IsCastSelected => Selected.Label == "Cast";

    /// <summary>Whether the diagnostics page is showing.</summary>
    public bool IsDiagnosticsSelected => Selected.Label == "Diagnostics";

    /// <summary>Whether the media page is showing.</summary>
    public bool IsMediaSelected => Selected.Label == "Media";

    /// <summary>Whether the screen page is showing.</summary>
    public bool IsScreenSelected => Selected.Label == "Screen";

    /// <summary>Whether the TV-resident browser capability page is showing.</summary>
    public bool IsWebSelected => Selected.Label == "Web";

    /// <summary>Whether the settings page is showing.</summary>
    public bool IsSettingsSelected => Selected.Label == "Settings";

    /// <summary>Whether a destination without a view is showing.</summary>
    public bool IsPlaceholderSelected => !Selected.IsImplemented;

    /// <summary>The selected destination's name.</summary>
    public string SelectedLabel => Selected.Label;

    /// <summary>
    /// Honest rail copy for the selected product boundary. Web pages are fetched by the TV, while
    /// Flint remains out of the route and provides no cloud relay.
    /// </summary>
    public string PrivacyLabel => IsWebSelected
        ? "TV CONNECTS DIRECTLY - NO FLINT CLOUD"
        : "LOCAL ONLY - NO CLOUD";

    /// <summary>Builds the shell with the real Windows probes wired in.</summary>
    public static MainWindowViewModel CreateDefault()
    {
        var sleepBlocker = new SleepBlocker();
        var shell = new MainWindowViewModel(
            new CastPageViewModel(
                new CapabilityProber(
                    new EngineHostProbe(new WindowsHostProbe()),
                    new FireTvDeviceProbe(),
                    new TcpNetworkProbe()),
                new FileRecentAddressStore()),
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
        IDisplayCatalog? displays = null) =>
        new(
            new CastPageViewModel(
                prober,
                addressStore ?? new EmptyRecentAddressStore(),
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

    /// <summary>
    /// Takes the TV over for the selected page because the person pressed the button that says so.
    /// </summary>
    [RelayCommand]
    private Task SwitchHereAsync() => Coordinator.SwitchToAsync(SelectedSurface);

    private void RaiseTvNotice()
    {
        OnPropertyChanged(nameof(TvNotice));
        OnPropertyChanged(nameof(ShowTvNotice));
        OnPropertyChanged(nameof(CanSwitchHere));
        OnPropertyChanged(nameof(SwitchHereLabel));
    }

    partial void OnSelectedChanged(NavigationDestination value)
    {
        Flint.Core.FlintDiag.Info("FlintUi", $"rail select={value.Label}");

        // A question about the page being left is withdrawn, as keeping what the TV shows.
        SwitchPrompt.Dismiss();
        if (value.Label == "Web")
        {
            _ = Coordinator.OfferOnArrivalAsync(TvSurfaceKind.Browser, () => IsWebSelected);
        }
        else if (value.Label == "Screen")
        {
            _ = Coordinator.OfferOnArrivalAsync(TvSurfaceKind.Mirror, () => IsScreenSelected);
        }

        RaiseTvNotice();
        OnPropertyChanged(nameof(IsCastSelected));
        OnPropertyChanged(nameof(IsDiagnosticsSelected));
        OnPropertyChanged(nameof(IsMediaSelected));
        OnPropertyChanged(nameof(IsScreenSelected));
        OnPropertyChanged(nameof(IsWebSelected));
        OnPropertyChanged(nameof(IsSettingsSelected));
        OnPropertyChanged(nameof(IsPlaceholderSelected));
        OnPropertyChanged(nameof(SelectedLabel));
        OnPropertyChanged(nameof(PrivacyLabel));
    }
}
