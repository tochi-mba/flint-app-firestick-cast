using Flint.Core;
using Shouldly;

namespace Flint.Cli.Tests;

/// <summary>What <c>flint --list-displays</c> prints.</summary>
public sealed class DisplayListingTests
{
    [Fact]
    public void EachDisplay_IsListedByTheNumberDisplayTakes_WithASidewaysOneSaidSo()
    {
        var output = new StringWriter();

        DisplayListing.Write(
            [
                new DisplayInfo(0, 1, "DELL U2720Q", "dell", 0, 0, 2560, 1440, DisplayRotation.Upright, IsMain: true),
                new DisplayInfo(1, 2, "Display 2", "side", 2560, 0, 1080, 1920, DisplayRotation.QuarterClockwise, IsMain: false),
            ],
            output);

        output.ToString().ReplaceLineEndings("\n").ShouldBe(
            """
              DISPLAYS
              --------
              1 · DELL U2720Q · 2560 × 1440 · main
              2 · Display 2 · 1080 × 1920 · rotated, so it will appear sideways on the TV

              Share one with --mirror --display <number>.


            """.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void NoDisplays_IsAnAnswer_ThatSaysWhatItMeans()
    {
        var output = new StringWriter();

        DisplayListing.Write([], output);

        output.ToString().ReplaceLineEndings("\n").ShouldBe(
            """
              DISPLAYS
              --------
              None could be listed, so this PC is unlikely to be able to share its screen.


            """.ReplaceLineEndings("\n"));
    }
}
