using Flint.App.Controls;
using Flint.Core;

namespace Flint.App.ViewModels;

/// <summary>
/// The words the Cast and Diagnostics pages show about the television, this PC and the network.
/// </summary>
/// <remarks>
/// Read-only text derived from the capability report, kept apart from the page's behaviour.
/// </remarks>
public sealed partial class CastPageViewModel
{
    /// <summary>Whether a report exists to display.</summary>
    public bool HasReport => Report is not null;

    /// <summary>Whether to show the pre-probe empty state.</summary>
    public bool ShowEmptyState => Report is null && !IsProbing && Failure is null;

    /// <summary>The device name, or a placeholder when none was found.</summary>
    public string DeviceName => Report?.Device?.FriendlyName ?? "No receiver found";

    /// <summary>The device's platform, spelled out.</summary>
    public string DevicePlatform =>
        Report?.Device?.Platform.ToDisplayLabel() ?? "-";

    /// <summary>The device address and port, when known.</summary>
    public string DeviceAddress => Report?.Device is { } device
        ? device.AdbPort is { } port ? $"{device.Address}:{port}" : device.Address.ToString()
        : "-";

    /// <summary>The receiver model exactly as reported, without guessing from its name.</summary>
    public string DeviceModel => Report?.Device?.Model ?? "Not reported";

    /// <summary>The result of the ADB identity exchange.</summary>
    public string AdbStatus => Report?.Device?.AdbState.ToString() ?? "Not probed";

    /// <summary>The Android API level read from the receiver.</summary>
    public string AndroidApiLevel => Report?.Device?.AndroidApiLevel is { } level
        ? level.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : "Not reported";

    /// <summary>The underlying Android release read from the receiver.</summary>
    public string AndroidRelease => Report?.Device?.AndroidRelease ?? "Not reported";

    /// <summary>The Windows build used for platform gates.</summary>
    public string WindowsBuild => Report is { } report
        ? report.Host.WindowsBuild.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : "-";

    /// <summary>Whether the native engine actually answered the encoder query.</summary>
    public string EncoderProbeStatus => Report?.Host.EncodersProbed switch
    {
        true => "Complete",
        false => "Engine unavailable or incompatible",
        null => "Not probed",
    };

    /// <summary>DXGI adapters and their desktop role.</summary>
    public string GraphicsAdapters => Report is { Host.Adapters.Count: > 0 } report
        ? string.Join(
            Environment.NewLine,
            report.Host.Adapters.Select(adapter =>
                adapter.Description
                    + (adapter.IsSoftware ? " (software)" : adapter.DrivesDisplay ? " (display)" : string.Empty)))
        : "None reported";

    /// <summary>The adapter owning the Windows primary display.</summary>
    public string PrimaryDisplayAdapter => Report is { } report
        ? report.Host.Adapters
            .FirstOrDefault(adapter => adapter.Luid == report.Host.PrimaryDisplayAdapterLuid)
            ?.Description ?? "Not identified"
        : "-";

    /// <summary>Hardware encoders and the exact codec set each one advertised.</summary>
    public string HardwareEncoders => Report switch
    {
        null => "-",
        { Host.EncodersProbed: false } => "Not probed",
        { Host.Encoders.Count: 0 } => "None found",
        { } report => string.Join(
            Environment.NewLine,
            report.Host.Encoders.Select(encoder =>
                $"{encoder.Vendor}: {string.Join(", ", encoder.Codecs.Order())}")),
    };

    /// <summary>Measured round-trip time.</summary>
    public string RoundTrip => Report?.Path is { } path ? $"{path.RoundTripMs:F1} ms" : "Not measured";

    /// <summary>Measured round-trip variation.</summary>
    public string Jitter => Report?.Path is { } path ? $"{path.JitterMs:F1} ms" : "Not measured";

    /// <summary>Measured or explicitly unavailable throughput.</summary>
    public string Throughput => Report?.Path?.ThroughputLabel ?? "Not measured";

    /// <summary>Observed connect-sample loss.</summary>
    public string PacketLoss => Report?.Path is { } path ? $"{path.PacketLossPercent:F0}%" : "Not measured";

    /// <summary>The status word for the page heading pill.</summary>
    public string HeadingStatus => (IsProbing, Report) switch
    {
        (true, _) => "Probing",
        (false, null) => "Not probed",
        (false, { Device: { IsReachable: false } }) => "Connect TV",
        (false, not null) => Report!.HasAnyAvailableMode ? "Ready" : "Limited",
    };

    /// <summary>The heading pill's colour.</summary>
    public Tone HeadingTone => Report?.HasAnyAvailableMode == true ? Tone.Signal : Tone.Neutral;
}
