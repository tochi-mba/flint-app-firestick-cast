using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flint.App.Services;
using Flint.Core;
using Flint.Discovery;
using Flint.Engine.Interop;
using Flint.Platform.Windows;

namespace Flint.App.ViewModels;

/// <summary>
/// The shell: the left rail, the brand lockup, and whichever page is selected.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    /// <summary>The product name shown under the REX mark.</summary>
    public const string ProductName = "Flint";

    /// <summary>The maker, shown as the tracked eyebrow above the product name.</summary>
    public const string MakerName = "REX TECHNOLOGIES";

    [ObservableProperty]
    private NavigationDestination _selected;

    private MainWindowViewModel(
        CastPageViewModel cast,
        IOnboardingState onboardingState,
        IUpdateSource? updateSource = null,
        IUpdatePreference? updatePreference = null)
    {
        Cast = cast;
        Browser = new BrowserPageViewModel(cast);
        Coordinator = new ModeSessionCoordinator(Cast, Browser, SwitchPrompt);
        Onboarding = new OnboardingViewModel(onboardingState);

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
        Settings = new SettingsPageViewModel(Cast, Updates);
        Cast.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName is nameof(CastPageViewModel.IsSessionConnected)
                or nameof(CastPageViewModel.IsMirroring))
            {
                Updates.SessionStateChanged();
            }
        };

        // Finishing the introduction runs the first probe, so the walkthrough ends on the answer it
        // spent five steps preparing the user for rather than on an empty screen.
        Onboarding.Completed += (_, _) => Cast.ProbeCommand.Execute(null);
        if (!Onboarding.IsVisible && !string.IsNullOrWhiteSpace(Cast.ManualAddress))
        {
            _ = Cast.ProbeCommand.ExecuteAsync(null);
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

    /// <summary>The rail's destinations.</summary>
    public ObservableCollection<NavigationDestination> Destinations { get; }

    /// <summary>The Cast page, which connects to the local receiver.</summary>
    /// <summary>The Cast page and local receiver connection screen.</summary>
    public CastPageViewModel Cast { get; }

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

    /// <summary>Finding and installing a newer Flint, shown on the Settings page.</summary>
    public UpdatesViewModel Updates { get; }

    /// <summary>The Settings page: what Flint remembers, and how it updates itself.</summary>
    public SettingsPageViewModel Settings { get; }

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
        var shell = new MainWindowViewModel(
            new CastPageViewModel(
                new CapabilityProber(
                    new EngineHostProbe(new WindowsHostProbe()),
                    new FireTvDeviceProbe(),
                    new TcpNetworkProbe()),
                new FileRecentAddressStore()),
            new FileOnboardingState(),
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
    /// <remarks>
    /// The receiver installer is the offline one: a shell built around a supplied prober has no
    /// television to talk to, and the real installer would try the fake device's address on every
    /// probe.
    /// </remarks>
    public static MainWindowViewModel CreateWith(
        CapabilityProber prober,
        IOnboardingState? onboardingState = null,
        IRecentAddressStore? addressStore = null) =>
        new(
            new CastPageViewModel(
                prober,
                addressStore ?? new EmptyRecentAddressStore(),
                receiverInstaller: new OfflineReceiverInstaller()),
            onboardingState ?? new CompletedOnboardingState());

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
