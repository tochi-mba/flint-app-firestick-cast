namespace Flint.Core;

/// <summary>
/// What this Windows host can actually do, as proven by probing.
/// </summary>
/// <param name="Adapters">Every graphics adapter present.</param>
/// <param name="Encoders">Every hardware encoder confirmed by a capability probe.</param>
/// <param name="PrimaryDisplayAdapterLuid">The adapter driving the primary display.</param>
/// <param name="WindowsBuild">The Windows build number used for display compatibility checks.</param>
/// <param name="EncodersProbed">
/// Whether the encoder probe actually ran. An empty <paramref name="Encoders"/> list means
/// "none found" only when this is <see langword="true"/>; otherwise it means "not yet asked", and
/// the two must never be reported as the same thing.
/// </param>
/// <param name="ScreenCaptureBackend">
/// The capture backend the engine offers on this host, or <see langword="null"/> when it offers
/// none. Reported by the engine rather than assumed, so a mode that depends on capture is judged
/// on what the build can actually do instead of on a hardcoded expectation.
/// </param>
public sealed record HostCapabilities(
    IReadOnlyList<DisplayAdapter> Adapters,
    IReadOnlyList<HostVideoEncoder> Encoders,
    long PrimaryDisplayAdapterLuid,
    int WindowsBuild,
    bool EncodersProbed = true,
    CaptureApi? ScreenCaptureBackend = null)
{
    /// <summary>Whether this build can capture the screen at all.</summary>
    /// <remarks>
    /// Mirroring and second-screen output both consume captured frames, so neither can be offered
    /// when this is <see langword="false"/> — regardless of how good the encoder or network is.
    /// </remarks>
    public bool CanCaptureScreen => ScreenCaptureBackend is not null;

    /// <summary>Whether any encoder is fit for a live session.</summary>
    public bool HasSessionCapableEncoder => Encoders.Any(encoder => encoder.IsSessionCapable);

    /// <summary>
    /// Whether Flint knows enough to make a claim about encoding on this host.
    /// </summary>
    public bool CanJudgeEncoding => EncodersProbed;

    /// <summary>Whether capture and encode would have to cross adapters on this host.</summary>
    public bool IsHybridGraphics => Adapters.Count(adapter => !adapter.IsSoftware) > 1;
}
