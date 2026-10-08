using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Flint.App.Controls;
using Flint.Core;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>The map of displays: drawn as Windows arranges them, picked by click or by arrow keys.</summary>
public sealed class DisplayArrangementPickerTests
{
    // Laid out as:   [Above]
    //        [Tall][Main][Side]
    private static readonly DisplayInfo Tall = new(2, 3, "Tall", "tall", -1080, 0, 1080, 1920, DisplayRotation.QuarterClockwise, IsMain: false);
    private static readonly DisplayInfo Main = new(0, 1, "Main", "main", 0, 0, 1920, 1080, DisplayRotation.Upright, IsMain: true);
    private static readonly DisplayInfo Side = new(1, 2, "Side", "side", 1920, 0, 2560, 1440, DisplayRotation.Upright, IsMain: false);
    private static readonly DisplayInfo Above = new(3, 4, "Above", "above", 0, -1080, 1920, 1080, DisplayRotation.Upright, IsMain: false);
    private static readonly DisplayInfo[] All = [Main, Side, Tall, Above];

    [Theory]
    [InlineData(Key.Right, "side")]
    [InlineData(Key.Left, "tall")]
    [InlineData(Key.Up, "above")]
    public void TheArrowKeys_MoveToTheNearestDisplayThatWay(Key key, string expected)
    {
        DisplayArrangementPicker.Nearest(All, Main, key).Identity.ShouldBe(expected);
    }

    [Fact]
    public void WithNothingThatWay_TheChoiceStays()
    {
        DisplayArrangementPicker.Nearest(All, Main, Key.Down).ShouldBe(Main);
        DisplayArrangementPicker.Nearest(All, Side, Key.Right).ShouldBe(Side);
        DisplayArrangementPicker.Nearest(All, Main, Key.Enter).ShouldBe(Main, "only arrows move");
    }

    [Fact]
    public void ADisplayStraightAhead_BeatsOneThatIsCloserButToTheSide()
    {
        var diagonal = new DisplayInfo(5, 5, "Diagonal", "diagonal", 1920, -900, 400, 400, DisplayRotation.Upright, IsMain: false);
        var far = new DisplayInfo(6, 6, "Far", "far", 2800, 0, 1920, 1080, DisplayRotation.Upright, IsMain: false);

        DisplayArrangementPicker.Nearest([Main, diagonal, far], Main, Key.Right).ShouldBe(far);
        Should.Throw<ArgumentNullException>(() => DisplayArrangementPicker.Nearest(null!, Main, Key.Up));
        Should.Throw<ArgumentNullException>(() => DisplayArrangementPicker.Nearest(All, null!, Key.Up));
    }

    [AvaloniaFact]
    public void EachDisplay_IsATile_InWindowsOwnArrangement_NamedWithItsSize()
    {
        var (picker, window) = Render(All, Main);
        try
        {
            var tiles = Tiles(picker);

            tiles.Count.ShouldBe(4);
            AutomationProperties.GetName(Tile(picker, Side)).ShouldBe("2 · Side · 2560 × 1440");
            Tile(picker, Tall).Bounds.Right.ShouldBeLessThanOrEqualTo(Tile(picker, Main).Bounds.Left);
            Tile(picker, Main).Bounds.Right.ShouldBeLessThanOrEqualTo(Tile(picker, Side).Bounds.Left);
            Tile(picker, Above).Bounds.Bottom.ShouldBeLessThanOrEqualTo(Tile(picker, Main).Bounds.Top);
            Tile(picker, Side).Bounds.Height.ShouldBeGreaterThan(Tile(picker, Main).Bounds.Height, "drawn to scale");
            picker.DesiredSize.Height.ShouldBeLessThanOrEqualTo(DisplayArrangementPicker.MapHeight + 1);
            Tile(picker, Main).Classes.ShouldContain("selected");
            Tile(picker, Side).Classes.ShouldNotContain("selected");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ClickingATile_ChoosesIt()
    {
        var (picker, window) = Render(All, Main);
        try
        {
            Tile(picker, Side).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            picker.SelectedDisplay.ShouldBe(Side);
            Tile(picker, Side).Classes.ShouldContain("selected");
            Tile(picker, Main).Classes.ShouldNotContain("selected");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AnArrowKey_ChoosesTheDisplayThatWay_AndMovesFocusToIt()
    {
        var (picker, window) = Render(All, Main);
        try
        {
            var key = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Left };
            Tile(picker, Main).RaiseEvent(key);

            key.Handled.ShouldBeTrue();
            picker.SelectedDisplay.ShouldBe(Tall);
            Tile(picker, Tall).IsFocused.ShouldBeTrue();

            var other = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A };
            Tile(picker, Tall).RaiseEvent(other);
            other.Handled.ShouldBeFalse("letters are left for the rest of the window");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ANewList_RedrawsTheMap()
    {
        var (picker, window) = Render(All, Main);
        try
        {
            picker.Displays = [Main];
            window.UpdateLayout();

            Tiles(picker).ShouldHaveSingleItem();
            picker.Displays = null;
            window.UpdateLayout();
            Tiles(picker).ShouldBeEmpty();
            picker.DesiredSize.Height.ShouldBe(0);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TheMap_IsTheSizeOfItsDisplays_WithinItsLimits()
    {
        var picker = new DisplayArrangementPicker { Displays = [Main, Side] };

        picker.Measure(new Avalonia.Size(double.PositiveInfinity, double.PositiveInfinity));
        picker.DesiredSize.Height.ShouldBe(DisplayArrangementPicker.MapHeight, 1, "two wide displays fill the height first");
        picker.DesiredSize.Width.ShouldBe(4480d / 1440 * DisplayArrangementPicker.MapHeight, 1, "drawn whole pixels");

        picker.Measure(new Avalonia.Size(200, double.PositiveInfinity));
        picker.DesiredSize.Width.ShouldBe(200, 1, "a narrow window narrows the map");
    }

    private static (DisplayArrangementPicker Picker, Window Window) Render(IReadOnlyList<DisplayInfo> displays, DisplayInfo selected)
    {
        var picker = new DisplayArrangementPicker { Displays = displays, SelectedDisplay = selected };
        var window = new Window { Width = 800, Height = 400, Content = picker };
        window.Show();
        window.UpdateLayout();
        return (picker, window);
    }

    private static List<Button> Tiles(DisplayArrangementPicker picker) => [.. picker.Children.OfType<Button>()];

    private static Button Tile(DisplayArrangementPicker picker, DisplayInfo display) =>
        Tiles(picker).Single(tile => Equals(tile.Tag, display));
}
