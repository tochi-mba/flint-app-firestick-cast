using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Shouldly;

namespace Flint.App.Tests.Snapshots;

/// <summary>
/// The harness itself, proved before anything relies on it.
/// </summary>
/// <remarks>
/// A screenshot suite whose renderer quietly produces blank frames passes every test it has,
/// forever, and reports nothing wrong. That failure mode is silent, plausible and total, so the
/// harness is held to the same standard as the code it tests: these check that it captures real
/// pixels, that it notices a change, and that it fails rather than approving on a first run.
/// </remarks>
public sealed class SnapshotHarnessTests
{
    [AvaloniaFact]
    public void Render_CapturesRealPixelsRatherThanABlankFrame()
    {
        // The check that keeps the whole suite honest. If headless rendering is misconfigured every
        // capture is a uniform empty frame, every comparison passes, and no test ever fails again.

        // Arrange
        var content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xD7, 0xFF, 0x3F)),
            Width = 100,
            Height = 100,
        };

        // Act
        var pixels = Capture(content, new PixelSize(200, 200));

        // Assert: the Signal-coloured square is present, so something actually drew.
        var distinctColours = DistinctColours(pixels);
        distinctColours.ShouldBeGreaterThan(1, "a blank frame means rendering is switched off");
    }

    [AvaloniaFact]
    public void Render_ProducesTheSameFrameTwiceForTheSameContent()
    {
        // Determinism is the precondition for the whole approach. A renderer that varies run to run
        // makes every approved image a coin toss, and the usual response to that is to widen the
        // threshold until nothing can fail.

        // Arrange
        static Control Build() => new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x11, 0x15, 0x12)),
            Child = new TextBlock { Text = "REX TECHNOLOGIES", FontSize = 14 },
        };

        // Act
        var first = Capture(Build(), new PixelSize(320, 120));
        var second = Capture(Build(), new PixelSize(320, 120));

        // Assert
        var comparison = ImageComparer.Compare(first, second, 320, 120);
        comparison.DifferingPixels.ShouldBe(0);
    }

    [AvaloniaFact]
    public void Render_NoticesAChangeOfColour()
    {
        // The other half of determinism: a renderer that produces identical frames for *different*
        // content would also pass the test above.

        // Arrange
        static Control Build(byte red) => new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(red, 0x20, 0x20)),
        };

        // Act
        var first = Capture(Build(0x10), new PixelSize(64, 64));
        var second = Capture(Build(0xF0), new PixelSize(64, 64));

        // Assert
        var comparison = ImageComparer.Compare(first, second, 64, 64);
        comparison.DifferingPixels.ShouldBe(64 * 64);
    }

    [AvaloniaFact]
    public void Render_NoticesAnElementMoving()
    {
        // The regression screenshot tests exist for: nothing about the element changes except where
        // it is, which every property assertion in the suite would happily pass.

        // Arrange
        static Control Build(HorizontalAlignment alignment) => new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x08, 0x0A, 0x09)),
            Child = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xD7, 0xFF, 0x3F)),
                Width = 40,
                Height = 40,
                HorizontalAlignment = alignment,
            },
        };

        // Act
        var left = Capture(Build(HorizontalAlignment.Left), new PixelSize(200, 80));
        var right = Capture(Build(HorizontalAlignment.Right), new PixelSize(200, 80));

        // Assert
        var comparison = ImageComparer.Compare(left, right, 200, 80);
        comparison.IsWithin(ImageComparer.DefaultMaximumDifferingPercent).ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Matches_WithNoApprovedImage_FailsAndLeavesTheRenderedFrameToLookAt()
    {
        // A suite that adopts whatever it first renders cannot fail on a first run, which is when a
        // new page is most likely to be wrong.

        // Arrange
        var name = $"harness-unapproved-{Guid.NewGuid():N}";
        var content = new Border { Background = Brushes.Black };

        // Act
        var failure = Should.Throw<SnapshotException>(
            () => Snapshot.Matches(name, content, new PixelSize(64, 64)));

        // Assert
        failure.Message.ShouldContain("no approved image");
        failure.Message.ShouldContain("FLINT_UPDATE_SNAPSHOTS");
        File.Exists(SnapshotFiles.RejectedPath(name)).ShouldBeTrue();

        // Cleanup: this name exists only for this test.
        File.Delete(SnapshotFiles.RejectedPath(name));
    }

    [AvaloniaFact]
    public void Matches_WhenTheRenderedFrameDiffers_WritesBothTheFrameAndADiff()
    {
        // The failure has to be inspectable. A message saying "0.9% of pixels differ" is useless on
        // its own; the two images and the highlight are what let someone judge it in seconds.

        // Arrange: approve one colour, then render another.
        var name = $"harness-diff-{Guid.NewGuid():N}";
        var size = new PixelSize(64, 64);
        var approved = Capture(new Border { Background = Brushes.Black }, size);
        SnapshotFiles.WritePng(SnapshotFiles.ApprovedPath(name), approved, size.Width, size.Height);

        try
        {
            // Act
            var failure = Should.Throw<SnapshotException>(
                () => Snapshot.Matches(name, new Border { Background = Brushes.White }, size));

            // Assert
            failure.Message.ShouldContain("does not match");
            File.Exists(SnapshotFiles.RejectedPath(name)).ShouldBeTrue();
            File.Exists(SnapshotFiles.DiffPath(name)).ShouldBeTrue();
        }
        finally
        {
            File.Delete(SnapshotFiles.ApprovedPath(name));
            File.Delete(SnapshotFiles.RejectedPath(name));
            File.Delete(SnapshotFiles.DiffPath(name));
        }
    }

    [AvaloniaFact]
    public void Matches_WhenTheRenderedFrameIsUnchanged_Passes()
    {
        // Arrange
        var name = $"harness-match-{Guid.NewGuid():N}";
        var size = new PixelSize(64, 64);
        static Control Build() => new Border { Background = Brushes.DarkSlateGray };
        SnapshotFiles.WritePng(
            SnapshotFiles.ApprovedPath(name),
            Capture(Build(), size),
            size.Width,
            size.Height);

        try
        {
            // Act & Assert
            Should.NotThrow(() => Snapshot.Matches(name, Build(), size));
        }
        finally
        {
            File.Delete(SnapshotFiles.ApprovedPath(name));
        }
    }

    [AvaloniaFact]
    public void Matches_WhenTheRenderedSizeChanges_SaysSoRatherThanReportingPixelDifferences()
    {
        // A size mismatch and a colour change are different problems with different causes, and
        // reporting the first as the second sends whoever reads it looking in the wrong place.

        // Arrange
        var name = $"harness-size-{Guid.NewGuid():N}";
        SnapshotFiles.WritePng(
            SnapshotFiles.ApprovedPath(name),
            Capture(new Border { Background = Brushes.Black }, new PixelSize(64, 64)),
            64,
            64);

        try
        {
            // Act
            var failure = Should.Throw<SnapshotException>(() => Snapshot.Matches(
                name,
                new Border { Background = Brushes.Black },
                new PixelSize(128, 64)));

            // Assert
            failure.Message.ShouldContain("128x64");
            failure.Message.ShouldContain("64x64");
        }
        finally
        {
            File.Delete(SnapshotFiles.ApprovedPath(name));
        }
    }

    [AvaloniaFact]
    public void WritePngAndReadPng_RoundTripThePixelsExactly()
    {
        // Every comparison goes through PNG on the approved side, so a lossy round trip would show
        // up as a permanent, unfixable difference on every snapshot in the suite.

        // Arrange
        var name = $"harness-roundtrip-{Guid.NewGuid():N}";
        var path = SnapshotFiles.ApprovedPath(name);
        var original = Capture(
            new Border { Background = new SolidColorBrush(Color.FromRgb(0xD7, 0xFF, 0x3F)) },
            new PixelSize(32, 32));

        try
        {
            // Act
            SnapshotFiles.WritePng(path, original, 32, 32);
            var (reloaded, width, height) = SnapshotFiles.ReadPng(path);

            // Assert
            width.ShouldBe(32);
            height.ShouldBe(32);
            ImageComparer.Compare(original, reloaded, 32, 32).DifferingPixels.ShouldBe(0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void CapturedPixelsAreNormalisedToBgraWhateverTheRendererHandsBack()
    {
        // The channel order has to be established rather than assumed. Getting it wrong is
        // invisible from inside the suite: both sides of every comparison are wrong the same way,
        // so everything passes, while every stored image has red and blue swapped. That is exactly
        // what happened here, and only looking at an approved image caught it — the REX Signal
        // yellow-green had rendered as teal.

        // Arrange: the Signal accent, #D7FF3F.
        var content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xD7, 0xFF, 0x3F)),
        };

        // Act
        var pixels = Capture(content, new PixelSize(16, 16));

        // Assert: BGRA, so blue first and red last.
        pixels[0].ShouldBe((byte)0x3F, "blue belongs in the first channel");
        pixels[1].ShouldBe((byte)0xFF);
        pixels[2].ShouldBe((byte)0xD7, "red belongs in the third channel");
        pixels[3].ShouldBe((byte)0xFF);
    }

    [AvaloniaFact]
    public void SwapRedAndBlue_ExchangesOnlyThoseTwoChannels()
    {
        // Arrange
        byte[] pixels = [1, 2, 3, 4, 5, 6, 7, 8];

        // Act
        SnapshotFiles.SwapRedAndBlue(pixels);

        // Assert: green and alpha stay put.
        pixels.ShouldBe([3, 2, 1, 4, 7, 6, 5, 8]);
    }

    [AvaloniaFact]
    public void SwapRedAndBlue_IsItsOwnInverse()
    {
        // Arrange
        byte[] pixels = [0x11, 0x22, 0x33, 0xFF];
        var original = pixels.ToArray();

        // Act
        SnapshotFiles.SwapRedAndBlue(pixels);
        SnapshotFiles.SwapRedAndBlue(pixels);

        // Assert
        pixels.ShouldBe(original);
    }

    [AvaloniaFact]
    public void Matches_RejectsAnEmptyName()
    {
        Should.Throw<ArgumentException>(
            () => Snapshot.Matches("  ", new Border(), new PixelSize(16, 16)));
    }

    /// <summary>Renders a control and returns its pixels, using the harness's own path.</summary>
    private static byte[] Capture(Control content, PixelSize size)
    {
        // Goes through Snapshot.Matches' update mode so these tests exercise the same rendering
        // code the real snapshots use, rather than a parallel implementation that could drift.
        var name = $"harness-capture-{Guid.NewGuid():N}";
        var path = SnapshotFiles.ApprovedPath(name);
        var previous = Environment.GetEnvironmentVariable("FLINT_UPDATE_SNAPSHOTS");
        Environment.SetEnvironmentVariable("FLINT_UPDATE_SNAPSHOTS", "1");
        try
        {
            Snapshot.Matches(name, content, size);
            return SnapshotFiles.ReadPng(path).Pixels;
        }
        finally
        {
            Environment.SetEnvironmentVariable("FLINT_UPDATE_SNAPSHOTS", previous);
            File.Delete(path);
        }
    }

    /// <summary>Counts distinct colours in a BGRA buffer.</summary>
    private static int DistinctColours(byte[] pixels)
    {
        var seen = new HashSet<uint>();
        for (var offset = 0; offset + 3 < pixels.Length; offset += 4)
        {
            seen.Add(BitConverter.ToUInt32(pixels, offset));
        }

        return seen.Count;
    }
}
