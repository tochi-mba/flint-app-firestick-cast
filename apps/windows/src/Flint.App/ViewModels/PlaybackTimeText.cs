using System.Globalization;

namespace Flint.App.ViewModels;

/// <summary>Times as the Now Playing card shows and speaks them.</summary>
internal static class PlaybackTimeText
{
    /// <summary>A time as the bar shows it: m:ss, or h:mm:ss from an hour.</summary>
    internal static string Format(long milliseconds)
    {
        var span = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return span.TotalHours >= 1
            ? span.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : span.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>A time as a screen reader says it: "1 hour 32 minutes", "12 minutes 40 seconds".</summary>
    internal static string Spoken(long milliseconds)
    {
        var span = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        var parts = new List<string>(3);
        AddPart(parts, (int)span.TotalHours, "hour");
        AddPart(parts, span.Minutes, "minute");
        AddPart(parts, span.Seconds, "second");
        return parts.Count == 0 ? "0 seconds" : string.Join(' ', parts);
    }

    private static void AddPart(List<string> parts, int count, string unit)
    {
        if (count > 0)
        {
            parts.Add(count == 1 ? $"1 {unit}" : $"{count} {unit}s");
        }
    }
}
