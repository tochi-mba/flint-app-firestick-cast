namespace Flint.App.Controls;

/// <summary>
/// The semantic colour roles a REX component can take.
/// </summary>
/// <remarks>
/// Components take a tone rather than a brush so a status can never be painted an off-palette
/// colour. Mapping to brushes happens once, in the control templates.
/// </remarks>
public enum Tone
{
    /// <summary>Inactive or informational. Muted grey.</summary>
    Neutral = 0,

    /// <summary>Ready, connected, live. The signal accent.</summary>
    Signal = 1,

    /// <summary>Blocked, failed, recording. The warm accent.</summary>
    Live = 2,

    /// <summary>Structural only — a hairline with no status meaning.</summary>
    Line = 3,
}
