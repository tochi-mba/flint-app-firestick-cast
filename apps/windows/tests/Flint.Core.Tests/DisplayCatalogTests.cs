using Flint.Core.Settings;
using Shouldly;

namespace Flint.Core.Tests;

/// <summary>Naming and numbering the displays, and choosing which one a share captures.</summary>
public sealed class DisplayCatalogTests
{
    private static readonly DisplayOutput Main = new(0, @"\\.\DISPLAY1", 0, 0, 2560, 1440, DisplayRotation.Upright, IsMain: true);
    private static readonly DisplayOutput Side = new(2, @"\\.\DISPLAY3", 2560, 0, 1080, 1920, DisplayRotation.QuarterClockwise, IsMain: false);

    [Fact]
    public void Displays_AreNumberedFromOne_AndNamedByTheirMonitors()
    {
        var catalog = new DisplayCatalog(
            new Outputs(Main, Side),
            new Names(new()
            {
                [@"\\.\display1"] = new MonitorName("  DELL U2720Q ", @"\\?\DISPLAY#DEL41A8#1"),
            }));

        var displays = catalog.List();

        displays.Count.ShouldBe(2);
        displays[0].ShouldBe(new DisplayInfo(0, 1, "DELL U2720Q", @"\\?\DISPLAY#DEL41A8#1", 0, 0, 2560, 1440, DisplayRotation.Upright, IsMain: true));
        displays[1].Index.ShouldBe(2u, "the capture index is kept, whatever the number shown");
        displays[1].Number.ShouldBe(2);
        displays[1].Name.ShouldBe("Display 2", "a monitor Windows cannot name gets its number");
        displays[1].Identity.ShouldBe(@"\\.\DISPLAY3", "without a device path, the device name is the best identity there is");
    }

    [Fact]
    public void AMonitorWithABlankName_IsCalledByItsNumber_ButKeepsItsPath()
    {
        var catalog = new DisplayCatalog(new Outputs(Main), new Names(new() { [Main.DeviceName] = new MonitorName(" ", "path-1") }));

        var display = catalog.List().ShouldHaveSingleItem();

        display.Name.ShouldBe("Display 1");
        display.Identity.ShouldBe("path-1");
    }

    [Fact]
    public void NoDisplays_AreNoDisplays_WithoutAskingWindowsForNames()
    {
        var names = new Names([]);

        new DisplayCatalog(new Outputs(), names).List().ShouldBeEmpty();

        names.Reads.ShouldBe(0);
    }

    [Fact]
    public void ADisplay_DescribesItselfInOneLine()
    {
        var catalog = new DisplayCatalog(new Outputs(Main, Side), new Names(new() { [Main.DeviceName] = new MonitorName("DELL U2720Q", "p") }));
        var displays = catalog.List();

        displays[0].Describe().ShouldBe("1 · DELL U2720Q · 2560 × 1440 · main");
        displays[1].Describe().ShouldBe("2 · Display 2 · 1080 × 1920");
        displays[0].IsSideways.ShouldBeFalse();
        displays[1].IsSideways.ShouldBeTrue();
        (displays[1] with { Rotation = DisplayRotation.QuarterAnticlockwise }).IsSideways.ShouldBeTrue();
        (displays[1] with { Rotation = DisplayRotation.UpsideDown }).IsSideways.ShouldBeFalse();
    }

    [Fact]
    public void TheMainDisplay_IsChosen_UnlessTheRememberedOneIsWanted()
    {
        var displays = Listed();

        DisplayCatalog.Choose(displays, ShareDisplayChoice.Main, "side").ShouldBe(new DisplaySelection(displays[0], false));
        DisplayCatalog.Choose(displays, ShareDisplayChoice.Remembered, null).ShouldBe(new DisplaySelection(displays[0], false));
        DisplayCatalog.Choose(displays, ShareDisplayChoice.Remembered, " ").ShouldBe(new DisplaySelection(displays[0], false));
    }

    [Fact]
    public void TheRememberedDisplay_IsFoundByIdentity_WhereverItIsNow()
    {
        var displays = Listed();

        DisplayCatalog.Choose(displays, ShareDisplayChoice.Remembered, "SIDE").ShouldBe(new DisplaySelection(displays[1], false));
    }

    [Fact]
    public void AMissingRememberedDisplay_FallsBackToTheMainOne_AndSaysSo()
    {
        var displays = Listed();

        DisplayCatalog.Choose(displays, ShareDisplayChoice.Remembered, "unplugged").ShouldBe(new DisplaySelection(displays[0], true));
    }

    [Fact]
    public void WithoutAMainDisplay_TheFirstStandsIn_AndWithNoneThereIsNothing()
    {
        var displays = Listed().Select(display => display with { IsMain = false }).ToList();

        DisplayCatalog.Choose(displays, ShareDisplayChoice.Main, null).Display.ShouldBe(displays[0]);
        DisplayCatalog.Choose([], ShareDisplayChoice.Remembered, "side").ShouldBe(
            new DisplaySelection(null, false),
            "a list that could not be read says nothing about the usual display");
        DisplayCatalog.Choose([], ShareDisplayChoice.Main, null).ShouldBe(new DisplaySelection(null, false));
    }

    [Fact]
    public void TheCatalog_NeedsBothSources()
    {
        Should.Throw<ArgumentNullException>(() => new DisplayCatalog(null!, new Names([])));
        Should.Throw<ArgumentNullException>(() => new DisplayCatalog(new Outputs(), null!));
        Should.Throw<ArgumentNullException>(() => DisplayCatalog.Choose(null!, ShareDisplayChoice.Main, null));
    }

    private static List<DisplayInfo> Listed() =>
    [
        new(0, 1, "Main", "main", 0, 0, 1920, 1080, DisplayRotation.Upright, IsMain: true),
        new(1, 2, "Side", "side", 1920, 0, 1920, 1080, DisplayRotation.Upright, IsMain: false),
    ];

    private sealed class Outputs(params DisplayOutput[] outputs) : IDisplayOutputSource
    {
        public IReadOnlyList<DisplayOutput> ListOutputs() => outputs;
    }

    private sealed class Names(Dictionary<string, MonitorName> names) : IMonitorNames
    {
        public int Reads { get; private set; }

        public IReadOnlyDictionary<string, MonitorName> Read()
        {
            Reads++;
            return new Dictionary<string, MonitorName>(names, StringComparer.OrdinalIgnoreCase);
        }
    }
}
