namespace Flint.Core;

/// <summary>
/// The chosen capture API and encoder for a session, and why they were chosen.
/// </summary>
/// <param name="Api">The capture API to use.</param>
/// <param name="Encoder">The encoder that will consume captured frames.</param>
/// <param name="CaptureAdapterLuid">The adapter driving the display being captured.</param>
/// <param name="Rationale">
/// A short, user-facing explanation. Surfaced in Diagnostics so the choice is never a black box.
/// </param>
public sealed record CaptureStrategy(
    CaptureApi Api,
    HostVideoEncoder Encoder,
    long CaptureAdapterLuid,
    string Rationale)
{
    /// <summary>
    /// Whether frames must cross adapters between capture and encode, which costs a copy over PCIe
    /// and is the single largest avoidable term in the host-side budget.
    /// </summary>
    public bool RequiresCrossAdapterCopy => CaptureAdapterLuid != Encoder.AdapterLuid;
}
