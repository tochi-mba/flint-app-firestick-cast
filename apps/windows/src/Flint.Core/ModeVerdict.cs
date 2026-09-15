namespace Flint.Core;

/// <summary>
/// Whether one <see cref="CastMode"/> is possible on one host-and-device pair, and why.
/// </summary>
/// <param name="Mode">The mode being judged.</param>
/// <param name="Status">The verdict.</param>
/// <param name="Reason">
/// A complete, user-facing sentence. Shown verbatim in the UI, so it must read as an explanation
/// rather than a diagnostic code, and must never overstate what Flint knows.
/// </param>
/// <param name="Remedy">
/// What the user can do about it, when there is something. <see langword="null"/> when the verdict
/// is <see cref="ModeStatus.Impossible"/>, because offering a remedy for an impossibility is a lie.
/// </param>
public sealed record ModeVerdict(CastMode Mode, ModeStatus Status, string Reason, string? Remedy = null)
{
    /// <summary>Whether this mode may be offered as a working control in the UI.</summary>
    public bool IsOfferable => Status is ModeStatus.Available;
}
