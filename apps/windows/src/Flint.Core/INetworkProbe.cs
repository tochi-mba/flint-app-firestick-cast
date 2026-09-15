namespace Flint.Core;

/// <summary>Measures the network path to a receiver.</summary>
public interface INetworkProbe
{
    /// <summary>
    /// Measures round-trip time, jitter, throughput and loss.
    /// </summary>
    /// <returns>
    /// The measured path, or <see langword="null"/> when measurement failed. Never a value derived
    /// from link speed or radio band: those are proxies, and this type only carries measurements.
    /// </returns>
    Task<NetworkPath?> MeasureAsync(FireTvDevice device, CancellationToken cancellationToken = default);
}
