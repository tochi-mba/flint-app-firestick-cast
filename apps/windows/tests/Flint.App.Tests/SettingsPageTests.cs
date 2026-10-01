using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Flint.App.Controls;
using Flint.App.Tests.Snapshots;
using Flint.App.ViewModels.Settings;
using Flint.App.Views;
using Shouldly;

namespace Flint.App.Tests;

public sealed class SettingsPageTests
{
    [AvaloniaFact]
    public void GeneralControlsRenderWithNames_AndTheRealToggleBindingChangesTheSetting()
    {
        var model = SnapshotFixtures.Settings();
        var page = new SettingsPage { DataContext = model };
        var window = Show(page, 1024, 700);
        try
        {
            var controls = page.GetVisualDescendants().OfType<Control>().ToList();
            controls.OfType<ToggleSwitch>().Count().ShouldBe(2);
            // Only what can be seen and reached. A non-editable ComboBox carries a hidden text box in
            // its template that nobody can focus, and naming it would mean nothing to a screen reader.
            foreach (var control in controls.Where(control => control.IsEffectivelyVisible
                && control is ToggleSwitch or ComboBox or TextBox or Button or ListBox))
            {
                AutomationProperties.GetName(control).ShouldNotBeNullOrWhiteSpace($"{control.GetType().Name} '{control.Name}' has no accessible name");
            }

            // The toggle lives in the General section's own control, which has its own name scope.
            var toggle = controls.OfType<ToggleSwitch>().Single(control => control.Name == "AskBeforeSwitching");
            toggle.IsChecked = false;
            window.UpdateLayout();

            model.Section<GeneralSettingsViewModel>().AskBeforeSwitching.ShouldBeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void NarrowPageMovesTheSectionListAboveTheContentAndKeepsEverythingInside()
    {
        var page = new SettingsPage { DataContext = SnapshotFixtures.Settings() };
        var window = Show(page, 700, 620);
        try
        {
            var list = page.FindControl<ListBox>("SectionList")!;
            var content = page.FindControl<ScrollViewer>("SectionScroller")!;
            Grid.GetRow(list).ShouldBe(0);
            Grid.GetRow(content).ShouldBe(1);
            Grid.GetColumn(content).ShouldBe(0);
            list.Bounds.Right.ShouldBeLessThanOrEqualTo(page.Bounds.Right);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EverySectionGetsItsOwnRenderedSurface()
    {
        var model = SnapshotFixtures.Settings();
        var page = new SettingsPage { DataContext = model };
        var window = Show(page, 1024, 700);
        try
        {
            foreach (var section in model.Sections)
            {
                model.SelectedSection = section;
                window.UpdateLayout();
                page.GetVisualDescendants().OfType<TextBlock>().ShouldContain(text => text.Text == section.Title);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SegmentedChoiceUsesArrowKeysForOneSelection()
    {
        var choice = new SegmentedChoice { ItemsSource = new[] { "One", "Two", "Three" }, SelectedIndex = 0 };
        var window = Show(choice, 400, 100);
        try
        {
            // Tabbing into a list lands on its selected option; the arrows move from there.
            choice.ContainerFromIndex(0)!.Focus(NavigationMethod.Tab).ShouldBeTrue();

            Press(window, PhysicalKey.ArrowRight);
            choice.SelectedIndex.ShouldBe(1);
            Press(window, PhysicalKey.ArrowRight);
            Press(window, PhysicalKey.ArrowRight);
            choice.SelectedIndex.ShouldBe(2, "the last option is as far as the arrows go");
            Press(window, PhysicalKey.ArrowLeft);
            choice.SelectedIndex.ShouldBe(1);
            choice.SelectedItems!.Count.ShouldBe(1, "a segmented choice never holds two options");
        }
        finally
        {
            window.Close();
        }
    }

    private static void Press(Window window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.None);
        window.KeyReleaseQwerty(key, RawInputModifiers.None);
        window.UpdateLayout();
    }

    [AvaloniaFact]
    public void Privacy_ListsWhatFlintKeepsInWords()
    {
        // The list once showed "{Binding}" five times: text with braces in it is not a binding.
        var model = SnapshotFixtures.Settings();
        model.SelectedSection = model.Section<PrivacySettingsViewModel>();
        var page = new SettingsPage { DataContext = model };
        var window = Show(page, 1024, 900);
        try
        {
            var texts = Texts(page);
            texts.ShouldContain("•  Your settings.");
            texts.ShouldNotContain(text => text.Contains("{Binding", StringComparison.Ordinal));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SearchResults_ShowEachSettingsSectionTitleAndExplanation()
    {
        // Each result once read "AVALONIA.CONTROLS.STACKPANEL": a label button cannot show a layout.
        var model = SnapshotFixtures.Settings();
        model.SearchText = "folder";
        var page = new SettingsPage { DataContext = model };
        var window = Show(page, 1024, 700);
        try
        {
            var texts = Texts(page);
            texts.ShouldContain("Open the data folder");
            texts.ShouldContain("Privacy and data");
            texts.ShouldNotContain(text => text.Contains("Avalonia", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            window.Close();
        }
    }

    private static List<string> Texts(Control root) =>
        [.. root.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text ?? string.Empty)];

    private static Window Show(Control content, double width, double height)
    {
        var window = new Window { Width = width, Height = height, Content = content };
        window.Show();
        window.UpdateLayout();
        return window;
    }
}
