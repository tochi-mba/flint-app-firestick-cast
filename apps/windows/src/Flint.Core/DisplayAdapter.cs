namespace Flint.Core;

/// <summary>
/// A graphics adapter, and whether it drives any display.
/// </summary>
/// <param name="Luid">The adapter's locally unique identifier.</param>
/// <param name="Description">The adapter description string reported by DXGI.</param>
/// <param name="DrivesDisplay">Whether at least one output is attached to this adapter.</param>
/// <param name="IsSoftware">Whether this is WARP or another software renderer.</param>
public sealed record DisplayAdapter(
    long Luid,
    string Description,
    bool DrivesDisplay,
    bool IsSoftware = false);
