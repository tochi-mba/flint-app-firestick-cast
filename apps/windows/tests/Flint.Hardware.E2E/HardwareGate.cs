namespace Flint.Hardware.E2E;

/// <summary>
/// Opt-in gate for physical Fire TV runs. Default <c>dotnet test</c> skips every hardware flow.
/// </summary>
public static class HardwareGate
{
    public const string EnableVariable = "FLINT_HARDWARE_E2E";
    public const string AcknowledgeVariable = "FLINT_ACKNOWLEDGE_PHYSICAL_DEVICE";
    public const string SerialVariable = "FLINT_HARDWARE_SERIAL";
    public const string BrowseUrlVariable = "FLINT_HARDWARE_BROWSE_URL";
    public const string BrowseExpectedTextVariable = "FLINT_HARDWARE_BROWSE_EXPECTED_TEXT";
    public const string StepPauseMsVariable = "FLINT_HARDWARE_E2E_STEP_MS";

    public static bool IsEnabled =>
        IsTruthy(Environment.GetEnvironmentVariable(EnableVariable))
        && IsTruthy(Environment.GetEnvironmentVariable(AcknowledgeVariable));

    public static string? Serial =>
        Environment.GetEnvironmentVariable(SerialVariable)?.Trim() is { Length: > 0 } serial
            ? serial
            : null;

    public static string BrowseUrl =>
        Environment.GetEnvironmentVariable(BrowseUrlVariable)?.Trim() is { Length: > 0 } url
            ? url
            : "https://example.com/";

    /// <summary>A custom fixture must declare its own assertion, never silently reuse Example Domain.</summary>
    public static string BrowseExpectedText => ResolveExpectedPageText(BrowseUrl,
        Environment.GetEnvironmentVariable(BrowseExpectedTextVariable));

    internal static string ResolveExpectedPageText(string url, string? expected)
    {
        if (!string.IsNullOrWhiteSpace(expected))
        {
            if (expected.Length > 1024) throw new ArgumentException("Expected fixture text must be bounded.");
            return expected.Trim();
        }
        if (url is "https://example.com/" or "https://example.com") return "Example Domain";
        throw new InvalidOperationException($"Set {BrowseExpectedTextVariable} when using a custom browse fixture.");
    }

    /// <summary>
    /// Pause between UI steps so a headed run is watchable. Default 1.5s; set 0 to go as fast as
    /// the TV answers.
    /// </summary>
    public static TimeSpan StepPause
    {
        get
        {
            var raw = Environment.GetEnvironmentVariable(StepPauseMsVariable);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return TimeSpan.FromMilliseconds(1500);
            }

            return int.TryParse(raw, out var ms) && ms >= 0
                ? TimeSpan.FromMilliseconds(ms)
                : TimeSpan.FromMilliseconds(1500);
        }
    }

    public static string SkipReason =>
        $"Opt-in hardware E2E. Set {EnableVariable}=1, {AcknowledgeVariable}=1, and {SerialVariable}=<adb-serial>.";

    private static bool IsTruthy(string? value) =>
        value is "1" or "true" or "TRUE" or "yes" or "YES";
}
