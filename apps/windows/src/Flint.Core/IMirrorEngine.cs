namespace Flint.Core;

/// <summary>
/// Starts capture-and-encode sessions.
/// </summary>
/// <remarks>
/// An interface so the mirror loop can be driven and asserted on without a GPU, an encoder, or a
/// television — none of which exist on a machine running the test suite.
/// </remarks>
public interface IMirrorEngine
{
    /// <summary>Starts a session, or reports why the host cannot.</summary>
    /// <param name="options">What the session should produce.</param>
    /// <exception cref="MirrorEngineException">
    /// The host has no capture or no usable encoder. This is a real answer about the machine, not
    /// a defect, so it carries the engine's own reason rather than a generic failure.
    /// </exception>
    IMirrorEngineSession Start(MirrorSessionOptions options);
}
