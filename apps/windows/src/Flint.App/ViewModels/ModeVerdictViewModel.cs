using Flint.App.Controls;
using Flint.Core;

namespace Flint.App.ViewModels;

/// <summary>
/// Presents one <see cref="ModeVerdict"/> as a card.
/// </summary>
/// <remarks>
/// The presentation mapping lives here rather than in XAML so the rule "an impossible mode is never
/// painted like an available one" is unit-testable.
/// </remarks>
/// <param name="verdict">The assessed verdict.</param>
public sealed class ModeVerdictViewModel(ModeVerdict verdict)
{
    /// <summary>The verdict being presented.</summary>
    public ModeVerdict Verdict { get; } = verdict;

    /// <summary>The mode's name, as a person would say it.</summary>
    public string Title => Verdict.Mode switch
    {
        CastMode.Mirror => "Mirror this screen",
        CastMode.SecondScreen => "Second screen",
        CastMode.MediaHandoff => "Play a file on the TV",
        _ => Verdict.Mode.ToString(),
    };

    /// <summary>The explanation, shown verbatim from the assessor.</summary>
    public string Reason => Verdict.Reason;

    /// <summary>What the user can do, or an empty string when nothing would help.</summary>
    public string Remedy => Verdict.Remedy ?? string.Empty;

    /// <summary>Whether to show the remedy line at all.</summary>
    public bool HasRemedy => !string.IsNullOrWhiteSpace(Verdict.Remedy);

    /// <summary>
    /// The word shown in the card's pill.
    /// </summary>
    /// <remarks>
    /// "Coming soon" rather than "Not built yet" for an unimplemented mode: both are honest, but
    /// the first reads as a plan and the second as an apology. It is deliberately distinct from
    /// "Blocked", which means the user could act — nobody should go hunting through settings for a
    /// mode that has not shipped.
    /// </remarks>
    public string StatusLabel => Verdict.Status switch
    {
        ModeStatus.Available => "Ready",
        ModeStatus.Blocked => "Blocked",
        ModeStatus.Impossible => "Not possible",
        ModeStatus.NotImplemented => "Coming soon",
        _ => "Unknown",
    };

    /// <summary>
    /// Whether this mode is announced as planned rather than broken.
    /// </summary>
    /// <remarks>
    /// Bound by the card so an unshipped mode can be styled as an upcoming feature instead of a
    /// failure, and so the disabled control beside it can explain itself without the user reading
    /// the absence as something they misconfigured.
    /// </remarks>
    public bool IsComingSoon => Verdict.Status is ModeStatus.NotImplemented;

    /// <summary>
    /// The card's colour.
    /// </summary>
    /// <remarks>
    /// Only a genuinely available mode gets Signal. Unavailable modes stay neutral so the accent
    /// keeps meaning "this works".
    /// </remarks>
    public Tone Tone => Verdict.Status switch
    {
        ModeStatus.Available => Tone.Signal,
        ModeStatus.Blocked => Tone.Live,
        ModeStatus.Impossible => Tone.Live,
        _ => Tone.Neutral,
    };

    /// <summary>Whether this mode may be presented as a working control.</summary>
    public bool IsOfferable => Verdict.IsOfferable;
}
