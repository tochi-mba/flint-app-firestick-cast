using System.Net;

namespace Flint.Core;

/// <summary>Identifies a receiver address supplied explicitly by the user.</summary>
/// <remarks>
/// This path is essential on networks that route unicast traffic but suppress multicast discovery.
/// It identifies one user-named endpoint; it never broadens into a subnet scan.
/// </remarks>
public interface IAddressableDeviceProbe : IDeviceProbe
{
    /// <summary>Probes one address and either one port or the documented bounded ADB range.</summary>
    Task<FireTvDevice> ProbeAddressAsync(
        IPAddress address,
        int? port = null,
        CancellationToken cancellationToken = default);
}
