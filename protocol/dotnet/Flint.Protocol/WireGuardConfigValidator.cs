using System.Text.RegularExpressions;

namespace Flint.Protocol;

/// <summary>
/// Structural check for a WireGuard config text blob.
/// </summary>
/// <remarks>
/// This does not parse keys or endpoints; it only verifies that an <c>[Interface]</c> and a
/// <c>[Peer]</c> section are present so the wire layer can reject incomplete Set commands.
/// Config text must never be logged.
/// </remarks>
public static partial class WireGuardConfigValidator
{
    [GeneratedRegex(@"(?m)^\s*\[Interface\]\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex InterfaceHeader();

    [GeneratedRegex(@"(?m)^\s*\[Peer\]\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex PeerHeader();

    /// <summary>Whether <paramref name="configText"/> contains both required section headers.</summary>
    public static bool IsValid(string configText)
    {
        if (string.IsNullOrWhiteSpace(configText))
        {
            return false;
        }

        return InterfaceHeader().IsMatch(configText) && PeerHeader().IsMatch(configText);
    }
}
