using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.Platform.Windows;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>How Flint starts: shown or in the tray, quitting only when asked, and a second launch.</summary>
public sealed class AppStartupTests
{
    [AvaloniaFact]
    public async Task Starting_ShowsTheWindow_QuitsThroughTheApplication_AndPutsEverythingAway()
    {
        var shell = Shell();
        var shutDowns = 0;

        using var started = FlintApplication.Start(Application.Current!, shell, [], () => shutDowns++, null, GlobalShortcuts.Start);

        started.ShowAtStart.ShouldBeTrue();
        started.Window.DataContext.ShouldBeSameAs(shell);
        shell.HasWindowControl.ShouldBeTrue();
        await shell.QuitAsync();
        shutDowns.ShouldBe(1);
    }

    [AvaloniaFact]
    public void StartingAtSignInInTheTray_KeepsTheWindowHidden_OnlyWithAnIcon()
    {
        using var hidden = FlintApplication.Start(Application.Current!, Shell(), [RunAtSignIn.MinimizedArgument], () => { }, null, GlobalShortcuts.Start);
        hidden.ShowAtStart.ShouldBeFalse();

        var withoutIcon = Shell();
        withoutIcon.SettingsService.Update(current => current with { Tray = current.Tray with { ShowIcon = false } });
        using var shown = FlintApplication.Start(Application.Current!, withoutIcon, ["--MINIMIZED"], () => { }, null, GlobalShortcuts.Start);
        shown.ShowAtStart.ShouldBeTrue("never an invisible app with no icon");

        using var noArguments = FlintApplication.Start(Application.Current!, Shell(), null, () => { }, null, GlobalShortcuts.Start);
        noArguments.ShowAtStart.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task ASecondLaunch_BringsTheWindowForward()
    {
        var name = $@"Local\Flint-test-{Guid.NewGuid():N}";
        using var instance = FlintSingleInstance.TryAcquire(name).ShouldNotBeNull();
        using var started = FlintApplication.Start(Application.Current!, Shell(), [RunAtSignIn.MinimizedArgument], () => { }, instance, GlobalShortcuts.Start);
        started.Window.IsVisible.ShouldBeFalse();

        FlintSingleInstance.AskToShow(name).ShouldBeTrue();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!started.Window.IsVisible && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        started.Window.IsVisible.ShouldBeTrue();
        started.Window.Close();
    }

    [AvaloniaFact]
    public void WhenWindowsWillNotMakeTheShortcutWindow_FlintStartsWithoutShortcuts()
    {
        GlobalShortcuts.Start(Shell(), () => null).ShouldBeNull();

        var shell = Shell();
        var started = FlintApplication.Start(Application.Current!, shell, [], () => { }, null, _ => null);
        started.ShowAtStart.ShouldBeTrue();
        started.Dispose();
        shell.HasWindowControl.ShouldBeTrue();
    }

    /// <summary>A shell whose shortcuts work only in its window, so starting claims nothing from Windows.</summary>
    private static MainWindowViewModel Shell()
    {
        var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        shell.SettingsService.Update(current => current with { Shortcuts = current.Shortcuts with { WorkInBackground = false } });
        return shell;
    }
}
