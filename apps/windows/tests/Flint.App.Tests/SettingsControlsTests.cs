using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Flint.App.Controls;
using Flint.App.Tests.Snapshots;
using Flint.App.ViewModels;
using Flint.App.Views;
using Flint.Core.Settings;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// The REX input controls added for Settings, rendered through their real themes.
/// </summary>
public sealed class SettingsControlsTests
{
    private static readonly string[] Sizes = ["90%", "100%", "115%", "130%"];

    [AvaloniaTheory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void ToggleSwitch_DrawsOnlyWithPaletteColours(bool on, bool enabled)
    {
        var toggle = new ToggleSwitch { Theme = Theme("FlintToggleSwitch"), IsChecked = on, IsEnabled = enabled };

        ColoursOf(Show(toggle), toggle).ShouldBeSubsetOf(PaletteColours());
    }

    [AvaloniaFact]
    public void ToggleSwitch_ShowsFocusOutsideTheTrack_SoItReadsWhenOn()
    {
        // A focus border on the track itself is signal on signal when the switch is on.
        var toggle = new ToggleSwitch { Theme = Theme("FlintToggleSwitch"), IsChecked = true };
        var window = Show(toggle);
        var ring = Named<Border>(toggle, "FocusRing");
        Colour(ring.BorderBrush).ShouldBe(Colors.Transparent);

        toggle.Focus(NavigationMethod.Tab).ShouldBeTrue();
        window.UpdateLayout();

        Colour(ring.BorderBrush).ShouldBe(Palette("SignalColor"));
        Colour(Named<Border>(toggle, "Track").Background).ShouldBe(Palette("SignalColor"));
        window.Close();
    }

    [AvaloniaFact]
    public void ToggleSwitch_WhenDisabled_LosesItsColourRatherThanFading()
    {
        var toggle = new ToggleSwitch { Theme = Theme("FlintToggleSwitch"), IsChecked = true, IsEnabled = false };
        var window = Show(toggle);

        Colour(Named<Border>(toggle, "Track").Background).ShouldBe(Palette("PanelColor"));
        toggle.Opacity.ShouldBe(1);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Slider_DrawsOnlyWithPaletteColours(bool enabled)
    {
        var slider = new Slider { Theme = Theme("FlintSlider"), Value = 40, IsEnabled = enabled, Width = 240 };

        ColoursOf(Show(slider), slider).ShouldBeSubsetOf(PaletteColours());
    }

    [AvaloniaFact]
    public void Slider_ArrowKeysMoveItOneStepAtATime_AndItsFillFollows()
    {
        var slider = new Slider
        {
            Theme = Theme("FlintSlider"),
            Minimum = 0,
            Maximum = 100,
            SmallChange = 5,
            Value = 40,
            Width = 240,
        };
        var window = Show(slider);
        slider.Focus(NavigationMethod.Tab).ShouldBeTrue();
        var fill = Named<RepeatButton>(slider, "PART_DecreaseButton");
        var before = fill.Bounds.Width;

        Press(window, PhysicalKey.ArrowRight);

        slider.Value.ShouldBe(45);
        fill.Bounds.Width.ShouldBeGreaterThan(before);
        Press(window, PhysicalKey.ArrowLeft);
        Press(window, PhysicalKey.ArrowLeft);
        slider.Value.ShouldBe(35);
        Colour(Named<Thumb>(slider, "thumb").BorderBrush).ShouldBe(Palette("SignalColor"), "the thumb carries the focus ring");
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void ComboBox_DrawsOnlyWithPaletteColours(bool enabled)
    {
        var combo = new ComboBox { Theme = Theme("FlintComboBox"), ItemsSource = Sizes, SelectedIndex = 1, IsEnabled = enabled };

        var drawn = ColoursOf(Show(combo), combo);
        drawn.ShouldBeSubsetOf(PaletteColours(), string.Join(", ", drawn.Except(PaletteColours()).Select(colour => $"{colour} on {Where[colour]}")));
    }

    private static readonly Dictionary<Color, string> Where = [];

    [AvaloniaFact]
    public void ComboBox_ItsListUsesTheRexItemTheme()
    {
        var combo = new ComboBox { Theme = Theme("FlintComboBox"), ItemsSource = Sizes, SelectedIndex = 1 };
        var window = Show(combo);
        combo.IsDropDownOpen = true;
        window.UpdateLayout();

        var item = (ComboBoxItem)combo.ContainerFromIndex(1)!;
        item.Theme.ShouldBe(Theme("FlintComboBoxItem"));
        Colour(item.BorderBrush).ShouldBe(Palette("SignalColor"), "the chosen size is marked in signal");
        combo.IsDropDownOpen = false;
        window.Close();
    }

    [AvaloniaFact]
    public void SegmentedChoice_AndContentButton_DrawOnlyWithPaletteColours()
    {
        var choice = new SegmentedChoice { ItemsSource = new[] { "TV only", "TV and this PC" }, SelectedIndex = 0 };
        var button = new Button { Theme = Theme("ContentOutlineButton"), Content = new TextBlock { Text = "Result" } };
        var panel = new StackPanel { Children = { choice, button } };

        ColoursOf(Show(panel), panel).ShouldBeSubsetOf(PaletteColours());
    }

    [AvaloniaFact]
    public void SettingsPage_TabReachesEveryControlInReadingOrder()
    {
        var page = new SettingsPage { DataContext = SnapshotFixtures.Settings() };
        var window = new Window { Width = 1024, Height = 700, Content = page };
        window.Show();
        window.UpdateLayout();
        var visited = new List<string>();

        for (var press = 0; press < 13; press++)
        {
            Press(window, PhysicalKey.Tab);
            if (window.FocusManager!.GetFocusedElement() is Control focused)
            {
                visited.Add(Describe(focused));
            }
        }

        // The search, then the section list, then the open section top to bottom, then round again.
        visited.Take(11).ShouldBe(
        [
            "TextBox Search settings",
            "ListBoxItem General",
            "ToggleSwitch Ask before switching what the TV shows",
            "ToggleSwitch Keep this PC awake while sharing or playing",
            "ComboBox Interface size",
            "ToggleSwitch Reconnect to the last TV when Flint starts",
            "ToggleSwitch Keep trying if the connection drops",
            "ComboBox How long to keep trying",
            "ComboBox After reconnecting",
            "ToggleSwitch Open Flint on the TV when a new code is needed",
            "Button RESET GENERAL SETTINGS",
        ]);
        window.Close();
    }

    [AvaloniaFact]
    public void InterfaceSize_ScalesEverythingInTheWindow()
    {
        var store = new InMemoryAppSettingsStore(new AppSettings { General = new GeneralSettings { InterfaceScalePercent = 130 } });
        using var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()), settingsStore: store);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        window.UpdateLayout();
        try
        {
            // The brand mark in the rail is forty pixels square at Flint's own size.
            var mark = window.GetVisualDescendants().OfType<Border>().First(border => border.Width == 40 && border.Height == 40);
            var left = mark.TranslatePoint(new Point(0, 0), window)!.Value;
            var right = mark.TranslatePoint(new Point(40, 0), window)!.Value;

            (right.X - left.X).ShouldBe(52, 0.01);
            window.MinWidth.ShouldBe(1170);
            window.MinHeight.ShouldBe(806);
        }
        finally
        {
            window.Close();
        }
    }

    private static string Describe(Control control) =>
        $"{control.GetType().Name} {Avalonia.Automation.AutomationProperties.GetName(control) ?? (control as ContentControl)?.Content switch
        {
            Flint.App.ViewModels.Settings.SettingsSectionViewModel section => section.Title,
            var other => other?.ToString(),
        }}";

    private static ControlTheme Theme(string key) =>
        (ControlTheme)Application.Current!.FindResource(key)!;

    private static Color Palette(string token) => (Color)Application.Current!.FindResource(token)!;

    /// <summary>Every colour the palette offers, the washes as they are drawn, and nothing at all.</summary>
    private static HashSet<Color> PaletteColours()
    {
        var colours = new HashSet<Color> { Colors.Transparent };
        foreach (var token in new[] { "InkColor", "PanelColor", "RaisedColor", "LineColor", "TextColor", "MutedColor", "SignalColor", "LiveColor" })
        {
            colours.Add(Palette(token));
        }

        return colours;
    }

    private static Window Show(Control content)
    {
        var window = new Window { Width = 400, Height = 200, Content = content };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static void Press(Window window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.None);
        window.KeyReleaseQwerty(key, RawInputModifiers.None);
        window.UpdateLayout();
    }

    private static T Named<T>(Visual root, string name)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(control => control.Name == name);

    private static Color Colour(IBrush? brush) => brush switch
    {
        ISolidColorBrush solid => solid.Color,
        null => Colors.Transparent,
        _ => throw new ShouldAssertException($"{brush.GetType().Name} is not a palette brush"),
    };

    /// <summary>The colours actually drawn under <paramref name="root"/>, washes ignored.</summary>
    private static HashSet<Color> ColoursOf(Window window, Visual root)
    {
        var drawn = new HashSet<Color>();
        foreach (var visual in root.GetVisualDescendants().Prepend(root).Where(visual => visual.IsEffectivelyVisible))
        {
            var brushes = visual switch
            {
                Border border => new[] { border.Background, border.BorderBrush },
                Shape shape => [shape.Fill, shape.Stroke],
                TextBlock text => [text.Foreground],
                PathIcon icon => [icon.Foreground],
                _ => [],
            };
            foreach (var brush in brushes.OfType<ISolidColorBrush>().Where(brush => brush.Opacity >= 1))
            {
                drawn.Add(brush.Color);
                Where[brush.Color] = $"{visual.GetType().Name}#{(visual as Control)?.Name}";
            }
        }

        window.Close();
        return drawn;
    }
}
