using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Styling;
using Flint.App.Controls;

namespace Flint.App.Tests.Snapshots;

/// <summary>
/// The REX control library, each control held to an approved image.
/// </summary>
/// <remarks>
/// <para>
/// The page snapshots would catch most of what breaks here, but they would catch it in the worst
/// possible way: a token changed in <c>FlintColors.axaml</c> fails a dozen page images at once, and
/// the diff on each shows the same control wrong in the same place. These narrow the failure to the
/// control that actually changed.
/// </para>
/// <para>
/// Every tone of every control appears, because the tones are the part of the design system that
/// carries meaning. <c>Signal</c> means ready, <c>Live</c> means blocked, and a control that renders
/// the wrong one is not a cosmetic problem — it tells the user the opposite of the truth.
/// </para>
/// </remarks>
public sealed class ControlSnapshotTests
{
    [AvaloniaFact]
    public void Pill_EveryTone()
    {
        var content = Row(
            new Pill { Text = "READY", Tone = Tone.Signal },
            new Pill { Text = "BLOCKED", Tone = Tone.Live },
            new Pill { Text = "NOT PROBED", Tone = Tone.Neutral });

        Snapshot.Matches("control-pill-tones", content, new PixelSize(520, 96));
    }

    [AvaloniaFact]
    public void StatusDot_EveryTone()
    {
        var content = Row(
            new StatusDot { Tone = Tone.Signal },
            new StatusDot { Tone = Tone.Live },
            new StatusDot { Tone = Tone.Neutral });

        Snapshot.Matches("control-status-dot-tones", content, new PixelSize(240, 80));
    }

    [AvaloniaFact]
    public void SectionLabel()
    {
        // The signature REX label: nine pixels, bold, wide tracking, always upper case. Its whole
        // identity is typographic, which is precisely what a property assertion cannot check.
        var content = new SectionLabel { Text = "What this pair can do" };

        Snapshot.Matches("control-section-label", content, new PixelSize(420, 64));
    }

    [AvaloniaFact]
    public void PageHeading_WithATrailingPill()
    {
        var content = new PageHeading
        {
            Eyebrow = "Local link",
            Title = "Cast",
            Trailing = "READY",
            TrailingTone = Tone.Signal,
        };

        Snapshot.Matches("control-page-heading", content, new PixelSize(720, 120));
    }

    [AvaloniaFact]
    public void PageHeading_WithNoTrailingPill()
    {
        // The trailing pill is optional, and a heading that reserves space for one it does not have
        // leaves the title vertically off-centre against every other page.
        var content = new PageHeading { Eyebrow = "Settings", Title = "Settings" };

        Snapshot.Matches("control-page-heading-bare", content, new PixelSize(720, 120));
    }

    [AvaloniaFact]
    public void InfoCard_EveryBorderTone()
    {
        var content = Column(
            Card(Tone.Neutral, "Neutral", "The resting state, with a hairline border."),
            Card(Tone.Signal, "Ready", "Signal border: this pair can do the thing."),
            Card(Tone.Live, "Blocked", "Live border: this pair cannot, and here is why."),
            Card(Tone.Line, "Structural", "A hairline with no status meaning attached to it."));

        Snapshot.Matches("control-info-card-tones", content, new PixelSize(680, 540));
    }

    [AvaloniaFact]
    public void DiagnosticRow_Stacked()
    {
        // Rows share a hairline divider, so they only look right in sequence: the spacing above the
        // first and below the last is the part that goes wrong.
        var content = Column(
            new DiagnosticRow { Label = "Round trip", Value = "4.0 ms" },
            new DiagnosticRow { Label = "Jitter", Value = "1.0 ms" },
            new DiagnosticRow { Label = "Throughput", Value = "120 Mbit/s" },
            new DiagnosticRow { Label = "Loss", Value = "0.0%" });

        Snapshot.Matches("control-diagnostic-rows", content, new PixelSize(560, 260));
    }

    [AvaloniaFact]
    public void EmptyState()
    {
        var content = new EmptyState
        {
            Glyph = "TV",
            Title = "Nothing probed yet",
            Body = "Flint has not looked at your network. It will find receivers that advertise "
                + "themselves, identify what each one runs, and then say plainly which modes your "
                + "hardware can actually do.",
        };

        Snapshot.Matches("control-empty-state", content, new PixelSize(680, 320));
    }

    /// <summary>Lays controls out horizontally on the page ground.</summary>
    private static Control Row(params Control[] children)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        return Ground(panel);
    }

    /// <summary>Lays controls out vertically on the page ground.</summary>
    private static Control Column(params Control[] children)
    {
        var panel = new StackPanel { Spacing = 12 };
        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        return Ground(panel);
    }

    /// <summary>
    /// Puts content on the REX page ground with a margin.
    /// </summary>
    /// <remarks>
    /// The margin matters: a control whose border sits flush against the frame edge cannot be seen
    /// to have shifted by a pixel, which is most of what these images are for.
    /// </remarks>
    private static Control Ground(Control content) =>
        new Border
        {
            Background = Application.Current!.FindResource("Ink") as Avalonia.Media.IBrush,
            Padding = new Thickness(24),
            Child = content,
        };

    private static Control Card(Tone tone, string title, string body) =>
        new InfoCard
        {
            BorderTone = tone,
            Content = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        Theme = Application.Current!.FindResource("TitleMedium") as ControlTheme,
                    },
                    new TextBlock { Text = body, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                },
            },
        };
}
