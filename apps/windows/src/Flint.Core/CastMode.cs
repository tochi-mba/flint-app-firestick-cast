namespace Flint.Core;

/// <summary>The things Flint can be asked to do.</summary>
public enum CastMode
{
    /// <summary>Mirror the desktop to the television with the lowest achievable latency.</summary>
    Mirror = 0,

    /// <summary>Extend the desktop onto the television as an additional monitor.</summary>
    SecondScreen = 1,

    /// <summary>Play a local media file on the television at original quality, without transcoding.</summary>
    MediaHandoff = 2,
}
