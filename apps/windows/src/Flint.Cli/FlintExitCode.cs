namespace Flint.Cli;

/// <summary>
/// What `flint` returns to whatever ran it.
/// </summary>
/// <remarks>
/// These are a contract, not an implementation detail: `tools/scripts/*.ps1`, CI steps and anything
/// piping this command branch on them, so a number that changes meaning breaks a caller silently.
/// They were previously written inline as bare integers at five call sites, where nothing said what
/// 3 meant or that 69 had been chosen deliberately.
///
/// Where a sensible <c>sysexits.h</c> value exists it is used, because operators already know them:
/// 64 for a bad command line, 69 for a service that is not available. The rest are Flint's own and
/// start at 2 to stay clear of the shell's conventional 1 for "general error".
/// </remarks>
internal static class FlintExitCode
{
    /// <summary>The command did what it was asked.</summary>
    public const int Success = 0;

    /// <summary>The command line could not be parsed. <c>sysexits.h</c> EX_USAGE.</summary>
    public const int UsageError = 64;

    /// <summary>
    /// A required service was not there. <c>sysexits.h</c> EX_UNAVAILABLE.
    /// </summary>
    /// <remarks>
    /// Used when the receiver answered but is not advertising a browser endpoint — a live device
    /// that cannot serve this request, which is different from one that never answered.
    /// </remarks>
    public const int ReceiverUnavailable = 69;

    /// <summary>The probe ran out of time before reaching a verdict.</summary>
    public const int ProbeTimedOut = 2;

    /// <summary>
    /// A mirror session ended having encoded no frames.
    /// </summary>
    /// <remarks>
    /// Distinct from a mirror that failed to start: this one reported success while sending
    /// nothing, which is the failure most worth catching from a script.
    /// </remarks>
    public const int MirrorProducedNoFrames = 3;

    /// <summary>This PC cannot mirror at all — no hardware encoder, or the engine refused.</summary>
    public const int MirrorUnsupported = 4;
}
