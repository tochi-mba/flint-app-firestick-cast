using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Flint.App.ViewModels;

/// <summary>
/// A full-window walkthrough of steps: the first-run introduction, and "What's new" after an update.
/// </summary>
/// <remarks>
/// Both are shown by the same view, so they look and behave alike: the same step layout, Back, Next
/// and Skip, the same progress count. Each says only what finishing means for it.
/// </remarks>
public abstract partial class WalkthroughViewModel : ObservableObject
{
    /// <summary>The step being shown, counted from zero.</summary>
    [ObservableProperty]
    private int _stepIndex;

    /// <summary>Whether the walkthrough covers the window.</summary>
    [ObservableProperty]
    private bool _isVisible;

    /// <summary>Starts the walkthrough on its first step.</summary>
    /// <param name="steps">The steps, in order. There must be at least one.</param>
    protected WalkthroughViewModel(IReadOnlyList<OnboardingStep> steps) => SetSteps(steps);

    /// <summary>Every step, in order.</summary>
    public IReadOnlyList<OnboardingStep> Steps { get; private set; } = [];

    /// <summary>The step being shown.</summary>
    public OnboardingStep Current => Steps[StepIndex];

    /// <summary>Whether there is a previous step to go back to.</summary>
    public bool CanGoBack => StepIndex > 0;

    /// <summary>Whether this is the last step.</summary>
    public bool IsLastStep => StepIndex == Steps.Count - 1;

    /// <summary>The label on the forward button, which becomes the finish action at the end.</summary>
    public string AdvanceLabel => IsLastStep ? FinishLabel : "NEXT";

    /// <summary>Human-readable position, for the step counter.</summary>
    public string Progress => $"{StepIndex + 1} / {Steps.Count}";

    /// <summary>What the forward button says on the last step.</summary>
    protected abstract string FinishLabel { get; }

    /// <summary>Jumps to a specific step, for the progress dots.</summary>
    public void GoToStep(int index)
    {
        if (index >= 0 && index < Steps.Count)
        {
            StepIndex = index;
        }
    }

    /// <summary>Records that the walkthrough is done and takes it off the window.</summary>
    protected abstract void Finish();

    /// <summary>Replaces the steps and goes back to the first.</summary>
    protected void SetSteps(IReadOnlyList<OnboardingStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count == 0)
        {
            throw new ArgumentException("A walkthrough needs at least one step.", nameof(steps));
        }

        Steps = steps;
        StepIndex = 0;

        // Raised whether or not the index moved: the step at index 0 may be a different one now.
        OnPropertyChanged(nameof(Steps));
        OnStepIndexChanged(0);
    }

    /// <summary>Moves to the next step, or finishes on the last one.</summary>
    [RelayCommand]
    private void Advance()
    {
        if (IsLastStep)
        {
            Finish();
            return;
        }

        StepIndex++;
    }

    /// <summary>Moves back one step. Does nothing on the first.</summary>
    [RelayCommand]
    private void GoBack()
    {
        if (CanGoBack)
        {
            StepIndex--;
        }
    }

    /// <summary>Dismisses the walkthrough without going through it; this still counts as finishing.</summary>
    [RelayCommand]
    private void Skip() => Finish();

    partial void OnStepIndexChanged(int value)
    {
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(AdvanceLabel));
        OnPropertyChanged(nameof(Progress));
    }
}
