using Flint.Protocol;

namespace Flint.App.ViewModels;

/// <summary>What the Now Playing card can ask of the TV, over whatever session is live.</summary>
/// <remarks>
/// Implemented by the Cast page, which owns the session. The card never holds the session itself,
/// so it cannot outlive it or send on one that has been replaced.
/// </remarks>
public interface IMediaRemote
{
    /// <summary>Plays, pauses or seeks the TV's player.</summary>
    Task SendTransportAsync(TransportAction action, long positionMs = -1);

    /// <summary>Sets the TV's volume, from 0 to 1.</summary>
    Task SetVolumeAsync(float level);

    /// <summary>Stops what is playing and returns the TV to its idle screen.</summary>
    Task StopAsync();

    /// <summary>Abandons the file being sent and tells the TV to drop what it has of it.</summary>
    Task CancelSendAsync();

    /// <summary>Sends the last file again, after the TV could not play it.</summary>
    Task TryAgainAsync();
}
