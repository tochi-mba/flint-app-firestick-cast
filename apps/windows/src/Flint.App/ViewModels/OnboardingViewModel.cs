using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flint.App.Controls;
using Flint.Core;

namespace Flint.App.ViewModels;

/// <summary>
/// The first-run introduction.
/// </summary>
/// <remarks>
/// <para>
/// Its job is to tell a first-time user the one thing that decides whether Flint can work for them
/// at all — whether their device runs Fire OS or Vega — before they spend time wondering why
/// nothing is found. Everything else here is secondary to that.
/// </para>
/// <para>
/// Navigation is pure and the store is injected, so the whole flow is testable without a window
/// or a disk.
/// </para>
/// </remarks>
public sealed partial class OnboardingViewModel : ObservableObject
{
    private readonly IOnboardingState _state;

    [ObservableProperty]
    private int _stepIndex;

    [ObservableProperty]
    private bool _isVisible;

    /// <summary>Creates the introduction over a persistence store.</summary>
    /// <param name="state">Where completion is remembered.</param>
    public OnboardingViewModel(IOnboardingState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        Steps = BuildSteps();
        _isVisible = !state.HasCompleted;
    }

    /// <summary>Raised when the user finishes, so the shell can run the first probe.</summary>
    public event EventHandler? Completed;

    /// <summary>Every step, in order.</summary>
    public IReadOnlyList<OnboardingStep> Steps { get; }

    /// <summary>The step being shown.</summary>
    public OnboardingStep Current => Steps[StepIndex];

    /// <summary>Whether there is a previous step to go back to.</summary>
    public bool CanGoBack => StepIndex > 0;

    /// <summary>Whether this is the last step.</summary>
    public bool IsLastStep => StepIndex == Steps.Count - 1;

    /// <summary>The label on the forward button, which becomes a finish action at the end.</summary>
    public string AdvanceLabel => IsLastStep ? "PROBE MY NETWORK" : "NEXT";

    /// <summary>Human-readable position, for the step counter.</summary>
    public string Progress => $"{StepIndex + 1} / {Steps.Count}";

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

    /// <summary>
    /// Dismisses the introduction without walking through it.
    /// </summary>
    /// <remarks>
    /// Skipping still counts as completion. Someone who has decided they do not want the
    /// walkthrough should not be shown it again on every launch.
    /// </remarks>
    [RelayCommand]
    private void Skip() => Finish();

    /// <summary>Jumps to a specific step, for the progress dots.</summary>
    public void GoToStep(int index)
    {
        if (index >= 0 && index < Steps.Count)
        {
            StepIndex = index;
        }
    }

    /// <summary>Shows the introduction again from the beginning.</summary>
    [RelayCommand]
    public void Restart()
    {
        _state.Reset();
        StepIndex = 0;
        IsVisible = true;
    }

    private void Finish()
    {
        _state.MarkCompleted();
        IsVisible = false;
        Completed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnStepIndexChanged(int value)
    {
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(AdvanceLabel));
        OnPropertyChanged(nameof(Progress));
    }

    /// <summary>
    /// The walkthrough content.
    /// </summary>
    /// <remarks>
    /// Ordered by what a first-time user most needs to know. The device-family step comes second,
    /// before any instruction, because it is the only step that can end the conversation — and
    /// finding that out after following three pages of setup would be a worse experience than
    /// being told plainly at the start.
    /// </remarks>
    private static List<OnboardingStep> BuildSteps() =>
    [
        new OnboardingStep(
            "Welcome",
            "Flint casts this PC to your TV",
            "Flint finds your Fire TV, puts the Flint app on it, and shows your screen, videos "
                + "or web pages there. Everything stays on your own network: no account, no cloud, "
                + "no analytics.",
            [
                "Control and casting traffic stay on your network.",
                "Flint never changes your TV without showing you what and why.",
            ]),

        new OnboardingStep(
            "Read this first",
            "Your Fire TV model decides what is possible",
            "Flint on the TV is an Android app. It runs on Fire OS and cannot run on Vega OS, so "
                + "this is the one thing worth checking before anything else.",
            [
                "Check on the TV: Settings, then My Fire TV, then About.",
                "A Fire OS version is a starting point, not a guarantee that every feature is supported.",
                "If it says Vega OS, this receiver is not compatible. If the version is missing, check the device details instead of guessing.",
            ],
            Tone.Live),

        new OnboardingStep(
            "On the TV, in this order",
            "Unlock Developer Options, then turn on ADB",
            "This is how Flint puts its app on the TV and opens it for you, once. The TV hides "
                + "Developer Options until you unlock it, so the menu you need does not exist until "
                + "step 4.",
            [
                "Open Settings, then My Fire TV, then About.",
                "Highlight your device's own name in that list — the Fire TV Stick entry itself, "
                    + "not Network or Storage.",
                "Press Select on it about seven times. The TV counts down, then says you are now a "
                    + "developer.",
                "Press Back once to return to My Fire TV. Developer Options has now appeared here.",
                "Open Developer Options and switch on ADB debugging.",
                "When Flint connects, accept the prompt on the TV. Flint identifies itself as "
                    + "flint-rex-technologies.",
            ],
            Ordered: true),
    ];
}
