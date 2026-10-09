using Flint.Core;

namespace Flint.Cli;

/// <summary>Writes this PC's displays, numbered as <c>--display</c> takes them.</summary>
internal static class DisplayListing
{
    /// <summary>Writes <paramref name="displays"/> to <paramref name="output"/>, one to a line.</summary>
    internal static void Write(IReadOnlyList<DisplayInfo> displays, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(displays);
        ArgumentNullException.ThrowIfNull(output);

        output.WriteLine("  DISPLAYS");
        output.WriteLine("  --------");
        if (displays.Count == 0)
        {
            // An answer rather than a failure, as a probe that finds nothing is. Saying what it
            // means is more use than an empty heading.
            output.WriteLine("  None could be listed, so this PC is unlikely to be able to share its screen.");
            output.WriteLine();
            return;
        }

        foreach (var display in displays)
        {
            var sideways = display.IsSideways ? " · rotated, so it will appear sideways on the TV" : string.Empty;
            output.WriteLine($"  {display.Describe()}{sideways}");
        }

        output.WriteLine();
        output.WriteLine("  Share one with --mirror --display <number>.");
        output.WriteLine();
    }
}
