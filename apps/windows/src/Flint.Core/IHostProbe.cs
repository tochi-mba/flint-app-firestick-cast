namespace Flint.Core;

/// <summary>Discovers what this Windows host can do.</summary>
public interface IHostProbe
{
    /// <summary>
    /// Enumerates adapters and capability-probes every encoder.
    /// </summary>
    /// <remarks>
    /// Implementations must probe rather than infer. A vendor string is not evidence that an
    /// encoder exists, and a present GPU is not evidence that it can encode a given codec.
    /// </remarks>
    Task<HostCapabilities> ProbeAsync(CancellationToken cancellationToken = default);
}
