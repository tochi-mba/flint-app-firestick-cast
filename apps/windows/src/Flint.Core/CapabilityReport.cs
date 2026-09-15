namespace Flint.Core;

/// <summary>
/// The complete answer to "what can this PC and this television actually do together?"
/// </summary>
/// <param name="Device">The device judged, or <see langword="null"/> when none was found.</param>
/// <param name="Host">What the host can do.</param>
/// <param name="Path">The measured network path, or <see langword="null"/> when unmeasured.</param>
/// <param name="Verdicts">One verdict per <see cref="CastMode"/>.</param>
public sealed record CapabilityReport(
    FireTvDevice? Device,
    HostCapabilities Host,
    NetworkPath? Path,
    IReadOnlyList<ModeVerdict> Verdicts)
{
    /// <summary>
    /// The independent browser experiment verdict. It does not add a generic cast mode, because
    /// browser readiness has separate platform, protocol, endpoint, and WebView gates.
    /// </summary>
    public BrowserCapability Browser { get; init; } = BrowserCapability.Unverified;

    /// <summary>The verdict for one mode.</summary>
    public ModeVerdict this[CastMode mode] => Verdicts.First(verdict => verdict.Mode == mode);

    /// <summary>Whether anything at all is currently offerable.</summary>
    public bool HasAnyAvailableMode => Verdicts.Any(verdict => verdict.IsOfferable);
}
