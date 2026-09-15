using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;

namespace Flint.App.Tests.Snapshots;

/// <summary>
/// Where approved images live, and how images get in and out of them.
/// </summary>
/// <remarks>
/// Approved images are resolved against this source file's own path rather than the build output.
/// A test run must be able to write a rejected image back into the repository next to the one it
/// failed against, so a person can look at the two side by side; copying approved images into
/// <c>bin</c> would put the rejects somewhere nobody thinks to look, and lose them on the next
/// clean.
/// </remarks>
public static class SnapshotFiles
{
    /// <summary>
    /// The directory holding approved images, created on first use.
    /// </summary>
    public static string ApprovedDirectory
    {
        get
        {
            var directory = Path.Combine(SourceDirectory(), "Approved");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    /// <summary>
    /// The directory holding images from failed runs, created on first use.
    /// </summary>
    /// <remarks>
    /// Kept separate from the approved images and ignored by git, so a failing run can never be
    /// turned into an approval by an absent-minded <c>git add .</c>.
    /// </remarks>
    public static string RejectedDirectory
    {
        get
        {
            var directory = Path.Combine(SourceDirectory(), "Rejected");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    /// <summary>
    /// Whether this run should overwrite approved images instead of asserting against them.
    /// </summary>
    /// <remarks>
    /// Driven by the <c>FLINT_UPDATE_SNAPSHOTS</c> environment variable. Deliberately an
    /// environment variable and not a constant someone can flip in the source: a build that
    /// silently rewrites its own expectations proves nothing, and a committed <c>true</c> would
    /// turn the whole suite off without failing anything.
    /// </remarks>
    public static bool IsUpdating =>
        Environment.GetEnvironmentVariable("FLINT_UPDATE_SNAPSHOTS") is "1" or "true";

    /// <summary>The approved image's path for a named snapshot.</summary>
    public static string ApprovedPath(string name) =>
        Path.Combine(ApprovedDirectory, $"{name}.png");

    /// <summary>The path a failed run writes its rendered frame to.</summary>
    public static string RejectedPath(string name) =>
        Path.Combine(RejectedDirectory, $"{name}.actual.png");

    /// <summary>The path a failed run writes its difference image to.</summary>
    public static string DiffPath(string name) => Path.Combine(RejectedDirectory, $"{name}.diff.png");

    /// <summary>
    /// Reads a PNG as a BGRA buffer.
    /// </summary>
    /// <returns>The pixels, their width, and their height.</returns>
    public static (byte[] Pixels, int Width, int Height) ReadPng(string path)
    {
        using var bitmap = new Bitmap(path);
        return ToPixels(bitmap);
    }

    /// <summary>
    /// Copies a bitmap's pixels into a BGRA buffer.
    /// </summary>
    /// <remarks>
    /// Both the approved image and the rendered frame are put through this same call before they
    /// are compared, so any decision the imaging stack makes about premultiplied alpha or channel
    /// order applies equally to both and cancels out. Comparing a decoded PNG against a live
    /// bitmap directly does not have that property, and produces differences that track the
    /// imaging library rather than the user interface.
    /// </remarks>
    public static (byte[] Pixels, int Width, int Height) ToPixels(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        var stride = size.Width * BytesPerPixel;
        var length = stride * size.Height;
        var buffer = new byte[length];

        var scratch = Marshal.AllocHGlobal(length);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), scratch, length, stride);
            Marshal.Copy(scratch, buffer, 0, length);
        }
        finally
        {
            Marshal.FreeHGlobal(scratch);
        }

        if (bitmap.Format == Avalonia.Platform.PixelFormat.Rgba8888)
        {
            SwapRedAndBlue(buffer);
        }

        return (buffer, size.Width, size.Height);
    }

    /// <summary>
    /// Swaps the red and blue channels of a four-byte-per-pixel buffer, in place.
    /// </summary>
    /// <remarks>
    /// The headless renderer hands back <c>Rgba8888</c> while this code works in BGRA throughout.
    /// Normalising here rather than threading a format through every caller matters because getting
    /// it wrong is invisible: both sides of every comparison would be wrong in the same way, so the
    /// suite passes completely while every stored image has red and blue swapped — which is exactly
    /// what happened, and was caught only by looking at an approved image and noticing that the REX
    /// <c>Signal</c> yellow-green had rendered as teal.
    /// </remarks>
    public static void SwapRedAndBlue(byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        for (var offset = 0; offset + 3 < pixels.Length; offset += BytesPerPixel)
        {
            (pixels[offset], pixels[offset + 2]) = (pixels[offset + 2], pixels[offset]);
        }
    }

    /// <summary>Writes a BGRA buffer to a PNG.</summary>
    public static void WritePng(string path, byte[] pixels, int width, int height)
    {
        var stride = width * BytesPerPixel;
        var scratch = Marshal.AllocHGlobal(pixels.Length);
        try
        {
            Marshal.Copy(pixels, 0, scratch, pixels.Length);
            using var bitmap = new Bitmap(
                Avalonia.Platform.PixelFormat.Bgra8888,
                Avalonia.Platform.AlphaFormat.Unpremul,
                scratch,
                new PixelSize(width, height),
                new Vector(96, 96),
                stride);
            bitmap.Save(path);
        }
        finally
        {
            Marshal.FreeHGlobal(scratch);
        }
    }

    /// <summary>Bytes per pixel in the BGRA buffers these snapshots use.</summary>
    private const int BytesPerPixel = 4;

    /// <summary>This file's directory, resolved at compile time.</summary>
    private static string SourceDirectory([CallerFilePath] string path = "") =>
        Path.GetDirectoryName(path)
        ?? throw new InvalidOperationException("the snapshot source path has no directory");
}
