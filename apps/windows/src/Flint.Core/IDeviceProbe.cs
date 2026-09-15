namespace Flint.Core;

/// <summary>Finds Fire TV devices on the local network and determines what they run.</summary>
public interface IDeviceProbe
{
    /// <summary>
    /// Discovers devices and probes each one's platform.
    /// </summary>
    /// <remarks>
    /// A device that cannot be identified must be returned with
    /// <see cref="FireTvPlatform.Unknown"/> rather than omitted. Knowing that something is there
    /// but unidentifiable is a useful answer; silence is not.
    /// </remarks>
    Task<IReadOnlyList<FireTvDevice>> DiscoverAsync(CancellationToken cancellationToken = default);
}
