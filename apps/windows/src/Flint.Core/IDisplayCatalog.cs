namespace Flint.Core;

/// <summary>How a display is turned.</summary>
/// <remarks>Values match the engine's <c>FlintOutput::rotation</c> and DXGI's own.</remarks>
public enum DisplayRotation
{
    /// <summary>Upright.</summary>
    Upright = 1,

    /// <summary>Turned a quarter clockwise.</summary>
    QuarterClockwise = 2,

    /// <summary>Upside down.</summary>
    UpsideDown = 3,

    /// <summary>Turned a quarter anticlockwise.</summary>
    QuarterAnticlockwise = 4,
}

/// <summary>A display as capture sees it, before it is given a name.</summary>
/// <param name="Index">The index capture opens it by.</param>
/// <param name="DeviceName">Windows' device name for it, such as <c>\\.\DISPLAY1</c>.</param>
/// <param name="Left">Its left edge on the Windows desktop.</param>
/// <param name="Top">Its top edge on the Windows desktop.</param>
/// <param name="Width">Its width on the desktop, in pixels.</param>
/// <param name="Height">Its height on the desktop, in pixels.</param>
/// <param name="Rotation">How it is turned.</param>
/// <param name="IsMain">Whether it is the display Windows calls the main one.</param>
public sealed record DisplayOutput(
    uint Index,
    string DeviceName,
    int Left,
    int Top,
    int Width,
    int Height,
    DisplayRotation Rotation,
    bool IsMain);

/// <summary>A monitor's own name and its lasting identity, as Windows knows them.</summary>
/// <param name="FriendlyName">The name the monitor reports, such as "DELL U2720Q"; may be empty.</param>
/// <param name="DevicePath">
/// The monitor's device path. Unlike the device name, it stays the same when displays are
/// unplugged and plugged back in, so it is what a chosen display is remembered by.
/// </param>
public sealed record MonitorName(string FriendlyName, string DevicePath);

/// <summary>The displays capture can open.</summary>
public interface IDisplayOutputSource
{
    /// <summary>Lists them in capture's order, or an empty list when they cannot be listed.</summary>
    IReadOnlyList<DisplayOutput> ListOutputs();
}

/// <summary>Monitor names, by the device name capture reports.</summary>
public interface IMonitorNames
{
    /// <summary>Reads every active display's monitor name, keyed by device name.</summary>
    /// <returns>An empty map when Windows cannot say.</returns>
    IReadOnlyDictionary<string, MonitorName> Read();
}

/// <summary>A display the person can choose to share.</summary>
/// <param name="Index">The index capture opens it by.</param>
/// <param name="Number">Its number as shown to the person, from one.</param>
/// <param name="Name">The monitor's name, or "Display 2" when it has none.</param>
/// <param name="Identity">What the choice is remembered by.</param>
/// <param name="Left">Its left edge on the Windows desktop.</param>
/// <param name="Top">Its top edge on the Windows desktop.</param>
/// <param name="Width">Its width on the desktop, in pixels.</param>
/// <param name="Height">Its height on the desktop, in pixels.</param>
/// <param name="Rotation">How it is turned.</param>
/// <param name="IsMain">Whether it is the display Windows calls the main one.</param>
public sealed record DisplayInfo(
    uint Index,
    int Number,
    string Name,
    string Identity,
    int Left,
    int Top,
    int Width,
    int Height,
    DisplayRotation Rotation,
    bool IsMain)
{
    /// <summary>Whether the display is turned on its side, so it will appear sideways on the TV.</summary>
    public bool IsSideways => Rotation is DisplayRotation.QuarterClockwise or DisplayRotation.QuarterAnticlockwise;

    /// <summary>One line naming the display, such as "1 · DELL U2720Q · 2560 × 1440 · main".</summary>
    public string Describe() =>
        $"{Number} · {Name} · {Width} × {Height}{(IsMain ? " · main" : string.Empty)}";
}

/// <summary>The displays this PC can share, named and numbered.</summary>
public interface IDisplayCatalog
{
    /// <summary>Lists the displays, in capture's order.</summary>
    IReadOnlyList<DisplayInfo> List();
}
