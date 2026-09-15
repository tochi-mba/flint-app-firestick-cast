using Shouldly;

namespace Flint.App.Tests.Snapshots;

/// <summary>
/// The comparison rules every screenshot test depends on.
/// </summary>
/// <remarks>
/// Tested directly rather than only through rendered pages. A screenshot test that passes because
/// its comparer is too permissive looks exactly like one that passes because the UI is correct, and
/// the whole suite is worthless the moment that is true. These fix the tolerances against concrete
/// values from the REX palette instead.
/// </remarks>
public sealed class ImageComparerTests
{
    [Fact]
    public void Compare_IdenticalImages_ReportsNoDifference()
    {
        // Arrange
        var image = Fill(4, 4, 0x11, 0x15, 0x12);

        // Act
        var comparison = ImageComparer.Compare(image, image, 4, 4);

        // Assert
        comparison.DifferingPixels.ShouldBe(0);
        comparison.WorstChannelDifference.ShouldBe(0);
        comparison.DifferingPercent.ShouldBe(0d);
        comparison.IsWithin(0d).ShouldBeTrue();
    }

    [Fact]
    public void Compare_DifferenceInsideTheChannelTolerance_IsNotCounted()
    {
        // Antialiasing noise between driver versions looks exactly like this, and failing on it
        // would mean regenerating every approved image on whichever machine ran the tests.

        // Arrange
        var expected = Fill(2, 2, 0x40, 0x40, 0x40);
        var actual = Fill(2, 2, 0x40 + ImageComparer.DefaultChannelTolerance, 0x40, 0x40);

        // Act
        var comparison = ImageComparer.Compare(expected, actual, 2, 2);

        // Assert
        comparison.DifferingPixels.ShouldBe(0);
        comparison.WorstChannelDifference.ShouldBe(ImageComparer.DefaultChannelTolerance);
    }

    [Fact]
    public void Compare_DifferenceOneStepPastTheTolerance_IsCounted()
    {
        // The boundary itself, because an off-by-one here silently widens every threshold.

        // Arrange
        var expected = Fill(2, 2, 0x40, 0x40, 0x40);
        var actual = Fill(2, 2, 0x40 + ImageComparer.DefaultChannelTolerance + 1, 0x40, 0x40);

        // Act
        var comparison = ImageComparer.Compare(expected, actual, 2, 2);

        // Assert
        comparison.DifferingPixels.ShouldBe(4);
        comparison.DifferingPercent.ShouldBe(100d);
    }

    [Fact]
    public void Compare_TheTwoClosestRexTokens_AreStillTreatedAsDifferent()
    {
        // The tolerance is only safe if it is narrower than the smallest deliberate difference in
        // the palette. Panel (#111512) and Raised (#181E19) are the closest pair; a tolerance wide
        // enough to merge them would let a card render on the wrong surface colour and pass.

        // Arrange
        var panel = Fill(2, 2, 0x11, 0x15, 0x12);
        var raised = Fill(2, 2, 0x18, 0x1E, 0x19);

        // Act
        var comparison = ImageComparer.Compare(panel, raised, 2, 2);

        // Assert
        comparison.DifferingPixels.ShouldBe(4);
    }

    [Fact]
    public void Compare_AnAlphaOnlyChange_IsDetected()
    {
        // A control at the right colour and the wrong opacity is a real regression. Comparing only
        // the visible channels would hide a disabled state that renders as though it were enabled.

        // Arrange
        var opaque = Fill(2, 2, 0x40, 0x40, 0x40, alpha: 0xFF);
        var faded = Fill(2, 2, 0x40, 0x40, 0x40, alpha: 0x80);

        // Act
        var comparison = ImageComparer.Compare(opaque, faded, 2, 2);

        // Assert
        comparison.DifferingPixels.ShouldBe(4);
        comparison.WorstChannelDifference.ShouldBe(0x7F);
    }

    [Fact]
    public void Compare_CountsOnlyThePixelsThatActuallyChanged()
    {
        // Arrange: one pixel of four moved well past the tolerance.
        var expected = Fill(2, 2, 0x00, 0x00, 0x00);
        var actual = Fill(2, 2, 0x00, 0x00, 0x00);
        actual[0] = 0xFF;

        // Act
        var comparison = ImageComparer.Compare(expected, actual, 2, 2);

        // Assert
        comparison.DifferingPixels.ShouldBe(1);
        comparison.DifferingPercent.ShouldBe(25d);
        comparison.TotalPixels.ShouldBe(4);
    }

    [Fact]
    public void IsWithin_TheDefaultBudget_RejectsAVisibleElementMoving()
    {
        // A thousand changed pixels on a 1280x800 page is roughly a small button moving. The budget
        // has to fail that while tolerating scattered antialiasing.

        // Arrange
        var comparison = new ImageComparison(1280, 800, DifferingPixels: 1_100, 255);

        // Act
        var within = comparison.IsWithin(ImageComparer.DefaultMaximumDifferingPercent);

        // Assert
        within.ShouldBeFalse();
        comparison.DifferingPercent.ShouldBeGreaterThan(
            ImageComparer.DefaultMaximumDifferingPercent);
    }

    [Fact]
    public void IsWithin_TheDefaultBudget_ToleratesScatteredAntialiasing()
    {
        // Arrange: a hundred stray pixels on the same page.
        var comparison = new ImageComparison(1280, 800, DifferingPixels: 100, 20);

        // Act & Assert
        comparison.IsWithin(ImageComparer.DefaultMaximumDifferingPercent).ShouldBeTrue();
    }

    [Fact]
    public void Compare_MismatchedBufferLength_ThrowsRatherThanReportingADifference()
    {
        // A caller that renders at the wrong size has a bug, and reporting it as "the image
        // changed" would send whoever reads the failure looking in entirely the wrong place.

        // Arrange
        var expected = Fill(2, 2, 0, 0, 0);
        var actual = Fill(2, 1, 0, 0, 0);

        // Act & Assert
        Should.Throw<ArgumentException>(() => ImageComparer.Compare(expected, actual, 2, 2));
    }

    [Fact]
    public void Compare_ZeroSizedImages_ReportsNoDifferenceRatherThanDividingByZero()
    {
        // Arrange & Act
        var comparison = ImageComparer.Compare([], [], 0, 0);

        // Assert
        comparison.DifferingRatio.ShouldBe(0d);
        comparison.DifferingPercent.ShouldBe(0d);
    }

    [Fact]
    public void Compare_NegativeDimensions_AreRejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => ImageComparer.Compare([], [], -1, 1));
    }

    [Fact]
    public void ToString_NamesTheCountTheShareAndTheWorstChannel()
    {
        // The failure message is the whole diagnostic when a build fails on a machine nobody is
        // sitting at, so it has to carry every number needed to judge the diff.

        // Arrange
        var comparison = new ImageComparison(100, 100, DifferingPixels: 250, 64);

        // Act
        var text = comparison.ToString();

        // Assert
        text.ShouldContain("250");
        text.ShouldContain("10000");
        text.ShouldContain("2.500");
        text.ShouldContain("64");
    }

    [Fact]
    public void BuildDiff_PaintsChangedPixelsInTheLiveAccent()
    {
        // Arrange: one changed pixel of four.
        var expected = Fill(2, 2, 0x00, 0x00, 0x00);
        var actual = Fill(2, 2, 0x00, 0x00, 0x00);
        actual[0] = 0xFF;

        // Act
        var diff = ImageComparer.BuildDiff(expected, actual, 2, 2);

        // Assert: BGRA, so #FF774D is 4D 77 FF.
        diff[0].ShouldBe((byte)0x4D);
        diff[1].ShouldBe((byte)0x77);
        diff[2].ShouldBe((byte)0xFF);
        diff[3].ShouldBe((byte)0xFF);
    }

    [Fact]
    public void BuildDiff_KeepsUnchangedPixelsAsDimGreySoTheLayoutStaysReadable()
    {
        // Arrange
        var image = Fill(2, 2, 0x80, 0x80, 0x80);

        // Act
        var diff = ImageComparer.BuildDiff(image, image, 2, 2);

        // Assert: a quarter of the source luminance, equal across the visible channels.
        diff[0].ShouldBe((byte)0x20);
        diff[1].ShouldBe((byte)0x20);
        diff[2].ShouldBe((byte)0x20);
        diff[3].ShouldBe((byte)0xFF);
    }

    [Fact]
    public void BuildDiff_IsTheSameSizeAsTheImagesItCompares()
    {
        // Arrange
        var expected = Fill(3, 5, 0, 0, 0);
        var actual = Fill(3, 5, 0, 0, 0);

        // Act
        var diff = ImageComparer.BuildDiff(expected, actual, 3, 5);

        // Assert
        diff.Length.ShouldBe(3 * 5 * 4);
    }

    /// <summary>Builds a solid BGRA buffer.</summary>
    private static byte[] Fill(int width, int height, int blue, int green, int red, int alpha = 255)
    {
        var pixels = new byte[width * height * 4];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = (byte)blue;
            pixels[offset + 1] = (byte)green;
            pixels[offset + 2] = (byte)red;
            pixels[offset + 3] = (byte)alpha;
        }

        return pixels;
    }
}
