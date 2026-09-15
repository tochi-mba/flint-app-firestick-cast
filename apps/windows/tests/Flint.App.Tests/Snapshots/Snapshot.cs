using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;

namespace Flint.App.Tests.Snapshots;

/// <summary>
/// Renders a control and holds it to an approved image.
/// </summary>
/// <remarks>
/// <para>
/// This is the part of the suite that can check what the other tests cannot: that the design system
/// still <em>looks</em> right. A test can assert that a button is enabled, that its text is upper
/// case and that its background resolves to the <c>Signal</c> token, and every one of those can pass
/// while the button sits half off the page behind a panel. Only a rendered comparison catches that.
/// </para>
/// <para>
/// What it deliberately does not do is replace those tests. A screenshot failure says "something
/// moved" and nothing about what or why; the assertions in the other files say which property is
/// wrong. Both together are worth more than either, which is why this was added alongside them
/// rather than instead of them.
/// </para>
/// </remarks>
public static class Snapshot
{
    /// <summary>
    /// The window size every snapshot renders at.
    /// </summary>
    /// <remarks>
    /// Fixed rather than sized to content, because a layout regression frequently <em>is</em> a
    /// change in content size, and a window that grows to fit would absorb exactly the bug this is
    /// meant to catch. 1280x800 is the smallest window Flint supports.
    /// </remarks>
    public static readonly PixelSize DefaultSize = new(1280, 800);

    /// <summary>
    /// Renders <paramref name="content"/> and compares it with the approved image for
    /// <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The snapshot's name, which is also its file name.</param>
    /// <param name="content">The control to render.</param>
    /// <param name="size">The window size, defaulting to <see cref="DefaultSize"/>.</param>
    /// <param name="maximumDifferingPercent">
    /// The share of pixels allowed to differ. Raise it only for content that is genuinely
    /// non-deterministic, and say why at the call site.
    /// </param>
    /// <exception cref="SnapshotException">
    /// When the rendered frame differs from the approved image by more than the budget, or when no
    /// approved image exists yet.
    /// </exception>
    public static void Matches(
        string name,
        Control content,
        PixelSize? size = null,
        double maximumDifferingPercent = ImageComparer.DefaultMaximumDifferingPercent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(content);

        Compare(name, Render(content, size ?? DefaultSize), maximumDifferingPercent);
    }

    /// <summary>Holds a captured frame to its approved image.</summary>
    private static void Compare(
        string name,
        (byte[] Pixels, int Width, int Height) rendered,
        double maximumDifferingPercent)
    {
        var approvedPath = SnapshotFiles.ApprovedPath(name);

        if (SnapshotFiles.IsUpdating)
        {
            SnapshotFiles.WritePng(approvedPath, rendered.Pixels, rendered.Width, rendered.Height);
            return;
        }

        if (!File.Exists(approvedPath))
        {
            // Written where a person can look at it and decide, rather than approved automatically.
            // A suite that adopts whatever it first renders can never fail on a first run, which is
            // exactly when a new page is most likely to be wrong.
            var rejected = SnapshotFiles.RejectedPath(name);
            SnapshotFiles.WritePng(rejected, rendered.Pixels, rendered.Width, rendered.Height);
            throw new SnapshotException(
                $"no approved image for '{name}'. The rendered frame is at {rejected}. "
                + "Check it looks right, then re-run with FLINT_UPDATE_SNAPSHOTS=1 to approve it.");
        }

        var (approvedPixels, approvedWidth, approvedHeight) = SnapshotFiles.ReadPng(approvedPath);

        if (approvedWidth != rendered.Width || approvedHeight != rendered.Height)
        {
            throw new SnapshotException(
                $"'{name}' rendered at {rendered.Width}x{rendered.Height} but the approved image is "
                + $"{approvedWidth}x{approvedHeight}. A size change is never incidental: either the "
                + "window size changed or the layout no longer fits it.");
        }

        var comparison = ImageComparer.Compare(
            approvedPixels,
            rendered.Pixels,
            rendered.Width,
            rendered.Height);

        if (comparison.IsWithin(maximumDifferingPercent))
        {
            return;
        }

        var rejectedPath = SnapshotFiles.RejectedPath(name);
        var diffPath = SnapshotFiles.DiffPath(name);
        SnapshotFiles.WritePng(rejectedPath, rendered.Pixels, rendered.Width, rendered.Height);
        SnapshotFiles.WritePng(
            diffPath,
            ImageComparer.BuildDiff(approvedPixels, rendered.Pixels, rendered.Width, rendered.Height),
            rendered.Width,
            rendered.Height);

        throw new SnapshotException(
            $"'{name}' does not match its approved image: {comparison}, over the "
            + $"{maximumDifferingPercent:F3}% budget.{Environment.NewLine}"
            + $"  approved: {approvedPath}{Environment.NewLine}"
            + $"  rendered: {rejectedPath}{Environment.NewLine}"
            + $"  diff:     {diffPath}{Environment.NewLine}"
            + "If the change is intended, re-run with FLINT_UPDATE_SNAPSHOTS=1 to approve it.");
    }

    /// <summary>
    /// Renders a whole window and compares it with the approved image for <paramref name="name"/>.
    /// </summary>
    /// <remarks>
    /// For the shell, which is a window rather than a page. Lifting a window's content out and
    /// re-parenting it into a plain window does not work: the content inherits its data context
    /// from the window it belongs to, so a re-parented tree renders with no data at all. That
    /// mistake produced a first approved image of the shell with an empty navigation rail, which
    /// looked exactly like a real layout bug.
    /// </remarks>
    /// <param name="name">The snapshot's name, which is also its file name.</param>
    /// <param name="window">The window to render. It is shown and left for the caller to dispose.</param>
    /// <param name="size">The window size, defaulting to <see cref="DefaultSize"/>.</param>
    /// <param name="maximumDifferingPercent">The share of pixels allowed to differ.</param>
    public static void MatchesWindow(
        string name,
        Window window,
        PixelSize? size = null,
        double maximumDifferingPercent = ImageComparer.DefaultMaximumDifferingPercent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(window);

        var target = size ?? DefaultSize;
        window.Width = target.Width;
        window.Height = target.Height;
        window.SystemDecorations = SystemDecorations.None;
        window.Show();
        window.UpdateLayout();

        Compare(name, Capture(window), maximumDifferingPercent);
    }

    /// <summary>
    /// Renders a control in a window and captures the frame.
    /// </summary>
    /// <remarks>
    /// The window is the real one the application uses, styles and control themes included, so a
    /// token renamed in <c>FlintColors.axaml</c> changes these images. Layout is run explicitly
    /// before the capture: the headless platform does not pump a layout pass on its own, and
    /// capturing without one photographs an unmeasured tree, which renders as an empty window.
    /// </remarks>
    private static (byte[] Pixels, int Width, int Height) Render(Control content, PixelSize size)
    {
        var window = new Window
        {
            Content = content,
            Width = size.Width,
            Height = size.Height,
            SystemDecorations = SystemDecorations.None,
        };

        window.Show();
        window.Measure(new Size(size.Width, size.Height));
        window.Arrange(new Rect(0, 0, size.Width, size.Height));
        window.UpdateLayout();

        return Capture(window);
    }

    /// <summary>Captures a shown window's rendered frame.</summary>
    private static (byte[] Pixels, int Width, int Height) Capture(Window window)
    {
        var frame = window.CaptureRenderedFrame()
            ?? throw new SnapshotException(
                "the headless platform captured no frame. This means real rendering is off: "
                + "TestAppBuilder must use UseSkia() with UseHeadlessDrawing = false.");

        using (frame)
        {
            return SnapshotFiles.ToPixels(frame);
        }
    }
}

/// <summary>
/// A screenshot test failure.
/// </summary>
/// <remarks>
/// Its own type so the message survives intact. The assertion libraries wrap and reformat what they
/// are given, and these messages carry file paths a person needs to be able to copy.
/// </remarks>
public sealed class SnapshotException(string message) : Exception(message);
