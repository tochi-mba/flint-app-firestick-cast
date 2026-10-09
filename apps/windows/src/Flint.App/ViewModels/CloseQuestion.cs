using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Flint.App.ViewModels;

/// <summary>How the person answered a question about closing or quitting.</summary>
public enum CloseAnswer
{
    /// <summary>They put the question away without choosing; nothing happens.</summary>
    Cancelled = 0,

    /// <summary>They chose the first answer.</summary>
    First = 1,

    /// <summary>They chose the second answer.</summary>
    Second = 2,
}

/// <summary>The one question the shell asks about closing its window or quitting.</summary>
/// <remarks>
/// A second question withdraws the first as cancelled, so a stale answer can never quit Flint.
/// </remarks>
public sealed partial class CloseQuestion : ObservableObject
{
    private TaskCompletionSource<CloseAnswer>? pending;

    /// <summary>Whether the question is showing.</summary>
    [ObservableProperty]
    private bool _isOpen;

    /// <summary>The question itself.</summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>What each answer means.</summary>
    [ObservableProperty]
    private string _body = string.Empty;

    /// <summary>The label on the first, recommended answer.</summary>
    [ObservableProperty]
    private string _firstLabel = string.Empty;

    /// <summary>The label on the second answer.</summary>
    [ObservableProperty]
    private string _secondLabel = string.Empty;

    /// <summary>Asks the question and waits for the answer.</summary>
    public Task<CloseAnswer> AskAsync(string title, string body, string firstLabel, string secondLabel)
    {
        Answer(CloseAnswer.Cancelled);
        var question = new TaskCompletionSource<CloseAnswer>();
        pending = question;
        Title = title;
        Body = body;
        FirstLabel = firstLabel;
        SecondLabel = secondLabel;
        IsOpen = true;
        return question.Task;
    }

    [RelayCommand]
    private void ChooseFirst() => Answer(CloseAnswer.First);

    [RelayCommand]
    private void ChooseSecond() => Answer(CloseAnswer.Second);

    /// <summary>Puts the question away without choosing. Harmless when none is open.</summary>
    [RelayCommand]
    private void Cancel() => Answer(CloseAnswer.Cancelled);

    private void Answer(CloseAnswer answer)
    {
        var question = pending;
        pending = null;
        if (question is null)
        {
            return;
        }

        IsOpen = false;
        question.TrySetResult(answer);
    }
}
