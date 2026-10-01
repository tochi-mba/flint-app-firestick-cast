namespace Flint.Session;

/// <summary>Why a receiver session ended.</summary>
public enum CastSessionEnd
{
    /// <summary>This PC ended it: the person disconnected, or Flint closed the session itself.</summary>
    ClosedByThisPc = 0,

    /// <summary>The TV said goodbye: its app closed, restarted, or was asked to stop.</summary>
    EndedByTv = 1,

    /// <summary>The connection went away without a goodbye: Wi-Fi, power, or the network between.</summary>
    ConnectionLost = 2,

    /// <summary>The TV sent something this PC could not read, so the session could not go on.</summary>
    ProtocolError = 3,
}

/// <summary>How a receiver session ended.</summary>
/// <param name="Reason">Why it ended.</param>
/// <param name="Detail">
/// The TV's own words when it said goodbye with a reason; otherwise null. Shown to the person, never
/// logged: it is the TV's text, not Flint's.
/// </param>
public sealed record CastSessionClosed(CastSessionEnd Reason, string? Detail = null);
