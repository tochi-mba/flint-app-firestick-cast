using System.Globalization;
using Flint.Core;

namespace Flint.App.ViewModels;

/// <summary>
/// Which receiver the Web page is talking about, and on which port.
/// </summary>
/// <remarks>
/// Separated from the page because it is the one part of verification that is pure arithmetic over
/// the Cast probe's result. Keeping it here lets the recovery path — typing the port the television
/// is showing when multicast is blocked — be read and tested on its own, rather than inside a method
/// that is also opening a TLS session.
/// </remarks>
internal static class BrowserReceiverSelection
{
    /// <summary>A typed browser port, or null when the field is empty or out of range.</summary>
    internal static int? ParsePort(string text) =>
        int.TryParse(text.Trim(), out var port) && port is >= 1 and <= 65_535 ? port : null;

    /// <summary>Formats an advertised port for the manual field.</summary>
    internal static string FormatPort(int port) => port.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The selected device, with a typed port folded in as discovery evidence.
    /// </summary>
    /// <remarks>
    /// Typing the port the television already shows is how Web recovers when multicast TXT is
    /// blocked and ADB never identified the platform. It supplies the endpoint and nothing more —
    /// the certificate pin is still compared, so a typed port cannot skip trust.
    /// </remarks>
    internal static FireTvDevice? Effective(FireTvDevice? device, int? manualPort)
    {
        if (device is null)
        {
            return null;
        }

        if (manualPort is not { } port)
        {
            return device;
        }

        return device with
        {
            BrowserEvidence = new BrowserReceiverEvidence(2, true, BrowserWebViewProbe.Passed)
            {
                SecureEndpointPort = port,
            },
        };
    }

    /// <summary>
    /// The port a receiver advertised, formatted for the manual field, or null when it advertised
    /// none.
    /// </summary>
    /// <remarks>
    /// The field is a stand-in for discovery. Filling it from evidence is what keeps it from being
    /// a step a person has to complete when the television already answered the question.
    /// </remarks>
    internal static string? AdvertisedPortText(FireTvDevice? device) =>
        device?.BrowserEvidence?.SecureEndpointPort is { } port ? FormatPort(port) : null;
}
