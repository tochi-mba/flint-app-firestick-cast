using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flint.App.Services;
using Flint.Core;

namespace Flint.App.ViewModels;

/// <summary>This PC's sound across launches.</summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>Said once at launch when Flint put right something an earlier Flint left behind.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLaunchNotice))]
    private string? _launchNotice;

    /// <summary>Whether <see cref="LaunchNotice"/> has anything to say.</summary>
    public bool ShowLaunchNotice => LaunchNotice is not null;

    /// <summary>Says how a mute left behind by an earlier Flint was dealt with, when there is anything to say.</summary>
    internal void TellLeftOverMute(LeftOverMute result)
    {
        if (result is LeftOverMute.None)
        {
            return;
        }

        FlintDiag.Info("FlintCast", $"left-over mute {result}");
        LaunchNotice = result switch
        {
            LeftOverMute.Restored => "Flint closed last time while this PC was muted for the TV. Its sound is back as it was.",
            LeftOverMute.NotRestored => "Flint closed last time while this PC was muted for the TV, and Windows would not unmute it. Unmute it from the taskbar.",
            _ => null,
        };
    }

    /// <summary>Puts the launch notice away.</summary>
    [RelayCommand]
    private void DismissLaunchNotice() => LaunchNotice = null;
}
