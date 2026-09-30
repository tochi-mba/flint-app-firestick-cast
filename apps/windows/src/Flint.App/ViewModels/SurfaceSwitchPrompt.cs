using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Flint.App.ViewModels;

/// <summary>
/// The one question the shell asks before the TV is switched from one thing to another.
/// </summary>
/// <remarks>
/// Nothing on the TV changes while it is open: the answer decides. Only one question is ever
/// outstanding. A new one withdraws the old as "keep", which is also what leaving the page that
/// asked does, so a question can never be answered about something the person has moved on from.
/// </remarks>
public sealed partial class SurfaceSwitchPrompt : ObservableObject
{
    private TaskCompletionSource<bool>? pending;

    /// <summary>Whether the question is showing.</summary>
    [ObservableProperty]
    private bool _isOpen;

    /// <summary>The question itself.</summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>What the TV shows now and what switching stops.</summary>
    [ObservableProperty]
    private string _body = string.Empty;

    /// <summary>The label on the button that switches.</summary>
    [ObservableProperty]
    private string _confirmLabel = string.Empty;

    /// <summary>The label on the button that keeps what the TV shows.</summary>
    [ObservableProperty]
    private string _keepLabel = string.Empty;

    /// <summary>Asks the question; true when the person chooses to switch.</summary>
    public Task<bool> AskAsync(SurfaceSwitchCopy copy)
    {
        ArgumentNullException.ThrowIfNull(copy);
        Answer(false);

        pending = new TaskCompletionSource<bool>();
        Title = copy.Title;
        Body = copy.Body;
        ConfirmLabel = copy.ConfirmLabel;
        KeepLabel = copy.KeepLabel;
        IsOpen = true;
        Flint.Core.FlintDiag.Info("FlintSession", $"switch asked: {copy.Title}");
        return pending.Task;
    }

    /// <summary>Withdraws the question, as keeping what the TV shows. Harmless when none is open.</summary>
    public void Dismiss() => Answer(false);

    [RelayCommand]
    private void Confirm() => Answer(true);

    [RelayCommand]
    private void Keep() => Answer(false);

    private void Answer(bool switching)
    {
        var question = pending;
        pending = null;
        if (question is null)
        {
            return;
        }

        IsOpen = false;
        Flint.Core.FlintDiag.Info("FlintSession", switching ? "switch confirmed" : "switch declined");
        question.TrySetResult(switching);
    }
}
