namespace Flint.Discovery;

/// <summary>The DNS record types Flint reads during service discovery.</summary>
public enum DnsRecordType : ushort
{
    /// <summary>An IPv4 address.</summary>
    A = 1,

    /// <summary>A pointer from a service type to a service instance.</summary>
    Ptr = 12,

    /// <summary>Free-form key-value attributes for a service instance.</summary>
    Txt = 16,

    /// <summary>The host and port a service instance lives on.</summary>
    Srv = 33,

    /// <summary>Any type. Used in queries.</summary>
    Any = 255,
}
