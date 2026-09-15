using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Flint.App.Services;
using Flint.Core;
using Flint.Discovery;
using Flint.Engine.Interop;

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

    private MainWindowViewModel(CastPageViewModel cast, IOnboardingState onboardingState)
    {
        Cast = cast;
        Browser = new BrowserPageViewModel(cast);
        _ = new ModeSessionCoordinator(Cast, Browser);
        Onboarding = new OnboardingViewModel(onboardingState);

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

    /// <summary>The first-run introduction, shown over the shell until it is completed.</summary>
    public OnboardingViewModel Onboarding { get; }

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
    public static MainWindowViewModel CreateDefault() =>
        new(
            new CastPageViewModel(
                new CapabilityProber(
                    new EngineHostProbe(new WindowsHostProbe()),
                    new FireTvDeviceProbe(),
                    new TcpNetworkProbe()),
                new FileRecentAddressStore()),
            new FileOnboardingState());

    /// <summary>Builds the shell around a supplied prober, for tests and design-time data.</summary>
    /// <param name="prober">The capability probe to drive the Cast page with.</param>
    /// <param name="onboardingState">
    /// Onboarding persistence. Defaults to a store that reports the introduction as already seen,
    /// so a test asking about the shell is not handed the introduction it did not ask for.
    /// </param>
    /// <param name="addressStore">Recent addresses. Defaults to a store that remembers none.</param>
    public static MainWindowViewModel CreateWith(
        CapabilityProber prober,
        IOnboardingState? onboardingState = null,
        IRecentAddressStore? addressStore = null) =>
        new(
            new CastPageViewModel(prober, addressStore ?? new EmptyRecentAddressStore()),
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

    partial void OnSelectedChanged(NavigationDestination value)
    {
        Flint.Core.FlintDiag.Info("FlintUi", $"rail select={value.Label}");
        if (value.Label == "Web") _ = Browser.ActivateAsync();
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
