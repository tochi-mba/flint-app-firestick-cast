using Flint.Core;

namespace Flint.Discovery;

/// <summary>
/// The outcome of probing one address for ADB.
/// </summary>
/// <param name="State">What the probe proved.</param>
/// <param name="Port">The port that answered, or <see langword="null"/> when none did.</param>
/// <param name="Banner">The device's connect banner, present only when authorised.</param>
/// <param name="AndroidApiLevel">Read independently from the Android build properties.</param>
/// <param name="AndroidRelease">The underlying Android release string reported by the device.</param>
/// <param name="Model">The build model read from Android properties, when available.</param>
public sealed record AdbProbeResult(
    AdbConnectionState State,
    int? Port,
    AdbBanner? Banner,
    int? AndroidApiLevel = null,
    string? AndroidRelease = null,
    string? Model = null)
{
    /// <summary>Nothing on this address is speaking ADB.</summary>
    public static readonly AdbProbeResult NotFound =
        new(AdbConnectionState.Refused, Port: null, Banner: null);

    /// <summary>
    /// Whether the probe proved the device is Android-based.
    /// </summary>
    /// <remarks>
    /// An RSA prompt counts. The device answered in ADB, which Vega cannot do.
    /// </remarks>
    public bool ProvesAndroid =>
        State is AdbConnectionState.Connected or AdbConnectionState.Unauthorized;
}
