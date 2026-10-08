using CommunityToolkit.Mvvm.ComponentModel;
using Flint.Core;

namespace Flint.App.ViewModels;

/// <summary>Pausing a screen share for privacy, and resuming it.</summary>
public sealed partial class CastPageViewModel
{
    /// <summary>Whether the share is sending, and what the TV shows while it is not.</summary>
    [ObservableProperty]
    private MirrorPause _mirrorPause;

    /// <summary>Whether a share is running but paused.</summary>
    public bool IsMirrorPaused => MirrorPause is not MirrorPause.Running;

    /// <summary>Pauses the running share from its next frame.</summary>
    /// <param name="how">What the TV shows while paused.</param>
    /// <returns>False when nothing is being shared, or <paramref name="how"/> is not a pause.</returns>
    internal bool PauseMirror(MirrorPause how)
    {
        if (mirrorControl is not { } control || how is MirrorPause.Running)
        {
            return false;
        }

        control.SetPause(how);
        MirrorPause = how;
        FlintDiag.Info("FlintCast", $"mirror paused tv={how}");
        return true;
    }

    /// <summary>Resumes a paused share; the TV shows the screen at once.</summary>
    /// <returns>False when nothing is paused.</returns>
    internal bool ResumeMirror()
    {
        if (mirrorControl is not { } control || !IsMirrorPaused)
        {
            return false;
        }

        control.SetPause(MirrorPause.Running);
        MirrorPause = MirrorPause.Running;
        FlintDiag.Info("FlintCast", "mirror resumed");
        return true;
    }

    partial void OnMirrorPauseChanged(MirrorPause value) => OnPropertyChanged(nameof(IsMirrorPaused));
}
