using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Flint.Core;

namespace Flint.Engine.Interop;

/// <summary>The production implementation of the native one-shot capability boundary.</summary>
internal sealed partial class NativeEngineProbeApi : IEngineProbeApi
{
    internal const string LibraryName = "flint_engine";
    internal const string CompatibleEngineVersion = "0.1.0";
    internal const int MaxAdapters = 32;
    internal const int MaxEncoders = 32;
    internal const uint CodecMaskH264 = 1u << 0;
    internal const uint CodecMaskH265 = 1u << 1;
    internal const uint CodecMaskAv1 = 1u << 2;
    internal const byte CaptureBackendDesktopDuplication = 1;

    /// <inheritdoc />
    public bool TryProbe(out EngineProbeResult result)
    {
        result = EmptyResult();

        try
        {
            if (!HasCompatibleVersion())
            {
                return false;
            }

            var nativeAdapters = new NativeAdapter[MaxAdapters];
            var nativeEncoders = new NativeEncoder[MaxEncoders];
            uint adapterCount;
            uint encoderCount;
            int adapterStatus;
            int encoderStatus;

            unsafe
            {
                fixed (NativeAdapter* adapterPointer = nativeAdapters)
                fixed (NativeEncoder* encoderPointer = nativeEncoders)
                {
                    adapterStatus = NativeMethods.ProbeAdapters(
                        adapterPointer,
                        (nuint)nativeAdapters.Length,
                        &adapterCount);
                    encoderStatus = NativeMethods.ProbeEncoders(
                        encoderPointer,
                        (nuint)nativeEncoders.Length,
                        &encoderCount);
                }
            }

            if ((FlintStatus)adapterStatus is not FlintStatus.Ok
                || (FlintStatus)encoderStatus is not FlintStatus.Ok
                || adapterCount > nativeAdapters.Length
                || encoderCount > nativeEncoders.Length)
            {
                return false;
            }

            var adapters = ConvertAdapters(nativeAdapters, checked((int)adapterCount));
            var encoders = ConvertEncoders(nativeEncoders, checked((int)encoderCount));
            if (adapters is null || encoders is null)
            {
                return false;
            }

            var primary = FindPrimaryAdapterLuid(nativeAdapters, checked((int)adapterCount), adapters);
            result = new EngineProbeResult(adapters, encoders, primary, ProbeCaptureBackend());
            return true;
        }
        catch (Exception exception) when (exception is DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException
            or MarshalDirectiveException)
        {
            return false;
        }
    }

    internal static IReadOnlyList<DisplayAdapter>? ConvertAdapters(NativeAdapter[] source, int count)
    {
        if (count < 0 || count > source.Length)
        {
            return null;
        }

        var result = new List<DisplayAdapter>(count);
        for (var index = 0; index < count; index++)
        {
            var adapter = source[index].ToModel();
            if (adapter is null)
            {
                return null;
            }

            result.Add(adapter);
        }

        return result;
    }

    internal static IReadOnlyList<HostVideoEncoder>? ConvertEncoders(NativeEncoder[] source, int count)
    {
        if (count < 0 || count > source.Length)
        {
            return null;
        }

        var result = new List<HostVideoEncoder>(count);
        for (var index = 0; index < count; index++)
        {
            if (source[index].ToModel() is { } encoder)
            {
                result.Add(encoder);
            }
        }

        return result;
    }

    private static long FindPrimaryAdapterLuid(
        NativeAdapter[] native,
        int count,
        IReadOnlyList<DisplayAdapter> adapters)
    {
        for (var index = 0; index < count; index++)
        {
            if (native[index].DrivesPrimaryDisplay == 1 && native[index].IsSoftware == 0)
            {
                return native[index].AdapterLuid;
            }
        }

        return adapters.FirstOrDefault(adapter => adapter.DrivesDisplay && !adapter.IsSoftware)?.Luid
            ?? adapters.FirstOrDefault(adapter => !adapter.IsSoftware)?.Luid
            ?? 0;
    }

    private static unsafe bool HasCompatibleVersion()
    {
        Span<byte> buffer = stackalloc byte[32];
        fixed (byte* pointer = buffer)
        {
            if ((FlintStatus)NativeMethods.Version(pointer, (nuint)buffer.Length) is not FlintStatus.Ok)
            {
                return false;
            }
        }

        var terminator = buffer.IndexOf((byte)0);
        return terminator >= 0
            && Encoding.UTF8.GetString(buffer[..terminator]) == CompatibleEngineVersion;
    }

    private static EngineProbeResult EmptyResult() => new([], [], 0);

    /// <summary>
    /// Asks the engine whether it can capture the screen, and with which backend.
    /// </summary>
    /// <returns>
    /// The backend, or <see langword="null"/> when the engine could not open a capture. A failure
    /// here downgrades the report to "cannot capture" rather than propagating, because the rest of
    /// the probe is still worth showing.
    /// </returns>
    internal static unsafe CaptureApi? ProbeCaptureBackend()
    {
        try
        {
            NativeCapture capture;
            if (NativeMethods.ProbeCapture(&capture) != 0 || capture.Available == 0)
            {
                return null;
            }

            return capture.Backend switch
            {
                CaptureBackendDesktopDuplication => CaptureApi.DesktopDuplication,
                _ => null,
            };
        }
        catch (Exception exception) when (exception is DllNotFoundException
            or EntryPointNotFoundException or BadImageFormatException)
        {
            return null;
        }
    }

    private static partial class NativeMethods
    {
        [LibraryImport(LibraryName, EntryPoint = "flint_version")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static unsafe partial int Version(byte* buffer, nuint capacity);

        [LibraryImport(LibraryName, EntryPoint = "flint_probe_adapters")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static unsafe partial int ProbeAdapters(
            NativeAdapter* adapters,
            nuint capacity,
            uint* found);

        [LibraryImport(LibraryName, EntryPoint = "flint_probe_encoders")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static unsafe partial int ProbeEncoders(
            NativeEncoder* encoders,
            nuint capacity,
            uint* found);

        [LibraryImport(LibraryName, EntryPoint = "flint_probe_capture")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static unsafe partial int ProbeCapture(NativeCapture* capture);
    }
}
