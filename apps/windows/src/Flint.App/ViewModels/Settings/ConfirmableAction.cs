using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Flint.App.ViewModels.Settings;

/// <summary>
/// An action that cannot be undone, asked about in place before it runs.
/// </summary>
/// <remarks>
/// The button turns into a short sentence saying exactly what will change, with the confirming
/// button and a way back beside it. In place rather than in a dialog, so the question appears where
/// the person was already looking and nothing else on the page is covered.
/// </remarks>
public sealed partial class ConfirmableAction : ObservableObject
{
    private readonly Action action;

    /// <summary>Creates the action.</summary>
    /// <param name="label">The button that starts it.</param>
    /// <param name="question">What will change, said before it does.</param>
    /// <param name="confirmLabel">The button that does it.</param>
    /// <param name="action">The change itself.</param>
    public ConfirmableAction(string label, string question, string confirmLabel, Action action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmLabel);
        Label = label;
        Question = question;
        ConfirmLabel = confirmLabel;
        this.action = action ?? throw new ArgumentNullException(nameof(action));
    }

    /// <summary>The button that starts it.</summary>
    public string Label { get; }

    /// <summary>What will change, said before it does.</summary>
    public string Question { get; }

    /// <summary>The button that does it.</summary>
    public string ConfirmLabel { get; }

    /// <summary>Whether the question is showing.</summary>
    [ObservableProperty]
    private bool _isAsking;

    /// <summary>Shows the question.</summary>
    [RelayCommand]
    private void Ask() => IsAsking = true;

    /// <summary>Does it, and puts the button back.</summary>
    [RelayCommand]
    private void Confirm()
    {
        if (!IsAsking)
        {
            return;
        }

        IsAsking = false;
        action();
    }

    /// <summary>Puts the button back without doing anything.</summary>
    [RelayCommand]
    private void Cancel() => IsAsking = false;
}
