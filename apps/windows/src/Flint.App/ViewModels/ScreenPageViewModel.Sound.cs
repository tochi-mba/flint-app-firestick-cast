using CommunityToolkit.Mvvm.Input;
using Flint.Core;
using Flint.Core.Settings;

namespace Flint.App.ViewModels;

/// <summary>Where shared sound plays, as the Screen page's segmented row names it.</summary>
/// <param name="Destination">The destination.</param>
/// <param name="Label">Its name on the page.</param>
public sealed record SoundDestinationChoice(SoundDestination Destination, string Label)
{
    /// <inheritdoc />
    public override string ToString() => Label;
}

/// <summary>This PC's sound, shared with the picture.</summary>
public sealed partial class ScreenPageViewModel
{
    private int? tvVolume;

    /// <summary>Where sound can play, in the order the page offers them.</summary>
    public IReadOnlyList<SoundDestinationChoice> SoundDestinations { get; } =
    [
        new(SoundDestination.TvOnly, "The TV only"),
        new(SoundDestination.TvAndPc, "The TV and this PC"),
    ];

    /// <summary>Whether this PC's sound is shared with the picture.</summary>
    public bool ShareSound
    {
        get => Screen.ShareSound;
        set => UpdateScreen(screen => screen with { ShareSound = value });
    }

    /// <summary>Where shared sound plays.</summary>
    public SoundDestinationChoice ChosenDestination
    {
        get => SoundDestinations.First(choice => choice.Destination == Screen.SoundDestination);
        set
        {
            if (value is not null)
            {
                UpdateScreen(screen => screen with { SoundDestination = value.Destination });
            }
        }
    }

    /// <summary>The Sound row's status line.</summary>
    public string SoundStatus =>
        SoundText.Status(Screen.ShareSound, Cast.IsMirroring, Cast.IsMirrorPaused, Cast.SoundState, Cast.SoundProblem);

    /// <summary>The level meter's position, from 0 to 1.</summary>
    public double SoundMeter => SoundText.Meter(Cast.SoundLevel);

    /// <summary>Whether the level meter shows: only while sound is reaching the TV.</summary>
    public bool ShowSoundMeter => Cast.IsMirroring && !Cast.IsMirrorPaused && Cast.SoundState is AudioShareState.Sounding;

    /// <summary>Whether to offer the default output: when the chosen one cannot be shared.</summary>
    public bool OfferDefaultOutput =>
        Cast.SoundState is AudioShareState.Unavailable && Screen.SoundSource is SoundSource.NamedDevice;

    /// <summary>Something Flint did with this PC's sound that the person should know, or null.</summary>
    public string? SoundNotice => Cast.SoundNotice;

    /// <summary>Said under the Sound row about protected video.</summary>
    public static string SoundHelp => SoundText.ProtectedContent;

    /// <summary>Whether the TV's volume can be set from here: only with the TV connected.</summary>
    public bool CanSetTvVolume => Cast.IsSessionConnected;

    /// <summary>The TV volume slider's value, in percent.</summary>
    public double TvVolume
    {
        get => tvVolume ?? 100;
        set => SetTvVolume((int)Math.Round(value));
    }

    /// <summary>The line beside the TV volume slider.</summary>
    public string TvVolumeText => tvVolume is { } level ? $"TV volume: {level}%" : "TV volume: not set from here yet";

    /// <summary>Shares the default output instead of the chosen one, which cannot be shared.</summary>
    [RelayCommand]
    private void UseDefaultOutput() => UpdateScreen(screen => screen with { SoundSource = SoundSource.DefaultOutput });

    private void SetTvVolume(int percent)
    {
        if (CanSetTvVolume)
        {
            tvVolume = Math.Clamp(percent, 0, 100);
            _ = Cast.SetTvVolumeAsync(tvVolume.Value / 100f);
        }

        OnPropertyChanged(nameof(TvVolume));
        OnPropertyChanged(nameof(TvVolumeText));
    }

    private void RaiseSound()
    {
        OnPropertyChanged(nameof(ShareSound));
        OnPropertyChanged(nameof(ChosenDestination));
        OnPropertyChanged(nameof(SoundStatus));
        OnPropertyChanged(nameof(SoundMeter));
        OnPropertyChanged(nameof(ShowSoundMeter));
        OnPropertyChanged(nameof(OfferDefaultOutput));
        OnPropertyChanged(nameof(SoundNotice));
        OnPropertyChanged(nameof(CanSetTvVolume));
    }
}
