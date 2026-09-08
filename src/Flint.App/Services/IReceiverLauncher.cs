using Flint.Core;

namespace Flint.App.Services;

/// <summary>Opens the Flint receiver on a reachable Fire TV.</summary>
public interface IReceiverLauncher
{
    /// <summary>Brings the receiver activity to the foreground.</summary>
    Task LaunchAsync(FireTvDevice device, CancellationToken cancellationToken = default);
}