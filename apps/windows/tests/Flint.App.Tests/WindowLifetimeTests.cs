using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flint.App.ViewModels;
using Flint.App.Views;
using Flint.Core.Settings;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>Closing the window, quitting, and the questions Flint asks first.</summary>
public sealed class WindowLifetimeTests
{
    private readonly MainWindowViewModel shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
    private readonly RecordingWindow window = new();

    public WindowLifetimeTests() => shell.UseWindow(window);

    [AvaloniaTheory]
    [InlineData(true, false, "sharing your screen")]
    [InlineData(true, true, "sharing your screen")]
    [InlineData(false, true, "what is playing")]
    [InlineData(false, false, "the TV browser")]
    public void TheQuitQuestion_NamesWhatQuittingWouldStop(bool sharing, bool playing, string named) =>
        MainWindowViewModel.WhatIsOnTheTv(sharing, playing).ShouldBe(named);

    [AvaloniaFact]
    public async Task TheFirstClose_AsksWhetherToKeepRunning_AndRemembersKeepRunning()
    {
        var closing = shell.CloseWindowAsync();

        shell.CloseQuestion.IsOpen.ShouldBeTrue();
        shell.CloseQuestion.Title.ShouldBe("Keep Flint running in the tray?");
        shell.CloseQuestion.FirstLabel.ShouldBe("KEEP RUNNING");
        shell.CloseQuestion.SecondLabel.ShouldBe("QUIT FLINT");
        shell.CloseQuestion.ChooseFirstCommand.Execute(null);
        await closing;

        window.Hidden.ShouldBe(1);
        window.ShutDown.ShouldBe(0);
        shell.SettingsService.Current.General.CloseWindow.ShouldBe(CloseWindowOutcome.KeepRunning);

        await shell.CloseWindowAsync();
        window.Hidden.ShouldBe(2, "remembered, so not asked again");
        shell.CloseQuestion.IsOpen.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task QuitFlint_IsRemembered_AndQuits()
    {
        var closing = shell.CloseWindowAsync();
        shell.CloseQuestion.ChooseSecondCommand.Execute(null);
        await closing;

        window.ShutDown.ShouldBe(1);
        shell.IsQuitting.ShouldBeTrue();
        shell.SettingsService.Current.General.CloseWindow.ShouldBe(CloseWindowOutcome.Quit);
    }

    [AvaloniaFact]
    public async Task PuttingTheQuestionAway_DoesNothing_AndRemembersNothing()
    {
        var closing = shell.CloseWindowAsync();
        shell.CloseQuestion.CancelCommand.Execute(null);
        await closing;

        window.Hidden.ShouldBe(0);
        window.ShutDown.ShouldBe(0);
        shell.SettingsService.Current.General.CloseWindow.ShouldBe(CloseWindowOutcome.Ask);
    }

    [AvaloniaFact]
    public async Task WithoutATrayIcon_ClosingWhileSharing_AsksBeforeStoppingIt()
    {
        shell.SettingsService.Update(current => current with { Tray = current.Tray with { ShowIcon = false } });
        shell.Cast.IsMirroring = true;

        var closing = shell.CloseWindowAsync();
        shell.CloseQuestion.Title.ShouldBe("Quit and stop sharing your screen?");
        shell.CloseQuestion.ChooseSecondCommand.Execute(null);
        await closing;
        window.ShutDown.ShouldBe(0, "CANCEL keeps Flint running");

        closing = shell.CloseWindowAsync();
        shell.CloseQuestion.ChooseFirstCommand.Execute(null);
        await closing;
        window.ShutDown.ShouldBe(1);
        shell.Cast.IsMirroring = false;
    }

    [AvaloniaFact]
    public async Task QuittingFromTheTray_WhilePlaying_ShowsTheWindowToAsk()
    {
        shell.Media.NowPlaying.BeginSending("Film.mp4", isPicture: false);

        var quitting = shell.QuitAsync();

        window.Shown.ShouldBe(1, "a question is never asked behind a hidden window");
        shell.CloseQuestion.Title.ShouldBe("Quit and stop what is playing?");
        shell.CloseQuestion.ChooseFirstCommand.Execute(null);
        await quitting;
        window.ShutDown.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task PuttingAwayTheQuestionAboutQuitting_KeepsFlintRunning()
    {
        shell.Cast.IsMirroring = true;

        var quitting = shell.QuitAsync();
        shell.CloseQuestion.CancelCommand.Execute(null);
        await quitting;

        window.ShutDown.ShouldBe(0);
        shell.IsQuitting.ShouldBeFalse();
        shell.Cast.IsMirroring = false;
    }

    [AvaloniaFact]
    public async Task WithTheQuestionTurnedOff_QuittingWhileSharingDoesNotAsk()
    {
        shell.SettingsService.Update(current => current with { General = current.General with { ConfirmQuitWhileActive = false } });
        shell.Cast.IsMirroring = true;

        await shell.QuitAsync();

        window.ShutDown.ShouldBe(1);
        shell.Cast.IsMirroring = false;
    }

    [AvaloniaFact]
    public void ASwitchQuestion_BringsTheWindowForward()
    {
        _ = shell.SwitchPrompt.AskAsync(new SurfaceSwitchCopy("Switch?", "Body", "SWITCH", "KEEP"));

        window.Shown.ShouldBe(1);
        shell.SwitchPrompt.Dismiss();
        window.Shown.ShouldBe(1, "closing the question shows nothing");
    }

    [AvaloniaFact]
    public async Task ANewQuestion_WithdrawsTheOldOne()
    {
        var first = shell.CloseQuestion.AskAsync("First?", "Body", "ONE", "TWO");
        var second = shell.CloseQuestion.AskAsync("Second?", "Body", "ONE", "TWO");

        (await first).ShouldBe(CloseAnswer.Cancelled);
        shell.CloseQuestion.Title.ShouldBe("Second?");
        shell.CloseQuestion.ChooseSecondCommand.Execute(null);
        (await second).ShouldBe(CloseAnswer.Second);
        shell.CloseQuestion.CancelCommand.Execute(null);
    }

    [AvaloniaFact]
    public void AShellWithoutAWindow_DoesNotPretendToHaveOne()
    {
        var bare = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));

        bare.HasWindowControl.ShouldBeFalse();
        Should.NotThrow(bare.ShowWindow);
        Should.Throw<ArgumentNullException>(() => bare.UseWindow(null!));
        shell.HasWindowControl.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task AShellWithoutAWindow_StillDecides_AndQuits()
    {
        var bare = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        bare.SettingsService.Update(current => current with { General = current.General with { CloseWindow = CloseWindowOutcome.KeepRunning } });

        await bare.CloseWindowAsync();
        var asking = bare.CloseWindowAsync();
        bare.SettingsService.Update(current => current with { General = current.General with { CloseWindow = CloseWindowOutcome.Ask } });
        await asking;
        var asked = bare.CloseWindowAsync();
        bare.CloseQuestion.ChooseFirstCommand.Execute(null);
        await asked;
        await bare.QuitAsync();

        bare.IsQuitting.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void ThePersonsOwnClose_IsTurnedIntoTheQuestion_AndEscapePutsItAway()
    {
        var real = new MainWindow { DataContext = shell, Width = 1180, Height = 780 };
        real.Show();

        real.Close();

        real.IsVisible.ShouldBeTrue("the close was turned into a question");
        shell.CloseQuestion.IsOpen.ShouldBeTrue();
        var overlay = real.FindControl<Border>("CloseOverlay")!;
        overlay.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        shell.CloseQuestion.IsOpen.ShouldBeFalse();
        overlay.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });

        var quitting = shell.QuitAsync();
        quitting.IsCompleted.ShouldBeTrue();
        real.Close();
        real.IsVisible.ShouldBeFalse("quitting goes straight through");
    }

    [AvaloniaFact]
    public void AWindowWithoutAShellControl_ClosesAsItAlwaysDid()
    {
        var bare = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        var real = new MainWindow { DataContext = bare, Width = 1180, Height = 780 };
        real.Show();

        real.Close();

        real.IsVisible.ShouldBeFalse();
        Should.NotThrow(bare.Dispose);
    }
}
