using CommunityToolkit.Mvvm.Input;

namespace Flint.App.ViewModels;

/// <summary>
/// The TV's volume, as far as this PC has set it.
/// </summary>
public sealed partial class NowPlayingViewModel
{
    /// <summary>Whether a volume has been set from this PC, so the slider has a position to show.</summary>
    public bool HasVolume => volumePercent is not null;

    /// <summary>Whether the TV was muted from here.</summary>
    public bool IsMuted => volumeBeforeMute is not null;

    /// <summary>The volume slider's value, in percent.</summary>
    public double VolumePercent
    {
        get => volumePercent ?? 0;
        set => SetVolume((int)Math.Round(value));
    }

    /// <summary>The line under the volume slider.</summary>
    public string VolumeText => volumePercent is { } level
        ? $"TV volume: {level}%"
        : "TV volume: not set from here yet";

    /// <summary>The mute button's words.</summary>
    public string MuteLabel => IsMuted ? "UNMUTE" : "MUTE";

    /// <summary>The mute button's name for a screen reader.</summary>
    public string MuteName => IsMuted ? "Unmute the TV" : "Mute the TV";

    private bool CanChangeVolume => CanControl && !isSending;

    private bool CanStepVolume => CanChangeVolume && HasVolume;

    /// <summary>Mutes the TV, remembering the level to go back to; or goes back to it.</summary>
    [RelayCommand(CanExecute = nameof(CanStepVolume))]
    private void Mute()
    {
        if (volumeBeforeMute is { } restore)
        {
            SetVolume(restore);
            return;
        }

        volumeBeforeMute = volumePercent;
        volumePercent = 0;
        Send(() => remote.SetVolumeAsync(0f));
        RaiseVolume();
    }

    /// <summary>Turns the TV up by the volume step.</summary>
    [RelayCommand(CanExecute = nameof(CanStepVolume))]
    private void VolumeUp() => SetVolume(StepFrom + Media.VolumeStepPercent);

    /// <summary>Turns the TV down by the volume step.</summary>
    [RelayCommand(CanExecute = nameof(CanStepVolume))]
    private void VolumeDown() => SetVolume(StepFrom - Media.VolumeStepPercent);

    /// <summary>The level a volume step starts from: the one before muting, if muted.</summary>
    /// <remarks>Only read once a volume has been set, so there is always a level.</remarks>
    private int StepFrom => volumeBeforeMute ?? volumePercent.GetValueOrDefault();

    /// <summary>Sets the TV's volume, in percent.</summary>
    internal void SetVolume(int percent)
    {
        if (!CanChangeVolume)
        {
            RaiseVolume();
            return;
        }

        var level = Math.Clamp(percent, 0, 100);
        volumePercent = level;
        volumeBeforeMute = null;
        Send(() => remote.SetVolumeAsync(level / 100f));
        RaiseVolume();
    }

    private void RaiseVolume()
    {
        OnPropertyChanged(nameof(HasVolume));
        OnPropertyChanged(nameof(IsMuted));
        OnPropertyChanged(nameof(VolumePercent));
        OnPropertyChanged(nameof(VolumeText));
        OnPropertyChanged(nameof(MuteLabel));
        OnPropertyChanged(nameof(MuteName));
        MuteCommand.NotifyCanExecuteChanged();
        VolumeUpCommand.NotifyCanExecuteChanged();
        VolumeDownCommand.NotifyCanExecuteChanged();
    }
}
