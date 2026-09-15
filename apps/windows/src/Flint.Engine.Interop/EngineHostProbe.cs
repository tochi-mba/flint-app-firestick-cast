using System.Runtime.Versioning;
using Flint.Core;

namespace Flint.Engine.Interop;

/// <summary>
/// Combines the managed Windows probe with the engine's authoritative DXGI and encoder inventory.
/// </summary>
/// <remarks>
/// The managed probe supplies the Windows build and is also the honest fallback when the native
/// library is absent or incompatible. When the engine answers, both adapters and encoders are
/// replaced together so every value uses the same real DXGI LUID namespace.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class EngineHostProbe : IHostProbe
{
    private readonly IHostProbe _fallback;
    private readonly IEngineProbeApi _engine;

    /// <summary>Creates a probe backed by the native Flint engine.</summary>
    public EngineHostProbe(IHostProbe fallback)
        : this(fallback, new NativeEngineProbeApi())
    {
    }

    /// <summary>Creates a probe with an injectable engine boundary for hardware-free tests.</summary>
    public EngineHostProbe(IHostProbe fallback, IEngineProbeApi engine)
    {
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    /// <inheritdoc />
    public async Task<HostCapabilities> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var fallback = await _fallback.ProbeAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_engine.TryProbe(out var inventory))
        {
            return fallback;
        }

        return fallback with
        {
            Adapters = inventory.Adapters,
            Encoders = inventory.Encoders,
            PrimaryDisplayAdapterLuid = inventory.PrimaryDisplayAdapterLuid,
            EncodersProbed = true,
            ScreenCaptureBackend = inventory.ScreenCaptureBackend,
        };
    }
}

/// <summary>The complete native answer used to replace placeholder managed adapter identities.</summary>
/// <param name="Adapters">Every graphics adapter the engine enumerated.</param>
/// <param name="Encoders">Every hardware encoder the engine confirmed.</param>
/// <param name="PrimaryDisplayAdapterLuid">The adapter driving the primary display.</param>
/// <param name="ScreenCaptureBackend">
/// The capture backend the engine opened, or <see langword="null"/> when it could not open one.
/// Proven by opening a capture rather than inferred, so a host where capture is blocked reports
/// honestly instead of optimistically.
/// </param>
public sealed record EngineProbeResult(
    IReadOnlyList<DisplayAdapter> Adapters,
    IReadOnlyList<HostVideoEncoder> Encoders,
    long PrimaryDisplayAdapterLuid,
    CaptureApi? ScreenCaptureBackend = null);

/// <summary>A testable abstraction over the one-shot native capability calls.</summary>
public interface IEngineProbeApi
{
    /// <summary>Attempts a complete, version-compatible probe.</summary>
    bool TryProbe(out EngineProbeResult result);
}
