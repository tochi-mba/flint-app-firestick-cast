namespace Flint.Core;

/// <summary>The bounded outcome of the receiver's local, developer-initiated WebView smoke probe.</summary>
public enum BrowserWebViewProbe
{
    /// <summary>No probe result has been recorded for this receiver.</summary>
    NotRun = 0,

    /// <summary>A single probe is in progress; it is not a usable browser state.</summary>
    Running = 1,

    /// <summary>The probe passed the owned fixture and returned a bounded result.</summary>
    Passed = 2,

    /// <summary>The receiver's WebView cannot meet the controlled compatibility requirement.</summary>
    Unsupported = 3,

    /// <summary>The result was incomplete, stale, or otherwise insufficient to make a claim.</summary>
    Inconclusive = 4,
}
