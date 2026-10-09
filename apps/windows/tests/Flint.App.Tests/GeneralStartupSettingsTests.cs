using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Flint.App.ViewModels.Settings;
using Flint.App.Views;
using Flint.Core.Settings;
using Flint.Platform.Windows;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>Starting with Windows, the tray icon, and what closing the window does, in Settings.</summary>
public sealed class GeneralStartupSettingsTests : IDisposable
{
    private const string ThisCopy = @"C:\Users\Ada\AppData\Local\Flint\Flint.exe";

    private readonly SettingsService settings = new(new InMemoryAppSettingsStore());
    private readonly InMemoryRunKey runKey = new();
    private readonly HashSet<string> files = new(StringComparer.OrdinalIgnoreCase) { ThisCopy };

    public void Dispose() => settings.Dispose();

    [Fact]
    public void StartingWithWindows_WritesTheEntry_AndInTheTrayAddsMinimized()
    {
        var general = Section();

        general.CanStartWithWindows.ShouldBeTrue();
        general.StartWithWindows.ShouldBeFalse();
        general.StartsFile.ShouldBe($"Starts {ThisCopy}");

        general.StartWithWindows = true;
        runKey.Read(RunAtSignIn.EntryName).ShouldBe($"\"{ThisCopy}\"");
        settings.Current.General.StartWithWindows.ShouldBeTrue();

        general.StartInTray = true;
        runKey.Read(RunAtSignIn.EntryName).ShouldBe($"\"{ThisCopy}\" --minimized");
        general.StartWithWindows.ShouldBeTrue();

        general.StartWithWindows = false;
        runKey.Read(RunAtSignIn.EntryName).ShouldBeNull();
        settings.Current.General.StartWithWindows.ShouldBeFalse();

        general.StartInTray = false;
        runKey.Read(RunAtSignIn.EntryName).ShouldBeNull("changing the tray choice does not start Flint at sign-in");
    }

    [Fact]
    public void AnEntryForACopyThatMoved_IsShownOff_AndCanBePointedAtThisCopy()
    {
        runKey.Write(RunAtSignIn.EntryName, "\"D:\\Old place\\Flint.exe\"");
        var general = Section();

        general.StartWithWindows.ShouldBeFalse();
        general.StaleStart.ShouldBe("Flint was set to start from a copy that is no longer there.");

        general.StartThisCopyCommand.Execute(null);

        runKey.Read(RunAtSignIn.EntryName).ShouldBe($"\"{ThisCopy}\"");
        general.StaleStart.ShouldBeNull();
        general.StartWithWindows.ShouldBeTrue();
    }

    [Fact]
    public void WithoutAnEntryToWrite_StartingWithWindowsIsNotOffered()
    {
        var general = new GeneralSettingsViewModel(settings);

        general.CanStartWithWindows.ShouldBeFalse();
        general.StartWithWindows.ShouldBeFalse();
        general.StartsFile.ShouldBeNull();
        general.StaleStart.ShouldBeNull();
        general.StartWithWindows = true;
        general.StartThisCopyCommand.Execute(null);
        settings.Current.General.StartWithWindows.ShouldBeFalse();
        general.UseSignIn(null);
        general.CanStartWithWindows.ShouldBeFalse("a copy whose file Windows cannot name has no entry to offer");
    }

    [Fact]
    public void TheTrayAndClosingRows_ReadAndWriteTheirSettings()
    {
        var general = Section();

        general.ShowTrayIcon.ShouldBeTrue();
        general.SingleClickOpens.ShouldBeTrue();
        general.ConfirmQuit.ShouldBeTrue();
        general.CloseWindow.Label.ShouldBe("Ask me the first time");
        general.CloseWindowLocked.ShouldBeNull();

        general.CloseWindow = general.CloseWindowChoices[1];
        general.SingleClickOpens = false;
        general.ConfirmQuit = false;
        general.CloseWindow = null!;

        settings.Current.General.CloseWindow.ShouldBe(CloseWindowOutcome.KeepRunning);
        settings.Current.Tray.SingleClickOpens.ShouldBeFalse();
        settings.Current.General.ConfirmQuitWhileActive.ShouldBeFalse();
        general.CloseWindow.Label.ShouldBe("Keep running in the tray");
    }

    [Fact]
    public void WithoutTheTrayIcon_ClosingQuits_AndTheRowSaysWhy()
    {
        var general = Section();
        var told = new List<string?>();
        general.PropertyChanged += (_, change) => told.Add(change.PropertyName);

        general.ShowTrayIcon = false;

        settings.Current.Tray.ShowIcon.ShouldBeFalse();
        told.ShouldContain(string.Empty, "a tray change refreshes the section");
        general.CloseWindow.Label.ShouldBe("Quit Flint");
        general.CloseWindowLocked.ShouldBe("Without the tray icon, closing the window quits Flint.");
    }

    [Fact]
    public void TheNewRows_CanBeFound_ByWhatTheyDo()
    {
        var general = Section();

        general.Settings.Single(setting => setting.Matches("sign in to windows")).ShouldBe(general.StartWithWindowsText);
        general.Settings.Single(setting => setting.Matches("show notification area")).ShouldBe(general.ShowTrayIconText);
        general.Settings.ShouldContain(general.SingleClickText);
        general.Settings.ShouldContain(general.CloseWindowText);
        general.Settings.ShouldContain(general.ConfirmQuitText);
        general.Settings.ShouldContain(general.StartInTrayText);
    }

    [AvaloniaFact]
    public void TheSection_ShowsTheRows_AndOnlyOffersStartingWithWindowsWhereItCan()
    {
        var general = Section();
        var section = new GeneralSettingsSection { DataContext = general };
        var window = new Window { Width = 900, Height = 1600, Content = section };
        try
        {
            window.Show();
            window.UpdateLayout();

            section.FindControl<ToggleSwitch>("StartWithWindows")!.IsEffectivelyVisible.ShouldBeTrue();
            section.FindControl<ComboBox>("CloseWindow")!.SelectedItem.ShouldBe(general.CloseWindow);
            section.FindControl<TextBlock>("StartsFile")!.Text.ShouldBe($"Starts {ThisCopy}");

            section.DataContext = new GeneralSettingsViewModel(settings);
            window.UpdateLayout();
            section.FindControl<ToggleSwitch>("StartWithWindows")!.IsEffectivelyVisible.ShouldBeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    private GeneralSettingsViewModel Section()
    {
        var general = new GeneralSettingsViewModel(settings);
        general.UseSignIn(new RunAtSignIn(runKey, ThisCopy, files.Contains));
        return general;
    }
}
