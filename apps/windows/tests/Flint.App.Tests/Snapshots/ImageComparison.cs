namespace Flint.App.Tests.Snapshots;

/// <summary>
/// The result of comparing a rendered frame against its approved image.
/// </summary>
/// <param name="Width">Width of both images, in pixels.</param>
/// <param name="Height">Height of both images, in pixels.</param>
/// <param name="DifferingPixels">Pixels whose difference exceeded the channel tolerance.</param>
/// <param name="WorstChannelDifference">
/// The largest single-channel difference found, on a 0-255 scale. Reported even when the comparison
/// passes, because a run that sits just under the tolerance is worth knowing about before it starts
/// failing.
/// </param>
public readonly record struct ImageComparison(
    int Width,
    int Height,
    int DifferingPixels,
    int WorstChannelDifference)
{
    /// <summary>Total pixels in the compared area.</summary>
    public int TotalPixels => Width * Height;

    /// <summary>The fraction of pixels that differed, from 0 to 1.</summary>
    public double DifferingRatio => TotalPixels == 0 ? 0d : (double)DifferingPixels / TotalPixels;

    /// <summary>The fraction of pixels that differed, as a percentage.</summary>
    public double DifferingPercent => DifferingRatio * 100d;

    /// <summary>Whether the difference is within <paramref name="maximumPercent"/>.</summary>
    public bool IsWithin(double maximumPercent) => DifferingPercent <= maximumPercent;

    /// <summary>A one-line summary for a test failure message.</summary>
    public override string ToString() =>
        $"{DifferingPixels}/{TotalPixels} pixels differ ({DifferingPercent:F3}%), "
        + $"worst channel difference {WorstChannelDifference}/255";
}

/// <summary>
/// Compares two images pixel by pixel, with a tolerance for rendering noise.
/// </summary>
/// <remarks>
/// <para>
/// Two tolerances rather than one, because they guard against different things.
/// </para>
/// <para>
/// The <em>channel tolerance</em> absorbs the small, uniform differences that text antialiasing and
/// gradient dithering produce between machines and driver versions. Without it every approved image
/// would have to be regenerated on whichever machine happened to run the tests, which is the fastest
/// way to make a team stop trusting screenshot tests and start approving every diff unread.
/// </para>
/// <para>
/// The <em>differing-pixel budget</em> is what actually catches regressions. A moved button, a
/// changed colour token or a font that failed to load moves thousands of pixels well past the
/// channel tolerance; a slightly different antialiasing kernel moves a few hundred barely past it.
/// Only the first should fail a build.
/// </para>
/// <para>
/// Alpha is compared like any other channel. A control that renders at the right colour and the
/// wrong opacity is a real regression, and treating alpha as decoration would hide it.
/// </para>
/// </remarks>
public static class ImageComparer
{
    /// <summary>
    /// How far a single channel may differ before the pixel counts as changed.
    /// </summary>
    /// <remarks>
    /// Eight of 255, about 3%. Chosen to sit above the antialiasing noise measured between runs on
    /// the same machine and well below the smallest deliberate difference in the REX palette: the
    /// closest two tokens, <c>Panel</c> (#111512) and <c>Raised</c> (#181E19), are 9 apart on their
    /// nearest channel, so swapping one for the other still fails.
    /// </remarks>
    public const int DefaultChannelTolerance = 8;

    /// <summary>
    /// The share of pixels that may differ before a comparison fails, as a percentage.
    /// </summary>
    /// <remarks>
    /// A tenth of one percent. On a 1280x800 page that is about a thousand pixels — far more than
    /// antialiasing produces, and far less than any visible element occupies.
    /// </remarks>
    public const double DefaultMaximumDifferingPercent = 0.1d;

    /// <summary>
    /// Compares two BGRA pixel buffers of the same size.
    /// </summary>
    /// <param name="expected">The approved image's pixels, four bytes per pixel.</param>
    /// <param name="actual">The rendered frame's pixels, four bytes per pixel.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="channelTolerance">Per-channel tolerance, 0 to 255.</param>
    /// <returns>How the two images differ.</returns>
    /// <exception cref="ArgumentException">
    /// When either buffer is not exactly <c>width * height * 4</c> bytes. A size mismatch is a bug
    /// in the caller rather than a failed comparison, so it is not reported as a difference.
    /// </exception>
    public static ImageComparison Compare(
        ReadOnlySpan<byte> expected,
        ReadOnlySpan<byte> actual,
        int width,
        int height,
        int channelTolerance = DefaultChannelTolerance)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        ArgumentOutOfRangeException.ThrowIfNegative(channelTolerance);

        var required = checked(width * height * BytesPerPixel);
        if (expected.Length != required)
        {
            throw new ArgumentException(
                $"expected {required} bytes for {width}x{height}, got {expected.Length}",
                nameof(expected));
        }

        if (actual.Length != required)
        {
            throw new ArgumentException(
                $"expected {required} bytes for {width}x{height}, got {actual.Length}",
                nameof(actual));
        }

        var differing = 0;
        var worst = 0;

        for (var offset = 0; offset < required; offset += BytesPerPixel)
        {
            var pixelWorst = 0;
            for (var channel = 0; channel < BytesPerPixel; channel++)
            {
                var difference = Math.Abs(
                    expected[offset + channel] - actual[offset + channel]);
                if (difference > pixelWorst)
                {
                    pixelWorst = difference;
                }
            }

            if (pixelWorst > worst)
            {
                worst = pixelWorst;
            }

            if (pixelWorst > channelTolerance)
            {
                differing++;
            }
        }

        return new ImageComparison(width, height, differing, worst);
    }

    /// <summary>
    /// Builds a diff image highlighting the pixels that differed.
    /// </summary>
    /// <remarks>
    /// Unchanged pixels are kept, dimmed and desaturated, so the changed ones can be located in
    /// context rather than floating in a black field. Changed pixels are painted in the REX
    /// <c>Live</c> orange, which is the palette's "something is wrong" colour and does not occur in
    /// a dimmed grey background.
    /// </remarks>
    /// <param name="expected">The approved image's pixels.</param>
    /// <param name="actual">The rendered frame's pixels.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="channelTolerance">Per-channel tolerance, 0 to 255.</param>
    /// <returns>A BGRA buffer the same size as the inputs.</returns>
    public static byte[] BuildDiff(
        ReadOnlySpan<byte> expected,
        ReadOnlySpan<byte> actual,
        int width,
        int height,
        int channelTolerance = DefaultChannelTolerance)
    {
        var required = checked(width * height * BytesPerPixel);
        var diff = new byte[required];

        for (var offset = 0; offset < required; offset += BytesPerPixel)
        {
            var pixelWorst = 0;
            for (var channel = 0; channel < BytesPerPixel; channel++)
            {
                var difference = Math.Abs(expected[offset + channel] - actual[offset + channel]);
                if (difference > pixelWorst)
                {
                    pixelWorst = difference;
                }
            }

            if (pixelWorst > channelTolerance)
            {
                diff[offset] = LiveBlue;
                diff[offset + 1] = LiveGreen;
                diff[offset + 2] = LiveRed;
                diff[offset + 3] = 255;
                continue;
            }

            // Grey at a quarter brightness: enough to read the layout, dark enough that the
            // highlight colour is unmistakable.
            var luminance = (byte)((expected[offset] + expected[offset + 1] + expected[offset + 2])
                / 3 / 4);
            diff[offset] = luminance;
            diff[offset + 1] = luminance;
            diff[offset + 2] = luminance;
            diff[offset + 3] = 255;
        }

        return diff;
    }

    /// <summary>Bytes per pixel in the BGRA buffers this comparer works on.</summary>
    private const int BytesPerPixel = 4;

    /// <summary>Blue channel of the REX <c>Live</c> accent, #FF774D.</summary>
    private const byte LiveBlue = 0x4D;

    /// <summary>Green channel of the REX <c>Live</c> accent, #FF774D.</summary>
    private const byte LiveGreen = 0x77;

    /// <summary>Red channel of the REX <c>Live</c> accent, #FF774D.</summary>
    private const byte LiveRed = 0xFF;
}
