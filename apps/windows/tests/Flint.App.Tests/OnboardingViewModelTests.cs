using Flint.App.Controls;
using Flint.App.ViewModels;
using Flint.Core;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// The introduction is the only place a first-time user is told that a Vega device can never work.
/// These tests guard the navigation, the persistence, and the content that carries that message.
/// </summary>
public sealed class OnboardingViewModelTests
{
    [Fact]
    public void NewUser_SeesTheIntroduction()
    {
        // Act
        var onboarding = new OnboardingViewModel(new FakeOnboardingState(completed: false));

        // Assert
        onboarding.IsVisible.ShouldBeTrue();
        onboarding.StepIndex.ShouldBe(0);
    }

    [Fact]
    public void ReturningUser_DoesNotSeeTheIntroduction()
    {
        // Act
        var onboarding = new OnboardingViewModel(new FakeOnboardingState(completed: true));

        // Assert
        onboarding.IsVisible.ShouldBeFalse();
    }

    [Fact]
    public void Advance_MovesForwardOneStepAtATime()
    {
        // Arrange
        var onboarding = NewFlow();

        // Act
        onboarding.AdvanceCommand.Execute(null);

        // Assert
        onboarding.StepIndex.ShouldBe(1);
    }

    [Fact]
    public void GoBack_OnTheFirstStep_DoesNothing()
    {
        // Arrange
        var onboarding = NewFlow();

        // Act
        onboarding.GoBackCommand.Execute(null);

        // Assert
        onboarding.StepIndex.ShouldBe(0);
        onboarding.CanGoBack.ShouldBeFalse();
    }

    [Fact]
    public void GoBack_AfterAdvancing_ReturnsToThePreviousStep()
    {
        // Arrange
        var onboarding = NewFlow();
        onboarding.AdvanceCommand.Execute(null);
        onboarding.AdvanceCommand.Execute(null);

        // Act
        onboarding.GoBackCommand.Execute(null);

        // Assert
        onboarding.StepIndex.ShouldBe(1);
        onboarding.CanGoBack.ShouldBeTrue();
    }

    [Fact]
    public void Advance_OnTheLastStep_CompletesRatherThanOverrunning()
    {
        // Arrange
        var state = new FakeOnboardingState(completed: false);
        var onboarding = new OnboardingViewModel(state);
        AdvanceToLastStep(onboarding);

        // Act
        onboarding.AdvanceCommand.Execute(null);

        // Assert
        onboarding.IsVisible.ShouldBeFalse();
        state.Completed.ShouldBeTrue();
        onboarding.StepIndex.ShouldBe(onboarding.Steps.Count - 1);
    }

    [Fact]
    public void Advance_OnTheLastStep_RaisesCompletedSoTheShellCanProbe()
    {
        // The walkthrough ends on the answer it spent five steps preparing the user for.

        // Arrange
        var onboarding = NewFlow();
        var raised = 0;
        onboarding.Completed += (_, _) => raised++;
        AdvanceToLastStep(onboarding);

        // Act
        onboarding.AdvanceCommand.Execute(null);

        // Assert
        raised.ShouldBe(1);
    }

    [Fact]
    public void Skip_CountsAsCompletionSoItIsNotShownAgain()
    {
        // Someone who has decided they do not want the walkthrough should not meet it every launch.

        // Arrange
        var state = new FakeOnboardingState(completed: false);
        var onboarding = new OnboardingViewModel(state);

        // Act
        onboarding.SkipCommand.Execute(null);

        // Assert
        onboarding.IsVisible.ShouldBeFalse();
        state.Completed.ShouldBeTrue();
    }

    [Fact]
    public void Skip_AlsoRaisesCompleted()
    {
        // Arrange
        var onboarding = NewFlow();
        var raised = 0;
        onboarding.Completed += (_, _) => raised++;

        // Act
        onboarding.SkipCommand.Execute(null);

        // Assert
        raised.ShouldBe(1);
    }

    [Fact]
    public void Restart_ShowsTheIntroductionAgainFromTheBeginning()
    {
        // Arrange
        var state = new FakeOnboardingState(completed: true);
        var onboarding = new OnboardingViewModel(state);
        onboarding.IsVisible.ShouldBeFalse();

        // Act
        onboarding.Restart();

        // Assert
        onboarding.IsVisible.ShouldBeTrue();
        onboarding.StepIndex.ShouldBe(0);
        state.Completed.ShouldBeFalse();
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(2, 2)]
    [InlineData(99, 0)]
    public void GoToStep_IgnoresAnIndexOutsideTheFlow(int requested, int expected)
    {
        // Arrange
        var onboarding = NewFlow();

        // Act
        onboarding.GoToStep(requested);

        // Assert
        onboarding.StepIndex.ShouldBe(expected);
    }

    [Fact]
    public void AdvanceLabel_BecomesTheFinishActionOnTheLastStep()
    {
        // Arrange
        var onboarding = NewFlow();

        // Act & Assert
        onboarding.AdvanceLabel.ShouldBe("NEXT");
        AdvanceToLastStep(onboarding);
        onboarding.AdvanceLabel.ShouldBe("PROBE MY NETWORK");
    }

    [Fact]
    public void Progress_CountsFromOne()
    {
        // "0 / 5" would read as though nothing had started.

        // Arrange
        var onboarding = NewFlow();

        // Act & Assert
        onboarding.Progress.ShouldBe($"1 / {onboarding.Steps.Count}");
        onboarding.AdvanceCommand.Execute(null);
        onboarding.Progress.ShouldBe($"2 / {onboarding.Steps.Count}");
    }

    [Fact]
    public void EveryStep_HasAnEyebrowTitleAndBody()
    {
        // A blank step would render as an empty screen with a Next button.

        // Arrange
        var onboarding = NewFlow();

        // Act & Assert
        onboarding.Steps.ShouldNotBeEmpty();
        foreach (var step in onboarding.Steps)
        {
            step.Eyebrow.ShouldNotBeNullOrWhiteSpace();
            step.Title.ShouldNotBeNullOrWhiteSpace();
            step.Body.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void TheVegaLimitation_IsExplainedBeforeAnySetupInstruction()
    {
        // Finding out a device can never work, after following three pages of setup, would be a
        // worse experience than being told plainly at the start.

        // Arrange
        var onboarding = NewFlow();

        // Act
        var vegaStep = onboarding.Steps
            .Select((step, index) => (step, index))
            .First(entry => entry.step.Body.Contains("Vega", StringComparison.Ordinal));
        var adbStep = onboarding.Steps
            .Select((step, index) => (step, index))
            .First(entry => entry.step.Title.Contains("ADB", StringComparison.Ordinal));

        // Assert
        vegaStep.index.ShouldBeLessThan(adbStep.index);
    }

    [Fact]
    public void TheVegaStep_IsTonedAsALimitationNotAsGuidance()
    {
        // It must not read like the rest of the walkthrough; it is the one step that can end the
        // conversation.

        // Arrange
        var onboarding = NewFlow();

        // Act
        var vegaStep = onboarding.Steps.First(step => step.Body.Contains("Vega", StringComparison.Ordinal));

        // Assert
        vegaStep.Tone.ShouldBe(Tone.Live);
    }

    [Fact]
    public void TheIntroduction_PromisesToSayWhatFlintCannotDo()
    {
        // The wording changes as capabilities land; the commitment does not. What must survive is
        // that the introduction tells people Flint will not offer a control that quietly fails.

        // Arrange
        var onboarding = NewFlow();

        // Act
        var text = string.Join(
            " ",
            onboarding.Steps.Select(step => step.Body).Concat(onboarding.Steps.SelectMany(step => step.Points)));

        // Assert
        text.ShouldContain("cannot");
    }

    [Fact]
    public void TheAdbStep_IsNumberedBecauseItsOrderMatters()
    {
        // Rendered as bullets, people open Developer Options first — where it does not yet exist.

        // Arrange
        var onboarding = NewFlow();

        // Act
        var adbStep = onboarding.Steps.First(step => step.Title.Contains("ADB", StringComparison.Ordinal));

        // Assert
        adbStep.Ordered.ShouldBeTrue();
        adbStep.DisplayPoints.ShouldAllBe(point => point.IsNumbered);
        adbStep.DisplayPoints.Select(point => point.Marker).ShouldBe(["1", "2", "3", "4", "5", "6"]);
    }

    [Fact]
    public void TheAdbStep_UnlocksDeveloperOptionsBeforeOpeningIt()
    {
        // The actual failure this ordering exists to prevent: Developer Options is hidden until it
        // is unlocked from About, so an instruction to open it cannot come first.

        // Arrange
        var onboarding = NewFlow();
        var adbStep = onboarding.Steps.First(step => step.Title.Contains("ADB", StringComparison.Ordinal));

        // Act
        var unlockAt = adbStep.Points
            .Select((text, index) => (text, index))
            .First(entry => entry.text.Contains("About", StringComparison.Ordinal)).index;
        var openAt = adbStep.Points
            .Select((text, index) => (text, index))
            .First(entry => entry.text.Contains("Open Developer Options", StringComparison.Ordinal)).index;

        // Assert
        unlockAt.ShouldBeLessThan(openAt);
    }

    [Fact]
    public void TheAdbStep_TellsPeopleToPressBackBeforeLookingForTheMenu()
    {
        // Leaving this out is what strands people inside About wondering where the menu went.

        // Arrange
        var onboarding = NewFlow();

        // Act
        var adbStep = onboarding.Steps.First(step => step.Title.Contains("ADB", StringComparison.Ordinal));

        // Assert
        string.Join(" ", adbStep.Points).ShouldContain("Press Back");
    }

    [Fact]
    public void UnorderedSteps_RenderWithoutNumbers()
    {
        // A set of facts is not a procedure, and numbering it would imply an order that is not real.

        // Arrange
        var onboarding = NewFlow();

        // Act
        var welcome = onboarding.Steps[0];

        // Assert
        welcome.Ordered.ShouldBeFalse();
        welcome.DisplayPoints.ShouldAllBe(point => !point.IsNumbered);
    }

    [Fact]
    public void TheIntroduction_StatesThatNothingLeavesTheNetwork()
    {
        // Arrange
        var onboarding = NewFlow();

        // Act
        var points = string.Join(" ", onboarding.Steps.SelectMany(step => step.Points));

        // Assert
        points.ShouldContain("network");
    }

    [Fact]
    public void BrowserWorkspaceStep_ExplainsPaneInputProfilesAndInternetBoundary()
    {
        var onboarding = NewFlow();
        var text = string.Join(
            " ",
            onboarding.Steps.SelectMany(step =>
                new[] { step.Title, step.Body }.Concat(step.Points)));

        text.ShouldContain("separate webpage");
        text.ShouldContain("focus confirmation");
        text.ShouldContain("Windows-device profile");
        text.ShouldContain("connect to those websites over the internet");
    }

    [Fact]
    public void Constructor_NullState_Throws()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => new OnboardingViewModel(null!));
    }

    private static OnboardingViewModel NewFlow() =>
        new(new FakeOnboardingState(completed: false));

    private static void AdvanceToLastStep(OnboardingViewModel onboarding)
    {
        while (!onboarding.IsLastStep)
        {
            onboarding.AdvanceCommand.Execute(null);
        }
    }

    private sealed class FakeOnboardingState(bool completed) : IOnboardingState
    {
        internal bool Completed { get; private set; } = completed;

        public bool HasCompleted => Completed;

        public void MarkCompleted() => Completed = true;

        public void Reset() => Completed = false;
    }
}
