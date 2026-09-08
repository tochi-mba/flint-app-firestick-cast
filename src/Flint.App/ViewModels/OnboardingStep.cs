using Flint.App.Controls;

namespace Flint.App.ViewModels;

/// <summary>
/// One item in a step's list: either a numbered instruction or an unnumbered fact.
/// </summary>
/// <param name="Text">What to do, or what is true.</param>
/// <param name="Number">
/// Position in the procedure, or <see langword="null"/> when the list is a set of facts rather than
/// a sequence.
/// </param>
public sealed record OnboardingPoint(string Text, int? Number = null)
{
    /// <summary>Whether this item is part of a numbered sequence.</summary>
    public bool IsNumbered => Number is not null;

    /// <summary>The marker shown beside the text.</summary>
    public string Marker => Number?.ToString() ?? string.Empty;
}

/// <summary>
/// One page of the introduction.
/// </summary>
/// <param name="Eyebrow">The tracked label above the title.</param>
/// <param name="Title">What this step is about.</param>
/// <param name="Body">The explanation. Complete sentences; this is the only place a first-time user
/// is told what Flint can and cannot do.</param>
/// <param name="Points">Concrete steps or facts, shown as a list. May be empty.</param>
/// <param name="Tone">
/// The accent for this step. <see cref="Controls.Tone.Live"/> marks the step carrying a limitation
/// the user cannot work around, so it does not read like the rest of the walkthrough.
/// </param>
/// <param name="Ordered">
/// Whether <paramref name="Points"/> is a sequence that must be followed in order. Numbered when it
/// is, because a procedure rendered as bullets invites people to do the steps in the wrong order —
/// which is exactly how the Fire TV developer-options unlock goes wrong.
/// </param>
public sealed record OnboardingStep(
    string Eyebrow,
    string Title,
    string Body,
    IReadOnlyList<string> Points,
    Tone Tone = Tone.Signal,
    bool Ordered = false)
{
    /// <summary>The points as the view renders them, numbered when the step is a procedure.</summary>
    public IReadOnlyList<OnboardingPoint> DisplayPoints =>
        [.. Points.Select((text, index) => new OnboardingPoint(text, Ordered ? index + 1 : null))];
}
