namespace Flint.Discovery;

/// <summary>What the television's package manager said when asked to install.</summary>
/// <param name="Succeeded">Whether it answered <c>Success</c>.</param>
/// <param name="Output">Its answer, verbatim and bounded, for the person to read when it did not.</param>
public sealed record AdbInstallOutcome(bool Succeeded, string Output)
{
    /// <summary>The longest answer worth relaying. A package manager that refuses says so in a line.</summary>
    public const int MaximumOutputLength = 480;

    /// <summary>Reads the package manager's answer.</summary>
    public static AdbInstallOutcome FromOutput(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var trimmed = output.Trim();
        if (trimmed.Length > MaximumOutputLength)
        {
            trimmed = trimmed[..MaximumOutputLength];
        }

        return new AdbInstallOutcome(trimmed.StartsWith("Success", StringComparison.Ordinal), trimmed);
    }
}
