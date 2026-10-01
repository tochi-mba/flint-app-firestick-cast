using System.Globalization;
using System.Text.Json.Serialization;

namespace Flint.Core;

/// <summary>A previously used Fire TV endpoint.</summary>
public sealed record RecentAddress(string Address, int? Port)
{
    /// <summary>How the endpoint is shown to a person: the address, and its port when one was given.</summary>
    [JsonIgnore]
    public string Label => Port is { } port ? $"{Address}:{port.ToString(CultureInfo.InvariantCulture)}" : Address;
}
