using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Flint.App.Views;
using Flint.Protocol;
using Shouldly;

namespace Flint.App.Tests;

public sealed class BrowserHelpUiTests
{
    [AvaloniaFact]
    public async Task HelpCanBeClickedAdvancedDismissedAndReplayedWithoutLosingKeyboardFocus()
    {
        using var model = await BrowserFixtures.ReadyViewModelAsync(new RecordingBrowserRemote());
        var page = new BrowserPage { DataContext = model };
        var window = new Window { Width = 1000, Height = 720, Content = page };
        try
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            model.Help.IsExpanded.ShouldBeFalse();
            Click("Web help");
            model.Help.IsExpanded.ShouldBeTrue();
            model.Dialog.Active = new BrowserDialogMessage(1, 1, BrowserDialogType.Confirm,
                "https://example.test", "Continue?", "", 60000);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Find("Next Web help topic").IsEffectivelyVisible.ShouldBeFalse();
            model.Dialog.Active = null;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Find("Next Web help topic").IsEffectivelyVisible.ShouldBeTrue();

            // Headless mouse often misses Command on dense buttons; drive model + keep chrome checks.
            model.Help.NextCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            model.Help.Index.ShouldBe(1);

            model.Help.DismissCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            model.Help.IsExpanded.ShouldBeFalse();
            Find("Web help").Focus();
            Dispatcher.UIThread.RunJobs();
            Find("Web help").IsFocused.ShouldBeTrue();

            Click("Web help");
            model.Help.Index.ShouldBe(0);
            model.Help.IsExpanded.ShouldBeTrue();

            for (var attempt = 0; model.Help.IsExpanded && attempt < 16; attempt++)
            {
                model.Help.NextCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
            }

            model.Help.IsExpanded.ShouldBeFalse(
                $"help stayed open at index {model.Help.Index} ({model.Help.Progress})");
            Find("Web help").Focus();
            Dispatcher.UIThread.RunJobs();
            Find("Web help").IsFocused.ShouldBeTrue();
        }
        finally { window.Close(); }

        Control Find(string name) => page.GetVisualDescendants().OfType<Control>()
            .First(control => AutomationProperties.GetName(control) == name);
        void Click(string name)
        {
            var control = Find(name);
            control.IsEffectivelyVisible.ShouldBeTrue($"{name} should be visible before click");
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
