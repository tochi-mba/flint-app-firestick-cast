using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Flint.Core;

namespace Flint.Engine.Interop;

/// <summary>The displays the engine can capture, read through <c>flint_probe_outputs</c>.</summary>
/// <remarks>
/// The engine numbers displays with the same walk capture opens them by, so an index read here is
/// the index a share passes back in <see cref="MirrorSessionOptions.OutputIndex"/>.
/// </remarks>
public sealed partial class NativeDisplayOutputs : IDisplayOutputSource
{
    /// <summary>More displays than any PC Flint runs on drives at once.</summary>
    internal const int MaxOutputs = 16;

    private readonly Probe probe;

    /// <summary>Lists displays through the engine.</summary>
    public NativeDisplayOutputs()
        : this(CallEngine)
    {
    }

    /// <summary>Lists displays through a supplied call, for tests.</summary>
    internal NativeDisplayOutputs(Probe probe)
    {
        this.probe = probe;
    }

    /// <summary>Fills the buffer with displays and returns the engine's status and the true count.</summary>
    internal delegate int Probe(Span<NativeOutput> outputs, out uint found);

    /// <inheritdoc />
    public IReadOnlyList<DisplayOutput> ListOutputs()
    {
        var native = new NativeOutput[MaxOutputs];
        uint found;
        int status;
        try
        {
            status = probe(native, out found);
        }
        catch (Exception exception) when (exception is DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException
            or MarshalDirectiveException)
        {
            return [];
        }

        return (FlintStatus)status is FlintStatus.Ok ? Convert(native, found) : [];
    }

    /// <summary>
    /// The attached displays among the first <paramref name="found"/>, or none at all when any
    /// entry is malformed: a list that is partly wrong could point a share at the wrong screen.
    /// </summary>
    internal static IReadOnlyList<DisplayOutput> Convert(NativeOutput[] native, uint found)
    {
        var count = (int)Math.Min(found, (uint)native.Length);
        var displays = new List<DisplayOutput>(count);
        for (var position = 0; position < count; position++)
        {
            var output = native[position];
            if (output.Index != (uint)position || output.Attached > 1)
            {
                return [];
            }

            // A display that is not part of the desktop has nothing to capture, and Windows gives
            // it no place on the desktop to check.
            if (output.Attached == 0)
            {
                continue;
            }

            if (output.ToModel() is not { } display)
            {
                return [];
            }

            displays.Add(display);
        }

        return displays;
    }

    private static unsafe int CallEngine(Span<NativeOutput> outputs, out uint found)
    {
        uint count;
        int status;
        fixed (NativeOutput* pointer = outputs)
        {
            status = NativeMethods.ProbeOutputs(pointer, (nuint)outputs.Length, &count);
        }

        found = count;
        return status;
    }

    private static partial class NativeMethods
    {
        [LibraryImport(NativeEngineProbeApi.LibraryName, EntryPoint = "flint_probe_outputs")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static unsafe partial int ProbeOutputs(NativeOutput* outputs, nuint capacity, uint* found);
    }
}
