using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Flint.App.Controls;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// Pins the REX ink/signal visual system.
/// </summary>
/// <remarks>
/// These values are shared with REX Cast and the Fire TV receiver. Drifting one of them here would
/// silently break the family resemblance across three codebases, which no compiler would catch, so
/// the tokens are asserted directly.
/// </remarks>
public sealed class RexDesignSystemTests
{
    [AvaloniaTheory]
    [InlineData("InkColor", "#FF080A09")]
    [InlineData("PanelColor", "#FF111512")]
    [InlineData("RaisedColor", "#FF181E19")]
    [InlineData("LineColor", "#FF29302A")]
    [InlineData("TextColor", "#FFF2F5EE")]
    [InlineData("MutedColor", "#FF858D83")]
    [InlineData("SignalColor", "#FFD7FF3F")]
    [InlineData("LiveColor", "#FFFF774D")]
    public void Palette_MatchesRexCastExactly(string token, string expected)
    {
        // Act
        var found = Application.Current!.TryFindResource(token, out var value);

        // Assert
        found.ShouldBeTrue($"The token '{token}' is missing from the palette.");
        value.ShouldBeOfType<Color>().ShouldBe(Color.Parse(expected));
    }

    [AvaloniaTheory]
    [InlineData("InkBrush")]
    [InlineData("PanelBrush")]
    [InlineData("RaisedBrush")]
    [InlineData("LineBrush")]
    [InlineData("TextBrush")]
    [InlineData("MutedBrush")]
    [InlineData("SignalBrush")]
    [InlineData("LiveBrush")]
    [InlineData("SignalWashBrush")]
    [InlineData("LiveWashBrush")]
    public void EveryPaletteColour_HasAMatchingBrush(string token)
    {
        // Act & Assert
        Application.Current!.TryFindResource(token, out var brush).ShouldBeTrue();
        brush.ShouldBeAssignableTo<IBrush>();
    }

    [AvaloniaFact]
    public void TypeScale_CarriesTheRexMetrics()
    {
        // Ported one-for-one from RexTypography; sp maps to device-independent pixels.

        // Act & Assert
        Resource<double>("HeadlineLargeSize").ShouldBe(28);
        Resource<double>("TitleLargeSize").ShouldBe(18);
        Resource<double>("TitleMediumSize").ShouldBe(15);
        Resource<double>("BodyMediumSize").ShouldBe(13);
        Resource<double>("BodySmallSize").ShouldBe(12);
        Resource<double>("LabelSmallSize").ShouldBe(9);
    }

    [AvaloniaFact]
    public void LabelSmall_KeepsTheTrackingThatDefinesTheSystem()
    {
        // The 1.4 tracking on a 9px bold label is what makes a REX screen read as an instrument.

        // Act & Assert
        Resource<double>("LabelSmallTracking").ShouldBe(1.4);
    }

    [AvaloniaFact]
    public void CornerRadii_MatchRexShapes()
    {
        // Act & Assert
        Resource<CornerRadius>("RadiusSmall").TopLeft.ShouldBe(8);
        Resource<CornerRadius>("RadiusMedium").TopLeft.ShouldBe(12);
        Resource<CornerRadius>("RadiusLarge").TopLeft.ShouldBe(18);
    }

    [AvaloniaTheory]
    [InlineData("HeadlineLarge")]
    [InlineData("TitleLarge")]
    [InlineData("TitleMedium")]
    [InlineData("BodyMedium")]
    [InlineData("BodySmall")]
    [InlineData("LabelSmall")]
    [InlineData("Readout")]
    public void EveryTypeStyle_IsResolvable(string theme)
    {
        // A renamed style fails silently at runtime, showing default text. Catch it here.

        // Act & Assert
        Application.Current!.TryFindResource(theme, out var value).ShouldBeTrue();
        value.ShouldBeOfType<ControlTheme>();
    }

    [AvaloniaTheory]
    [InlineData("SignalButton")]
    [InlineData("OutlineAction")]
    public void ButtonThemes_AreResolvable(string theme)
    {
        // Act & Assert
        Application.Current!.TryFindResource(theme, out var value).ShouldBeTrue();
        value.ShouldBeOfType<ControlTheme>();
    }

    private static T Resource<T>(string key)
    {
        Application.Current!.TryFindResource(key, out var value).ShouldBeTrue($"Missing '{key}'.");
        return value.ShouldBeOfType<T>();
    }
}

/// <summary>
/// The REX components, exercised through the real control templates rather than by reading their
/// properties back.
/// </summary>
public sealed class RexControlTests
{
    [AvaloniaFact]
    public void Pill_UpperCasesItsTextSoTheRhythmCannotBeBroken()
    {
        // Arrange
        var pill = new Pill { Text = "Ready", Tone = Tone.Signal };

        // Act
        var text = Render(pill).GetVisualDescendants().OfType<TextBlock>().First();

        // Assert
        text.Text.ShouldBe("READY");
    }

    [AvaloniaTheory]
    [InlineData(Tone.Signal, "#FFD7FF3F")]
    [InlineData(Tone.Live, "#FFFF774D")]
    [InlineData(Tone.Neutral, "#FF858D83")]
    [InlineData(Tone.Line, "#FF29302A")]
    public void Pill_PaintsItselfFromTheToneNotAnArbitraryBrush(Tone tone, string expected)
    {
        // Arrange
        var pill = new Pill { Text = "status", Tone = tone };

        // Act
        var text = Render(pill).GetVisualDescendants().OfType<TextBlock>().First();

        // Assert
        text.Foreground.ShouldBeOfType<SolidColorBrush>().Color.ShouldBe(Color.Parse(expected));
    }

    [AvaloniaFact]
    public void StatusDot_IsNinePixels()
    {
        // Arrange
        var dot = new StatusDot { Tone = Tone.Signal };

        // Act
        var rendered = Render(dot);

        // Assert
        rendered.Bounds.Width.ShouldBe(9);
        rendered.Bounds.Height.ShouldBe(9);
    }

    [AvaloniaFact]
    public void PageHeading_ShowsEyebrowTitleAndPill()
    {
        // Arrange
        var heading = new PageHeading
        {
            Eyebrow = "local link",
            Title = "Cast",
            Trailing = "ready",
            TrailingTone = Tone.Signal,
        };

        // Act
        var texts = Render(heading).GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        // Assert
        texts.ShouldContain("LOCAL LINK");   // Eyebrow is tracked and upper-cased.
        texts.ShouldContain("Cast");         // The title keeps its given case.
        texts.ShouldContain("READY");
    }

    [AvaloniaFact]
    public void DiagnosticRow_ShowsItsHairlineExceptOnTheLastRow()
    {
        // Arrange
        var middle = new DiagnosticRow { Label = "Interface", Value = "wlan0", IsLast = false };
        var last = new DiagnosticRow { Label = "Interface", Value = "wlan0", IsLast = true };

        // Act
        var middleRule = Render(middle).GetVisualDescendants().OfType<Border>().Last();
        var lastRule = Render(last).GetVisualDescendants().OfType<Border>().Last();

        // Assert
        middleRule.IsVisible.ShouldBeTrue();
        lastRule.IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void EmptyState_ShowsGlyphTitleAndBody()
    {
        // Arrange
        var empty = new EmptyState
        {
            Glyph = "TV",
            Title = "Nothing probed yet",
            Body = "Flint has not looked at your network.",
        };

        // Act
        var texts = Render(empty).GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        // Assert
        texts.ShouldContain("TV");
        texts.ShouldContain("Nothing probed yet");
        texts.ShouldContain("Flint has not looked at your network.");
    }

    [AvaloniaTheory]
    [InlineData(Tone.Signal, "#FFD7FF3F")]
    [InlineData(Tone.Live, "#FFFF774D")]
    [InlineData(Tone.Line, "#FF29302A")]
    public void InfoCard_BorderCarriesTheStatus(Tone tone, string expected)
    {
        // The border colour is the entire status language of the system.

        // Arrange
        var card = new InfoCard { BorderTone = tone, Content = new TextBlock { Text = "body" } };

        // Act
        Render(card);

        // Assert
        card.BorderBrush.ShouldBeOfType<SolidColorBrush>().Color.ShouldBe(Color.Parse(expected));
    }

    [AvaloniaFact]
    public void SectionLabel_UpperCasesItsText()
    {
        // Arrange
        var label = new SectionLabel { Text = "what this pair can do" };

        // Act
        var text = Render(label).GetVisualDescendants().OfType<TextBlock>().First();

        // Assert
        text.Text.ShouldBe("WHAT THIS PAIR CAN DO");
    }

    /// <summary>
    /// Puts a control in a window and forces a layout pass, so template-applied values are real.
    /// </summary>
    private static Control Render(Control control)
    {
        var window = new Window { Width = 600, Height = 400, Content = control };
        window.Show();
        window.UpdateLayout();
        return control;
    }
}
