using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.App.ViewModels.Settings;
using Flint.App.Views;
using Flint.Core;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>"What's new": what it shows after an update, when it shows nothing, and where it is reopened.</summary>
public sealed class WhatsNewTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), $"flint-whatsnew-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AfterAnUpdateFromAnOlderCopy_EveryHighlightIsShown_ThenNeverAgain()
    {
        var state = new InMemoryWhatsNewState();
        var whatsNew = new WhatsNewViewModel(state, introductionDone: true);

        whatsNew.IsVisible.ShouldBeTrue();
        whatsNew.Steps.Count.ShouldBe(WhatsNewViewModel.Catalogue.Count);
        whatsNew.Progress.ShouldBe($"1 / {WhatsNewViewModel.Catalogue.Count}");
        whatsNew.Current.Eyebrow.ShouldBe("New in Flint");

        while (!whatsNew.IsLastStep)
        {
            whatsNew.AdvanceCommand.Execute(null);
        }

        whatsNew.AdvanceLabel.ShouldBe("GOT IT");
        whatsNew.AdvanceCommand.Execute(null);

        whatsNew.IsVisible.ShouldBeFalse();
        state.Seen.ShouldBe(WhatsNewViewModel.Catalogue.Select(highlight => highlight.Id), ignoreOrder: true);
        new WhatsNewViewModel(state, introductionDone: true).IsVisible.ShouldBeFalse("there is nothing newer");
    }

    [Fact]
    public void OnlyHighlightsNotSeenYet_AreShown()
    {
        var first = WhatsNewViewModel.Catalogue[0];
        var state = new InMemoryWhatsNewState(WhatsNewViewModel.Catalogue.Skip(1).Select(highlight => highlight.Id));

        var whatsNew = new WhatsNewViewModel(state, introductionDone: true);

        whatsNew.Steps.ShouldBe([first.Step]);
        whatsNew.IsLastStep.ShouldBeTrue();
        whatsNew.AdvanceLabel.ShouldBe("GOT IT");
    }

    [Fact]
    public void AFirstLaunch_ShowsNothing()
    {
        var whatsNew = new WhatsNewViewModel(new InMemoryWhatsNewState(), introductionDone: false);

        whatsNew.IsVisible.ShouldBeFalse();
        whatsNew.Steps.Count.ShouldBe(WhatsNewViewModel.Catalogue.Count, "the steps are ready, should it be opened");
    }

    [Fact]
    public void Skipping_StillMarksEverythingSeen()
    {
        var state = new InMemoryWhatsNewState();
        var whatsNew = new WhatsNewViewModel(state, introductionDone: true);

        whatsNew.SkipCommand.Execute(null);

        whatsNew.IsVisible.ShouldBeFalse();
        state.Seen!.Count.ShouldBe(WhatsNewViewModel.Catalogue.Count);
    }

    [Fact]
    public void ShowAll_OpensEveryHighlightFromTheFirst_EvenWhenAllAreSeen()
    {
        var state = new InMemoryWhatsNewState(WhatsNewViewModel.Catalogue.Select(highlight => highlight.Id));
        var whatsNew = new WhatsNewViewModel(state, introductionDone: true);
        var raised = new List<string?>();
        whatsNew.PropertyChanged += (_, change) => raised.Add(change.PropertyName);

        whatsNew.ShowAll();

        whatsNew.IsVisible.ShouldBeTrue();
        whatsNew.StepIndex.ShouldBe(0);
        whatsNew.Steps.Count.ShouldBe(WhatsNewViewModel.Catalogue.Count);
        raised.ShouldContain(nameof(WalkthroughViewModel.Current));
        raised.ShouldContain(nameof(WalkthroughViewModel.Steps));
    }

    [Fact]
    public void BackAndNext_MoveThroughTheSteps_AndGoToStepIgnoresStepsThatDoNotExist()
    {
        var whatsNew = new WhatsNewViewModel(new InMemoryWhatsNewState(), introductionDone: true);

        whatsNew.CanGoBack.ShouldBeFalse();
        whatsNew.GoBackCommand.Execute(null);
        whatsNew.StepIndex.ShouldBe(0);
        whatsNew.AdvanceCommand.Execute(null);
        whatsNew.CanGoBack.ShouldBeTrue();
        whatsNew.GoBackCommand.Execute(null);
        whatsNew.StepIndex.ShouldBe(0);
        whatsNew.GoToStep(99);
        whatsNew.GoToStep(-1);
        whatsNew.StepIndex.ShouldBe(0);
    }

    [Fact]
    public void ACatalogueOfItsOwn_IsUsed_AndAnEmptyOneIsRefused()
    {
        var step = new OnboardingStep("New", "Only this", "Body", []);
        var whatsNew = new WhatsNewViewModel(new InMemoryWhatsNewState(), true, [new WhatsNewHighlight("only", step)]);

        whatsNew.Steps.ShouldBe([step]);
        whatsNew.Highlights.Single().Id.ShouldBe("only");
        Should.Throw<ArgumentException>(() => new WhatsNewViewModel(new InMemoryWhatsNewState(), true, []));
        Should.Throw<ArgumentNullException>(() => new WhatsNewViewModel(null!, true));
    }

    [Fact]
    public void TheCatalogue_HasUniqueIds_AndNoEmptyHighlight()
    {
        var ids = WhatsNewViewModel.Catalogue.Select(highlight => highlight.Id).ToList();

        ids.ShouldNotBeEmpty();
        ids.Distinct(StringComparer.Ordinal).Count().ShouldBe(ids.Count);
        WhatsNewViewModel.Catalogue.ShouldAllBe(highlight =>
            highlight.Id.Length > 0 && highlight.Step.Title.Length > 0 && highlight.Step.Body.Length > 0);
    }

    [Fact]
    public void TheShell_HoldsWhatsNewBehindTheIntroduction_AndTheIntroductionMarksItSeen()
    {
        var onboarding = new UnseenOnboarding();
        var state = new InMemoryWhatsNewState();
        using var shell = MainWindowViewModel.CreateWith(
            BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()),
            onboardingState: onboarding,
            whatsNewState: state);
        var raised = new List<string?>();
        shell.PropertyChanged += (_, change) => raised.Add(change.PropertyName);

        shell.Onboarding.IsVisible.ShouldBeTrue();
        shell.ShowsWhatsNew.ShouldBeFalse("a first launch is introduced, not told what changed");

        shell.Onboarding.SkipCommand.Execute(null);

        state.Seen!.Count.ShouldBe(WhatsNewViewModel.Catalogue.Count);
        raised.ShouldContain(nameof(MainWindowViewModel.ShowsWhatsNew));

        shell.WhatsNew.ShowAll();
        shell.ShowsWhatsNew.ShouldBeTrue();
        shell.Onboarding.RestartCommand.Execute(null);
        shell.ShowsWhatsNew.ShouldBeFalse("the introduction always comes first");
    }

    [Fact]
    public void TheShell_ShowsWhatsNew_AfterAnUpdate()
    {
        using var shell = MainWindowViewModel.CreateWith(
            BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()),
            whatsNewState: new InMemoryWhatsNewState());

        shell.ShowsWhatsNew.ShouldBeTrue();
    }

    [Fact]
    public void TheShell_ByDefault_ShowsNoWhatsNew()
    {
        using var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));

        shell.ShowsWhatsNew.ShouldBeFalse();
    }

    [Fact]
    public void About_OpensWhatsNewAgain_AndSaysItCan()
    {
        var cast = Snapshots.SnapshotFixtures.ViewModel();
        var whatsNew = new WhatsNewViewModel(new InMemoryWhatsNewState(WhatsNewViewModel.Catalogue.Select(h => h.Id)), true);
        var about = new AboutSettingsViewModel(cast, "v1", "0.1.0", "Windows 11", whatsNew);

        about.HasWhatsNew.ShouldBeTrue();
        about.Settings.ShouldContain(about.WhatsNewText);
        about.ShowWhatsNewCommand.Execute(null);
        whatsNew.IsVisible.ShouldBeTrue();

        var without = new AboutSettingsViewModel(cast, "v1", "0.1.0", "Windows 11");
        without.HasWhatsNew.ShouldBeFalse();
        Should.NotThrow(() => without.ShowWhatsNewCommand.Execute(null));
    }

    [Fact]
    public void TheFileStore_IsUnrecordedUntilWritten_ThenRemembers()
    {
        var store = new FileWhatsNewState(folder);
        store.Seen.ShouldBeNull();

        store.MarkSeen(["a", "b"]);
        store.MarkSeen(["c"]);

        new FileWhatsNewState(folder).Seen.ShouldBe(["a", "b", "c"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"an\":\"object\"}")]
    public void ADamagedFile_ReadsAsUnrecorded(string text)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, FileWhatsNewState.FileName), text);

        new FileWhatsNewState(folder).Seen.ShouldBeNull();
    }

    [Fact]
    public void BlankIdsInTheFile_AreLeftOut_AndANullListIsEmpty()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, FileWhatsNewState.FileName), "[\"a\",\"\",\" \"]");
        new FileWhatsNewState(folder).Seen.ShouldBe(["a"]);

        File.WriteAllText(Path.Combine(folder, FileWhatsNewState.FileName), "null");
        new FileWhatsNewState(folder).Seen.ShouldBeEmpty();
    }

    [Fact]
    public void AFolderThatCannotBeWritten_RemembersForThisRunOnly()
    {
        Directory.CreateDirectory(folder);
        var blocked = Path.Combine(folder, "blocked");
        File.WriteAllText(blocked, "a file where the folder should be");
        var store = new FileWhatsNewState(blocked);

        Should.NotThrow(() => store.MarkSeen(["a"]));
        store.Seen.ShouldBe(["a"]);
        Should.Throw<ArgumentNullException>(() => store.MarkSeen(null!));
        Should.Throw<ArgumentException>(() => new FileWhatsNewState(" "));
        new FileWhatsNewState().ShouldNotBeNull();
    }

    [AvaloniaFact]
    public void TheWindow_CoversItselfWithWhatsNew_AfterAnUpdate()
    {
        var shell = MainWindowViewModel.CreateWith(
            BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()),
            whatsNewState: new InMemoryWhatsNewState());
        var window = new MainWindow { DataContext = shell, Width = 1180, Height = 780 };
        try
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            window.FindControl<OnboardingView>("WhatsNewView")!.IsVisible.ShouldBeTrue();
            shell.WhatsNew.SkipCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            window.FindControl<OnboardingView>("WhatsNewView")!.IsVisible.ShouldBeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>A first launch: the introduction has not been seen, and finishing it is remembered.</summary>
    private sealed class UnseenOnboarding : IOnboardingState
    {
        public bool HasCompleted { get; private set; }

        public void MarkCompleted() => HasCompleted = true;

        public void Reset() => HasCompleted = false;
    }
}
