using System.Text.Json;
using System.Text.Json.Nodes;
using Flint.Core;

namespace Flint.Cli;

/// <summary>
/// The probe verdict as JSON, for anything that is not a person reading a terminal.
/// </summary>
/// <remarks>
/// Written by hand rather than serialised straight off <see cref="CapabilityReport"/> on purpose.
/// Reflecting over the record would make every internal rename a breaking change for whatever is
/// parsing this, and would leak fields that exist for the UI rather than for a caller. The shape
/// here is a deliberate, stable contract; the records behind it stay free to move.
///
/// Enums are written as their names, not their numbers: a script asserting <c>"Available"</c> keeps
/// working when a value is inserted above it, and a log is readable a year later.
/// </remarks>
internal static class CapabilityReportJson
{
    /// <summary>Renders the whole verdict, indented so a failing CI log stays readable.</summary>
    internal static string Render(CapabilityReport report, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(report);

        var root = new JsonObject
        {
            ["schema"] = 1,
            ["probedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["elapsedSeconds"] = Math.Round(elapsed.TotalSeconds, 2),
            ["host"] = Host(report.Host),
            ["device"] = Device(report.Device),
            ["path"] = Path(report.Path),
            ["browser"] = Browser(report.Browser),
            ["verdicts"] = Verdicts(report.Verdicts),
        };

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject Host(HostCapabilities host) => new()
    {
        ["windowsBuild"] = host.WindowsBuild,
        ["canCaptureScreen"] = host.CanCaptureScreen,
        // Distinguishes "no encoder" from "not looked yet". Collapsing them would let a script
        // report a machine as incapable when the probe simply never ran.
        ["encodersProbed"] = host.EncodersProbed,
        ["encoderCount"] = host.Encoders.Count,
        ["adapterCount"] = host.Adapters.Count,
        ["screenCaptureBackend"] = host.ScreenCaptureBackend?.ToString(),
    };

    private static JsonNode? Device(FireTvDevice? device) => device is null
        ? null
        : new JsonObject
        {
            ["address"] = device.Address.ToString(),
            ["friendlyName"] = device.FriendlyName,
            ["discoverySource"] = device.Source.ToString(),
            ["adbPort"] = device.AdbPort,
            ["adbState"] = device.AdbState.ToString(),
            ["browserPort"] = device.BrowserEvidence?.SecureEndpointPort,
        };

    private static JsonNode? Path(NetworkPath? path) => path is null
        ? null
        : new JsonObject
        {
            ["roundTripMs"] = Math.Round(path.RoundTripMs, 2),
            ["jitterMs"] = Math.Round(path.JitterMs, 2),
            ["throughputMbps"] = Math.Round(path.ThroughputMbps, 2),
            ["packetLossPercent"] = Math.Round(path.PacketLossPercent, 2),
            // A throughput of 0 that was never measured is not a slow network.
            ["throughputMeasured"] = path.ThroughputMeasured,
        };

    private static JsonObject Browser(BrowserCapability browser) => new()
    {
        ["availability"] = browser.Availability.ToString(),
        ["isEligible"] = browser.IsEligible,
        ["reason"] = browser.Reason,
        ["remedy"] = browser.Remedy,
    };

    private static JsonArray Verdicts(IReadOnlyList<ModeVerdict> verdicts)
    {
        var array = new JsonArray();
        foreach (var verdict in verdicts)
        {
            array.Add(new JsonObject
            {
                ["mode"] = verdict.Mode.ToString(),
                ["status"] = verdict.Status.ToString(),
                ["isOfferable"] = verdict.IsOfferable,
                ["reason"] = verdict.Reason,
                ["remedy"] = verdict.Remedy,
            });
        }

        return array;
    }
}
