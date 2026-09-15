namespace Flint.Core;

/// <summary>
/// Chooses how to capture and encode, given what the host actually has.
/// </summary>
/// <remarks>
/// Desktop Duplication requires the capturing process to run on the adapter driving the captured
/// display. On hybrid laptops the desktop is commonly driven by the integrated GPU while the
/// discrete GPU holds the better encoder, so the naive pairing fails or silently falls back.
/// Windows Graphics Capture works across adapters and is therefore the safe default.
/// </remarks>
public static class CaptureStrategySelector
{
    /// <summary>
    /// Selects a strategy, or returns <see langword="null"/> when the host has no session-capable
    /// encoder at all.
    /// </summary>
    /// <param name="captureAdapterLuid">The adapter driving the display to be captured.</param>
    /// <param name="encoders">Encoders confirmed present by probing.</param>
    /// <param name="preferredCodec">The codec the receiver asked for.</param>
    public static CaptureStrategy? Select(
        long captureAdapterLuid,
        IReadOnlyList<HostVideoEncoder> encoders,
        VideoCodec preferredCodec)
    {
        ArgumentNullException.ThrowIfNull(encoders);

        var usable = encoders
            .Where(encoder => encoder.IsSessionCapable && encoder.Supports(preferredCodec))
            .ToList();
        if (usable.Count == 0)
        {
            return null;
        }

        // Same-adapter capture and encode avoids a cross-adapter copy, so it wins outright and
        // unlocks Desktop Duplication's cheaper path and dirty-rectangle metadata.
        var sameAdapter = usable.FirstOrDefault(encoder => encoder.AdapterLuid == captureAdapterLuid);
        if (sameAdapter is not null)
        {
            return new CaptureStrategy(
                CaptureApi.DesktopDuplication,
                sameAdapter,
                captureAdapterLuid,
                "Capture and encode share one adapter, so Desktop Duplication is used for its lower "
                    + "overhead and dirty-rectangle metadata.");
        }

        var best = usable.OrderBy(encoder => VendorRank(encoder.Vendor)).First();
        return new CaptureStrategy(
            CaptureApi.WindowsGraphicsCapture,
            best,
            captureAdapterLuid,
            "The display and the chosen encoder are on different adapters, so Windows Graphics "
                + "Capture is used because Desktop Duplication cannot cross adapters. Frames are "
                + "copied once between adapters.");
    }

    /// <summary>
    /// Preference order between encoder families when more than one could serve.
    /// </summary>
    /// <remarks>
    /// NVENC's dedicated encode silicon has the lowest and most consistent encode time of the
    /// three, which is what this workload optimises for.
    /// </remarks>
    private static int VendorRank(EncoderVendor vendor) => vendor switch
    {
        EncoderVendor.Nvenc => 0,
        EncoderVendor.Amf => 1,
        EncoderVendor.QuickSync => 2,
        EncoderVendor.Unknown => 3,
        _ => 4,
    };
}
