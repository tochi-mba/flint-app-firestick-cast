using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flint.App.Services;

namespace Flint.App.ViewModels;

/// <summary>Settings reflected from the TV; values are never optimistically fabricated.</summary>
public sealed partial class BrowserViewControlsViewModel : ObservableObject
{
    [ObservableProperty]
    private bool isAvailable;

    [ObservableProperty]
    private int zoomPercent = 125;

    [ObservableProperty]
    private BrowserUserAgentMode userAgent = BrowserUserAgentMode.Tv;

    [ObservableProperty]
    private BrowserDarkMode darkMode = BrowserDarkMode.Unavailable;

    [ObservableProperty]
    private BrowserInputMode inputMode = BrowserInputMode.Cursor;

    [ObservableProperty]
    private bool isFullscreen;

    [ObservableProperty]
    private bool isEditing;

    [ObservableProperty]
    private bool isFindActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FindStatus))]
    private int findCurrent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FindStatus))]
    private int findTotal;

    public string FindStatus => FindTotal <= 0 ? "NO MATCHES" : $"{FindCurrent} OF {FindTotal}";

    internal void Apply(BrowserViewSnapshot snapshot)
    {
        IsAvailable = true;
        ZoomPercent = Math.Clamp(snapshot.ZoomPercent, 75, 200);
        UserAgent = snapshot.UserAgent;
        DarkMode = snapshot.DarkMode;
        InputMode = snapshot.InputMode;
        IsFullscreen = snapshot.IsFullscreen;
        IsEditing = snapshot.IsEditing;
        IsFindActive = snapshot.IsFindActive;
        FindCurrent = Math.Clamp(snapshot.FindCurrent, 0, Math.Max(0, snapshot.FindTotal));
        FindTotal = Math.Max(0, snapshot.FindTotal);
    }

    internal void Reset(bool available)
    {
        IsAvailable = available;
        ZoomPercent = 125;
        UserAgent = BrowserUserAgentMode.Tv;
        DarkMode = available ? BrowserDarkMode.Off : BrowserDarkMode.Unavailable;
        InputMode = BrowserInputMode.Cursor;
        IsFullscreen = false;
        IsEditing = false;
        IsFindActive = false;
        FindCurrent = 0;
        FindTotal = 0;
    }

    /// <summary>Binds the cockpit channel these controls write to, or clears it on disconnect.</summary>
    internal void Bind(IBrowserCockpitRemote? cockpit)
    {
        remote = cockpit;
        ZoomInCommand.NotifyCanExecuteChanged();
        ZoomOutCommand.NotifyCanExecuteChanged();
        ResetZoomCommand.NotifyCanExecuteChanged();
        SetUserAgentCommand.NotifyCanExecuteChanged();
        ToggleDarkModeCommand.NotifyCanExecuteChanged();
        ToggleInputModeCommand.NotifyCanExecuteChanged();
        ToggleFullscreenCommand.NotifyCanExecuteChanged();
        StartFindCommand.NotifyCanExecuteChanged();
        FindNextCommand.NotifyCanExecuteChanged();
        FindPreviousCommand.NotifyCanExecuteChanged();
        ClearFindCommand.NotifyCanExecuteChanged();
    }

    // Steps rather than a slider: at a glance a percentage is easier to read than a thumb position,
    // and the same steps exist on the television so both ends agree on what a zoom level is.
    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private Task ZoomInAsync(CancellationToken cancellationToken) =>
        SetZoom(NextZoom(1), cancellationToken);

    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private Task ZoomOutAsync(CancellationToken cancellationToken) =>
        SetZoom(NextZoom(-1), cancellationToken);

    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private Task ResetZoomAsync(CancellationToken cancellationToken) =>
        SetZoom(DefaultZoomPercent, cancellationToken);

    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private Task SetUserAgentAsync(BrowserUserAgentMode mode, CancellationToken cancellationToken) =>
        Send(new BrowserViewRequest(BrowserViewOperation.SetUserAgent, (int)mode), cancellationToken);

    [RelayCommand(CanExecute = nameof(CanToggleDarkMode))]
    private Task ToggleDarkModeAsync(CancellationToken cancellationToken) =>
        Send(
            new BrowserViewRequest(BrowserViewOperation.SetDarkMode, DarkMode == BrowserDarkMode.On ? 0 : 1),
            cancellationToken);

    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private Task ToggleInputModeAsync(CancellationToken cancellationToken) =>
        Send(
            new BrowserViewRequest(
                BrowserViewOperation.SetInputMode,
                InputMode == BrowserInputMode.Cursor ? (int)BrowserInputMode.Focus : (int)BrowserInputMode.Cursor),
            cancellationToken);

    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private Task ToggleFullscreenAsync(CancellationToken cancellationToken) =>
        Send(
            new BrowserViewRequest(BrowserViewOperation.SetFullscreen, IsFullscreen ? 0 : 1),
            cancellationToken);

    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private Task StartFindAsync(string? query, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(query)
            ? Task.CompletedTask
            : Send(new BrowserViewRequest(BrowserViewOperation.FindStart, 0, query), cancellationToken);

    [RelayCommand(CanExecute = nameof(CanStepFind))]
    private Task FindNextAsync(CancellationToken cancellationToken) =>
        Send(new BrowserViewRequest(BrowserViewOperation.FindNext), cancellationToken);

    [RelayCommand(CanExecute = nameof(CanStepFind))]
    private Task FindPreviousAsync(CancellationToken cancellationToken) =>
        Send(new BrowserViewRequest(BrowserViewOperation.FindPrevious), cancellationToken);

    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private Task ClearFindAsync(CancellationToken cancellationToken) =>
        Send(new BrowserViewRequest(BrowserViewOperation.FindClear), cancellationToken);

    /// <summary>Dark mode is only offered when the receiver reported a state other than unavailable.</summary>
    private bool CanToggleDarkMode => IsAvailable && DarkMode != BrowserDarkMode.Unavailable;

    private bool CanStepFind => IsAvailable && IsFindActive && FindTotal > 0;

    private int NextZoom(int direction)
    {
        var index = Array.IndexOf(ZoomSteps, ZoomPercent);
        if (index < 0)
        {
            // The receiver is the authority on zoom, so a value that is not one of ours is possible.
            // Snapping to the nearest step keeps the next press predictable instead of jumping.
            index = Array.FindIndex(ZoomSteps, step => step >= ZoomPercent);
            index = index < 0 ? ZoomSteps.Length - 1 : index;
        }

        return ZoomSteps[Math.Clamp(index + direction, 0, ZoomSteps.Length - 1)];
    }

    private Task SetZoom(int percent, CancellationToken cancellationToken) =>
        Send(new BrowserViewRequest(BrowserViewOperation.SetZoom, percent), cancellationToken);

    private Task Send(BrowserViewRequest request, CancellationToken cancellationToken) =>
        remote?.SendViewCommandAsync(request, cancellationToken) ?? Task.CompletedTask;

    private IBrowserCockpitRemote? remote;

    /// <summary>The zoom levels both ends agree on.</summary>
    private static readonly int[] ZoomSteps = [75, 100, 125, 150, 175, 200];

    /// <summary>Ten feet from a panel, larger than a desktop default is the right resting place.</summary>
    private const int DefaultZoomPercent = 125;
}
