using Flint.Protocol;

namespace Flint.Session;

/// <summary>The TV refused the credential it was given: a wrong code, or a token it no longer accepts.</summary>
/// <param name="detail">What the TV said.</param>
public sealed class CastAuthenticationRejectedException(string detail)
    : WireFormatException($"Receiver rejected the session: {detail}");

/// <summary>
/// A TV other than the one a token was granted by answered at its address, so the token was not
/// sent. The address a router hands out can pass to another device.
/// </summary>
/// <param name="expected">The name the token's TV gave.</param>
/// <param name="answered">The name the TV that answered gave.</param>
public sealed class CastTvMismatchException(string expected, string answered)
    : WireFormatException($"A different TV answered: expected {expected}, found {answered}.")
{
    /// <summary>The name the token's TV gave.</summary>
    public string Expected { get; } = expected;

    /// <summary>The name the TV that answered gave.</summary>
    public string Answered { get; } = answered;
}
