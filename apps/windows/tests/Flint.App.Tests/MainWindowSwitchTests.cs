using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Flint.App.ViewModels;
using Flint.App.Views;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// The shell's part in switching the TV: the question withdrawn when its page is left, the notice
/// that says what the TV is showing, and the window the question covers.
/// </summary>
public sealed class MainWindowSwitchTests
{
    private static readonly SurfaceSwitchCopy MirrorToBrowser =
        SurfaceSwitchCopy.For(TvSurfaceKind.Mirror, TvSurfaceKind.Browser, "Living Room");

    [Fact]
    public async Task LeavingAPage_WithdrawsItsQuestion()
    {
        var shell = Shell();
        var answer = shell.SwitchPrompt.AskAsync(MirrorToBrowser);

        shell.Selected = Destination(shell, "Settings");

        (await answer).ShouldBeFalse();
        shell.SwitchPrompt.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task TheNotice_ShowsOnThePagesTheTvIsNotShowing()
    {
        var shell = Shell();
        await shell.Cast.ProbeCommand.ExecuteAsync(null);
        SetMirroring(shell.Cast, true);

        shell.Selected = Destination(shell, "Cast");
        shell.ShowTvNotice.ShouldBeFalse("Cast puts nothing on the TV");

        shell.Selected = Destination(shell, "Web");
        shell.TvNotice.ShouldBe("Living Room is showing your screen.");
        shell.ShowTvNotice.ShouldBeTrue();
        shell.SwitchHereLabel.ShouldBe("SWITCH TO BROWSER");
        shell.CanSwitchHere.ShouldBeFalse("the browser has not been verified in this shell");

        shell.Selected = Destination(shell, "Screen");
        shell.ShowTvNotice.ShouldBeFalse("the mirror is Screen's own");
    }

    [Fact]
    public async Task TheNotice_FollowsTheTv_WhenWhatItShowsChanges()
    {
        var shell = Shell();
        await shell.Cast.ProbeCommand.ExecuteAsync(null);
        SetMirroring(shell.Cast, true);
        shell.Selected = Destination(shell, "Web");
        var raised = new List<string?>();
        shell.PropertyChanged += (_, change) => raised.Add(change.PropertyName);

        await shell.Cast.StopMirrorAsync(TestContext.Current.CancellationToken);

        raised.ShouldContain(nameof(MainWindowViewModel.TvNotice));
        raised.ShouldContain(nameof(MainWindowViewModel.CanSwitchHere));
        shell.TvNotice.ShouldBeNull();
    }

    [Fact]
    public async Task ArrivingAtWebWhileMirroring_WithTheBrowserNotReady_AsksNothing()
    {
        var shell = Shell();
        await shell.Cast.ProbeCommand.ExecuteAsync(null);
        SetMirroring(shell.Cast, true);

        shell.Selected = Destination(shell, "Web");
        await shell.Browser.ActivateAsync();

        shell.SwitchPrompt.IsOpen.ShouldBeFalse("nothing can switch to a browser that is not connected yet");
        shell.Cast.IsMirroring.ShouldBeTrue();
    }

    [Fact]
    public async Task SwitchHere_WhereTheTvCannotBeTakenOver_DoesNothing()
    {
        var shell = Shell();
        await shell.Cast.ProbeCommand.ExecuteAsync(null);
        SetMirroring(shell.Cast, true);
        shell.Selected = Destination(shell, "Web");

        await shell.SwitchHereCommand.ExecuteAsync(null);

        shell.Cast.IsMirroring.ShouldBeTrue();
        shell.SwitchPrompt.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public void TheSwitchLabel_NamesWhatThePageWouldPutUp()
    {
        var shell = Shell();

        shell.Selected = Destination(shell, "Screen");

        shell.SwitchHereLabel.ShouldBe("MIRROR INSTEAD");
    }

    [AvaloniaFact]
    public async Task TheQuestion_CoversTheWindow_TakesFocus_AndEscapeKeeps()
    {
        var shell = Shell();
        var window = new MainWindow { DataContext = shell, Width = 1180, Height = 780 };
        try
        {
            window.Show();
            Settle(window);
            IsShowing(window, "Switch the TV").ShouldBeFalse();

            var answer = shell.SwitchPrompt.AskAsync(MirrorToBrowser);
            Settle(window);

            var confirm = Find(window, "Switch the TV");
            confirm.IsEffectivelyVisible.ShouldBeTrue();
            confirm.IsFocused.ShouldBeTrue("the question takes focus so a key answers it");
            Texts(window).ShouldContain(MirrorToBrowser.Body);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Settle(window);

            answer.IsCompleted.ShouldBeTrue("a key the question hears answers it at once");
            (await answer).ShouldBeFalse();
            IsShowing(window, "Switch the TV").ShouldBeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task EnterOnTheFocusedSwitchButton_Switches()
    {
        var shell = Shell();
        var window = new MainWindow { DataContext = shell, Width = 1180, Height = 780 };
        try
        {
            window.Show();
            Settle(window);
            var answer = shell.SwitchPrompt.AskAsync(MirrorToBrowser);
            Settle(window);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Settle(window);

            answer.IsCompleted.ShouldBeTrue("a key the question hears answers it at once");
            (await answer).ShouldBeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void WithTheQuestionClosed_EnterAndEscapeStillReachTheWindow()
    {
        // The window watches Enter for the question's sake; with no question open, it and Escape
        // carry on to whatever else listens.
        var shell = Shell();
        var window = new MainWindow { DataContext = shell, Width = 1180, Height = 780 };
        var reached = new List<Key>();
        try
        {
            window.Show();
            Settle(window);
            // Asked and answered once, so the question's buttons are built and stay in the window.
            var answered = shell.SwitchPrompt.AskAsync(MirrorToBrowser);
            Settle(window);
            shell.SwitchPrompt.KeepCommand.Execute(null);
            Settle(window);
            answered.IsCompleted.ShouldBeTrue();
            window.AddHandler(InputElement.KeyDownEvent, (_, args) => reached.Add(args.Key), RoutingStrategies.Bubble);
            // Nothing holds focus, so no control in between handles either key before the window does.
            window.Focus();
            Settle(window);
            window.FocusManager!.GetFocusedElement().ShouldBeOneOf(null, window);

            foreach (var key in new[] { PhysicalKey.Enter, PhysicalKey.Escape })
            {
                window.KeyPressQwerty(key, RawInputModifiers.None);
                window.KeyReleaseQwerty(key, RawInputModifiers.None);
            }

            Settle(window);
            reached.ShouldBe([Key.Enter, Key.Escape]);
            shell.SwitchPrompt.IsOpen.ShouldBeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AnEnterHeldFromOpeningTheQuestion_DoesNotAnswerIt()
    {
        // Enter in the address bar opens the question; held a moment, it repeats onto the switch
        // button. Only a fresh press answers.
        var shell = Shell();
        var window = new MainWindow { DataContext = shell, Width = 1180, Height = 780 };
        try
        {
            window.Show();
            Settle(window);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            var answer = shell.SwitchPrompt.AskAsync(MirrorToBrowser);
            Settle(window);
            Find(window, "Switch the TV").IsFocused.ShouldBeTrue();

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Settle(window);
            answer.IsCompleted.ShouldBeFalse("the key that opened the question is still down");

            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Settle(window);

            answer.IsCompleted.ShouldBeTrue("a key the question hears answers it at once");
            (await answer).ShouldBeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AnEnterReleasedInAnotherWindow_IsNotStillHeldHere()
    {
        // Its release went to the other window, so this one never saw the key come up.
        var shell = Shell();
        var window = new MainWindow { DataContext = shell, Width = 1180, Height = 780 };
        try
        {
            window.Show();
            window.Activate();
            Settle(window);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Deactivate(window);
            window.IsActive.ShouldBeFalse();
            window.Activate();
            var answer = shell.SwitchPrompt.AskAsync(MirrorToBrowser);
            Settle(window);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Settle(window);

            answer.IsCompleted.ShouldBeTrue("a key the question hears answers it at once");
            (await answer).ShouldBeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TheNoticeStrip_ShowsWhatTheTvIsShowing()
    {
        var shell = Shell();
        SetMirroring(shell.Cast, true);
        var window = new MainWindow { DataContext = shell, Width = 1180, Height = 780 };
        try
        {
            window.Show();
            shell.Selected = Destination(shell, "Web");
            Settle(window);

            IsShowing(window, "What the TV is showing").ShouldBeTrue();
            Texts(window).ShouldContain("The TV is showing your screen.");
            IsShowing(window, "Switch the TV to this page").ShouldBeFalse("the browser is not ready to take over");
        }
        finally
        {
            window.Close();
        }
    }

    private static MainWindowViewModel Shell() =>
        MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));

    private static NavigationDestination Destination(MainWindowViewModel shell, string label) =>
        shell.Destinations.Single(destination => destination.Label == label);

    private static void SetMirroring(CastPageViewModel cast, bool value) =>
        typeof(CastPageViewModel)
            .GetField("_isMirroring", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(cast, value);

    /// <summary>What the platform does when another app takes focus; headless has no other app.</summary>
    private static void Deactivate(Window window)
    {
        var platform = typeof(TopLevel).GetProperty("PlatformImpl")!.GetValue(window)!;
        var deactivated = (Action)platform.GetType().GetProperty("Deactivated")!.GetValue(platform)!;
        deactivated();
        Settle(window);
    }

    private static void Settle(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Whether a control with that name is on screen; one never shown is not in the tree yet.</summary>
    private static bool IsShowing(Window window, string name) => window.GetVisualDescendants()
        .OfType<Control>()
        .Any(control => AutomationProperties.GetName(control) == name && control.IsEffectivelyVisible);

    private static Control Find(Window window, string name) => window.GetVisualDescendants()
        .OfType<Control>()
        .First(control => AutomationProperties.GetName(control) == name);

    private static List<string> Texts(Window window) => window.GetVisualDescendants()
        .OfType<TextBlock>()
        .Where(text => text.IsEffectivelyVisible)
        .Select(text => text.Text ?? string.Empty)
        .ToList();
}
