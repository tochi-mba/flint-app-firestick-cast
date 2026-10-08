using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Flint.Core;
using Flint.Core.Settings;
using Flint.Session;

namespace Flint.App.ViewModels;

/// <summary>
/// The Screen page: which display to share, the picture it sends, the live numbers, and starting.
/// </summary>
/// <remarks>
/// The share itself stays on the Cast page's view model, which owns the connection. This page
/// decides what to share and asks for changes; a change made while sharing is applied in place,
/// so the TV keeps its mirror surface.
/// </remarks>
public sealed partial class ScreenPageViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService settings;
    private readonly IDisplayCatalog catalog;
    private readonly TimeProvider time;
    private readonly SynchronizationContext? context = SynchronizationContext.Current;
    private DisplayInfo? selected;
    private bool usualMissing;

    /// <summary>Builds the page over the Cast page, the live settings and this PC's displays.</summary>
    public ScreenPageViewModel(
        CastPageViewModel cast,
        ISettingsService settings,
        IDisplayCatalog catalog,
        TimeProvider? time = null)
    {
        Cast = cast ?? throw new ArgumentNullException(nameof(cast));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.time = time ?? cast.Time;

        cast.PropertyChanged += OnCastChanged;
        cast.MirrorSwitched += OnSwitched;
        cast.MirrorStatsUpdated += OnStats;
        cast.MirrorPictureStarted += OnPictureStarted;
        cast.MirrorReceiverStats += OnReceiverStats;
        settings.Changed += OnSettingsChanged;
        RefreshDisplays();
    }

    /// <summary>The connection, the verdict and the share itself.</summary>
    public CastPageViewModel Cast { get; }

    /// <summary>The displays this PC can share, in capture's order.</summary>
    public IReadOnlyList<DisplayInfo> Displays { get; private set; } = [];

    /// <summary>Whether there is more than one display to choose between.</summary>
    public bool HasSeveralDisplays => Displays.Count > 1;

    /// <summary>The one line naming the only display, when there is just one.</summary>
    public string? OnlyDisplay => Displays.Count == 1 ? Displays[0].Describe() : null;

    /// <summary>The display a share captures.</summary>
    public DisplayInfo? SelectedDisplay
    {
        get => selected;
        set => Choose(value);
    }

    /// <summary>Said when the remembered display is not connected.</summary>
    public string? MissingDisplayNotice =>
        usualMissing ? "Your usual display is not connected. Using the main display." : null;

    /// <summary>Said when the chosen display is turned on its side.</summary>
    public string? RotatedDisplayNotice =>
        selected?.IsSideways == true ? "This display is rotated. It will appear sideways on the TV." : null;

    /// <summary>Whether a change to the running share is being made.</summary>
    [ObservableProperty]
    private bool _isSwitching;

    /// <summary>What happened to the last change to a running share, or null.</summary>
    [ObservableProperty]
    private string? _switchStatus;

    /// <summary>Reads the displays again, keeping the chosen one when it is still connected.</summary>
    /// <remarks>Called when the page opens and whenever Windows reports a display change.</remarks>
    public void RefreshDisplays()
    {
        Displays = catalog.List();
        var screen = settings.Current.Screen;
        var still = selected is null
            ? null
            : Displays.FirstOrDefault(display => string.Equals(display.Identity, selected.Identity, StringComparison.OrdinalIgnoreCase));
        var chosen = DisplayCatalog.Choose(Displays, screen.Display, screen.DisplayIdentity);
        selected = still ?? chosen.Display;
        usualMissing = still is null && chosen.RememberedIsMissing;
        OnPropertyChanged(nameof(Displays));
        OnPropertyChanged(nameof(HasSeveralDisplays));
        OnPropertyChanged(nameof(OnlyDisplay));
        RaiseSelection();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Cast.PropertyChanged -= OnCastChanged;
        Cast.MirrorSwitched -= OnSwitched;
        Cast.MirrorStatsUpdated -= OnStats;
        Cast.MirrorPictureStarted -= OnPictureStarted;
        Cast.MirrorReceiverStats -= OnReceiverStats;
        settings.Changed -= OnSettingsChanged;
        countdown?.Cancel();
        countdown?.Dispose();
        StopPauseTimer();
    }

    /// <summary>Chooses a display, remembers it, and moves a running share onto it.</summary>
    private void Choose(DisplayInfo? display)
    {
        if (display is null || Equals(display, selected))
        {
            return;
        }

        selected = display;
        usualMissing = false;
        settings.Update(current => current with
        {
            Screen = current.Screen with { Display = ShareDisplayChoice.Remembered, DisplayIdentity = display.Identity },
        });
        RaiseSelection();
        ChangeRunningShare();
    }

    /// <summary>Asks a running share to send what the page now says.</summary>
    private void ChangeRunningShare()
    {
        if (!Cast.ChangeMirror(ShareOptions()))
        {
            return;
        }

        // Made now, it would replace the picture the TV is holding with its "waiting" screen.
        if (Cast.IsMirrorPaused)
        {
            SwitchStatus = "This change applies when you resume.";
            return;
        }

        IsSwitching = true;
        SwitchStatus = "Switching…";
    }

    internal void OnSwitched(MirrorSwitch result)
    {
        IsSwitching = false;
        if (result.Succeeded)
        {
            SwitchStatus = null;
            return;
        }

        // The share carried on with what it had, so the page shows that display again.
        var kept = Displays.FirstOrDefault(display => display.Index == result.Options.OutputIndex);
        if (kept is not null && !Equals(kept, selected))
        {
            selected = kept;
            RaiseSelection();
        }

        SwitchStatus = $"Flint could not switch, so sharing carries on as before. {result.Failure}";
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs change)
    {
        var before = change.Previous.Screen;
        var now = change.Current.Screen;
        RaiseModes();
        OnPropertyChanged(nameof(ShowLiveNumbers));
        if (ScreenQualityPreset.Resolve(before) != ScreenQualityPreset.Resolve(now))
        {
            ChangeRunningShare();
        }
    }

    private void OnCastChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (change.PropertyName is nameof(CastPageViewModel.IsMirroring))
        {
            ResetLiveNumbers();
            if (!Cast.IsMirroring)
            {
                IsSwitching = false;
            }
        }

        if (change.PropertyName is nameof(CastPageViewModel.IsMirroring) or nameof(CastPageViewModel.MirrorPause))
        {
            OnPauseChanged();
        }

        if (change.PropertyName is nameof(CastPageViewModel.IsMirroring)
            or nameof(CastPageViewModel.CanStartMirrorNow)
            or nameof(CastPageViewModel.MirrorVerdict))
        {
            OnPropertyChanged(nameof(CanShare));
            ShareCommand.NotifyCanExecuteChanged();
        }
    }

    private void RaiseSelection()
    {
        OnPropertyChanged(nameof(SelectedDisplay));
        OnPropertyChanged(nameof(MissingDisplayNotice));
        OnPropertyChanged(nameof(RotatedDisplayNotice));
        OnPropertyChanged(nameof(ModeSentence));
    }
}
