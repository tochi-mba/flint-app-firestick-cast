using System.Runtime.InteropServices;

namespace Flint.Engine.Interop;

/// <summary>The version of the native engine this copy of Flint loaded.</summary>
/// <remarks>
/// For the About section and bug reports, where "which engine was it" is the first question. A
/// missing or mismatched engine answers null rather than throwing: that is itself the answer.
/// </remarks>
public static class EngineVersion
{
    /// <summary>The engine's own version, or null when it could not be loaded or asked.</summary>
    public static string? Read()
    {
        try
        {
            return NativeEngineProbeApi.ReadVersion();
        }
        catch (Exception exception) when (exception is DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException
            or MarshalDirectiveException)
        {
            return null;
        }
    }
}
