using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Flint.App.ViewModels;

namespace Flint.App.Services;

/// <summary>The tray icon for each state: Flint's own mark, with a dot that says what it is doing.</summary>
/// <remarks>
/// Drawn rather than shipped as four more image files, so the states always match the mark. Idle is
/// the mark dimmed; connected is the mark; sharing adds a red dot, the colour people read as "live";
/// paused adds an amber one.
/// </remarks>
internal static class TrayIconArt
{
    /// <summary>The size the icon is drawn at; Windows scales it to the notification area.</summary>
    internal const int Size = 32;

    private static readonly Uri Mark = new("avares://Flint.App/Assets/flint-icon-256.png");

    /// <summary>The live dot.</summary>
    internal static readonly Color Live = Color.FromRgb(0xFF, 0x4D, 0x4D);

    /// <summary>The paused dot.</summary>
    internal static readonly Color Held = Color.FromRgb(0xFF, 0xB0, 0x20);

    /// <summary>The icon for <paramref name="state"/>.</summary>
    internal static Bitmap Draw(TrayState state)
    {
        using var mark = new Bitmap(AssetLoader.Open(Mark));
        var art = new RenderTargetBitmap(new PixelSize(Size, Size));
        using (var context = art.CreateDrawingContext())
        {
            using (context.PushOpacity(state is TrayState.Idle ? 0.55 : 1))
            {
                context.DrawImage(mark, new Rect(0, 0, Size, Size));
            }

            if (DotFor(state) is { } colour)
            {
                var dot = new Rect(Size - 13, Size - 13, 12, 12);
                context.DrawEllipse(new SolidColorBrush(Colors.Black), null, dot.Inflate(1.5));
                context.DrawEllipse(new SolidColorBrush(colour), null, dot);
            }
        }

        return art;
    }

    /// <summary>The dot a state shows, or null for none.</summary>
    internal static Color? DotFor(TrayState state) => state switch
    {
        TrayState.Sharing => Live,
        TrayState.Paused => Held,
        _ => null,
    };
}
