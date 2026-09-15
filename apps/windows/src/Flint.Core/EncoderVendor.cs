namespace Flint.Core;

/// <summary>The hardware encoder family backing a <see cref="HostVideoEncoder"/>.</summary>
public enum EncoderVendor
{
    /// <summary>NVIDIA NVENC.</summary>
    Nvenc = 0,

    /// <summary>AMD Advanced Media Framework.</summary>
    Amf = 1,

    /// <summary>Intel Quick Sync Video.</summary>
    QuickSync = 2,

    /// <summary>
    /// A software encoder. Present so the pipeline can be exercised, never selected for a session:
    /// software encoding cannot meet the latency budget.
    /// </summary>
    Software = 3,

    /// <summary>
    /// A hardware encoder whose registered name Flint does not recognise.
    /// </summary>
    /// <remarks>
    /// Still usable — the platform proved it exists, and only the vendor label is missing. Ranked
    /// last when choosing between encoders, because Flint cannot reason about its characteristics.
    /// Never folded into a named vendor: claiming an unknown encoder is Quick Sync would put a
    /// guess into the capture strategy and, worse, into the diagnostics the user reads.
    /// </remarks>
    Unknown = 4,
}
