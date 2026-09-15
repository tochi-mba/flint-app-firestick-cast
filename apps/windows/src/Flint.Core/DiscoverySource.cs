namespace Flint.Core;

/// <summary>How a device came to Flint's attention.</summary>
public enum DiscoverySource
{
    /// <summary>Advertised over multicast DNS on the local link.</summary>
    MulticastDns = 0,

    /// <summary>Found by the bounded ADB port scan across the documented range.</summary>
    AdbPortScan = 1,

    /// <summary>Entered by the user. Always permitted; discovery is never the only route.</summary>
    Manual = 2,
}
