using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Flint.App.ViewModels;
using Flint.App.Views;
using Flint.Core;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// The introduction rendered through its real template, so a binding that silently resolves to
/// nothing fails here rather than showing a first-time user a blank screen.
/// </summary>
public sealed class OnboardingViewTests
{
    [AvaloniaFact]
    public void FirstStep_ShowsItsTitleAndBody()
    {
        // Arrange
        var onboarding = NewFlow();

        // Act
        var texts = Texts(Render(onboarding));

        // Assert
        texts.ShouldContain(onboarding.Current.Title);
        texts.ShouldContain(onboarding.Current.Body);
    }

    [AvaloniaFact]
    public void TheBrandLockupIsPresentSoTheIntroductionIsRecognisablyRex()
    {
        // Arrange
        var onboarding = NewFlow();

        // Act
        var texts = Texts(Render(onboarding));

        // Assert
        texts.ShouldContain("REX");
        texts.ShouldContain(MainWindowViewModel.MakerName);
        texts.ShouldContain(MainWindowViewModel.ProductName);
    }

    [AvaloniaFact]
    public void EveryPointOnTheStepIsRendered()
    {
        // The points carry the actual instructions; dropping one silently would be a defect.

        // Arrange
        var onboarding = NewFlow();

        // Act
        var texts = Texts(Render(onboarding));

        // Assert
        foreach (var point in onboarding.Current.Points)
        {
            texts.ShouldContain(point);
        }
    }

    [AvaloniaFact]
    public void BackIsDisabledOnTheFirstStepAndEnabledAfterwards()
    {
        // Arrange
        var onboarding = NewFlow();
        var view = Render(onboarding);
        var back = Buttons(view).First(button => Equals(button.Content, "BACK"));

        // Assert
        back.IsEnabled.ShouldBeFalse();

        // Act
        onboarding.AdvanceCommand.Execute(null);
        view.UpdateLayout();

        // Assert
        back.IsEnabled.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void TheForwardButtonBecomesTheFinishActionOnTheLastStep()
    {
        // Arrange
        var onboarding = NewFlow();
        var view = Render(onboarding);

        // Act
        while (!onboarding.IsLastStep)
        {
            onboarding.AdvanceCommand.Execute(null);
        }

        view.UpdateLayout();

        // Assert
        Texts(view).ShouldContain("PROBE MY NETWORK");
    }

    [AvaloniaFact]
    public void AdvancingChangesTheRenderedStep()
    {
        // Arrange
        var onboarding = NewFlow();
        var view = Render(onboarding);
        var firstTitle = onboarding.Current.Title;

        // Act
        onboarding.AdvanceCommand.Execute(null);
        view.UpdateLayout();

        // Assert
        var texts = Texts(view);
        texts.ShouldContain(onboarding.Current.Title);
        texts.ShouldNotContain(firstTitle);
    }

    [AvaloniaFact]
    public void TheSkipAffordanceIsOffered()
    {
        // Arrange
        var onboarding = NewFlow();

        // Act
        var texts = Texts(Render(onboarding));

        // Assert
        texts.ShouldContain("SKIP");
    }

    [AvaloniaFact]
    public void TheProgressCounterIsShown()
    {
        // Arrange
        var onboarding = NewFlow();

        // Act
        var texts = Texts(Render(onboarding));

        // Assert
        texts.ShouldContain($"1 / {onboarding.Steps.Count}");
    }

    [AvaloniaFact]
    public void EveryStepRendersWithoutABlankScreen()
    {
        // Walks the whole flow, because a step with a broken binding would only show on that step.

        // Arrange
        var onboarding = NewFlow();
        var view = Render(onboarding);

        // Act & Assert
        for (var index = 0; index < onboarding.Steps.Count; index++)
        {
            onboarding.GoToStep(index);
            view.UpdateLayout();

            var texts = Texts(view);
            texts.ShouldContain(onboarding.Steps[index].Title, $"step {index} lost its title");
            texts.ShouldContain(onboarding.Steps[index].Body, $"step {index} lost its body");
        }
    }

    private static OnboardingViewModel NewFlow() => new(new FakeState());

    private static OnboardingView Render(OnboardingViewModel onboarding)
    {
        var view = new OnboardingView { DataContext = onboarding };
        var window = new Window { Width = 1180, Height = 780, Content = view };
        window.Show();
        window.UpdateLayout();
        return view;
    }

    private static List<string> Texts(Control root) =>
    [
        .. root.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(block => block.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!),
    ];

    private static List<Button> Buttons(Control root) =>
        [.. root.GetVisualDescendants().OfType<Button>()];

    private sealed class FakeState : IOnboardingState
    {
        private bool _completed;

        public bool HasCompleted => _completed;

        public void MarkCompleted() => _completed = true;

        public void Reset() => _completed = false;
    }
}
