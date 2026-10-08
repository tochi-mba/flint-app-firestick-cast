using Flint.Core;

namespace Flint.App.ViewModels;

/// <summary>What the Screen page says about shared sound.</summary>
public static class SoundText
{
    /// <summary>The quietest level the meter shows, in decibels below full scale.</summary>
    internal const double MeterFloorDecibels = 60;

    /// <summary>Said under the Sound row, because Windows hides protected video's sound from every capture.</summary>
    public const string ProtectedContent =
        "Some apps play protected video, such as films from streaming services, as silence and often as a black picture. That is Windows protecting it, not a fault.";

    /// <summary>The Sound row's status line.</summary>
    /// <param name="shareSound">Whether sound is switched on.</param>
    /// <param name="sharing">Whether the screen is being shared.</param>
    /// <param name="paused">Whether the share is paused.</param>
    /// <param name="state">What shared sound is doing.</param>
    /// <param name="problem">Why sound is unavailable, when it is.</param>
    public static string Status(bool shareSound, bool sharing, bool paused, AudioShareState state, string? problem) =>
        !shareSound ? "Sound is off."
        : !sharing ? "This PC's sound plays on the TV with the picture."
        : state is AudioShareState.Unavailable ? $"Sound is not available. {problem ?? "Windows would not share this PC's sound."}"
        : paused ? "Sound is paused with the picture."
        : state is AudioShareState.Sounding ? "Sending sound to the TV."
        : "Ready. Nothing is playing on this PC yet.";

    /// <summary>A captured level, from 0 to 1, as the meter shows it: on a decibel scale, so speech moves it.</summary>
    public static double Meter(float level) =>
        level > 0 ? Math.Clamp((20 * Math.Log10(level) + MeterFloorDecibels) / MeterFloorDecibels, 0, 1) : 0;
}
