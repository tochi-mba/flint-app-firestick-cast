using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Flint.Engine.Interop;

/// <summary>The engine's sound calls, as the wrapper makes them.</summary>
/// <remarks>
/// A seam rather than a convenience: it lets tests stand in for an engine that refuses, answers
/// oddly or is missing altogether, which a real engine on a working PC never does. Each method
/// returns the engine's status code. An empty device identity means the default output.
/// </remarks>
internal interface IAudioExports
{
    int Devices(Span<NativeAudioDevice> devices, out uint found);

    int Start(string deviceId, uint bitrateKbps, long startOffsetUs, uint delayMs, out nint handle, out uint failure);

    int Config(nint handle, Span<byte> buffer, out uint length);

    int Next(nint handle, Span<byte> buffer, uint timeoutMs, out uint length, out long timeUs);

    int Stats(nint handle, out NativeAudioStats stats);

    int Problem(nint handle, Span<byte> buffer, out uint length);

    int SetPaused(nint handle, bool paused);

    int SetDelay(nint handle, uint delayMs);

    int Stop(nint handle);

    int Muted(string deviceId, out bool muted);

    int SetMuted(string deviceId, bool muted);
}

/// <summary>The engine's own sound exports.</summary>
internal sealed partial class EngineAudioExports : IAudioExports
{
    /// <summary>The one instance; the exports hold no state.</summary>
    internal static EngineAudioExports Instance { get; } = new();

    public unsafe int Devices(Span<NativeAudioDevice> devices, out uint found)
    {
        uint count;
        int status;
        fixed (NativeAudioDevice* pointer = devices)
        {
            status = Native.Devices(pointer, (nuint)devices.Length, &count);
        }

        found = count;
        return status;
    }

    public unsafe int Start(string deviceId, uint bitrateKbps, long startOffsetUs, uint delayMs, out nint handle, out uint failure)
    {
        nint started = 0;
        uint why;
        int status;
        // A pinned empty span is a null pointer, which is how the engine is told "the default output".
        fixed (char* id = deviceId.AsSpan())
        {
            var config = new NativeAudioConfig
            {
                DeviceId = (nint)id,
                DeviceIdLength = (uint)deviceId.Length,
                BitrateKbps = bitrateKbps,
                StartOffsetUs = startOffsetUs,
                DelayMs = delayMs,
            };
            status = Native.Start(&config, &started, &why);
        }

        handle = started;
        failure = why;
        return status;
    }

    public unsafe int Config(nint handle, Span<byte> buffer, out uint length)
    {
        uint written;
        int status;
        fixed (byte* pointer = buffer)
        {
            status = Native.Config(handle, pointer, (uint)buffer.Length, &written);
        }

        length = written;
        return status;
    }

    public unsafe int Next(nint handle, Span<byte> buffer, uint timeoutMs, out uint length, out long timeUs)
    {
        uint written;
        long time;
        int status;
        fixed (byte* pointer = buffer)
        {
            status = Native.Next(handle, pointer, (uint)buffer.Length, timeoutMs, &written, &time);
        }

        length = written;
        timeUs = time;
        return status;
    }

    public unsafe int Stats(nint handle, out NativeAudioStats stats)
    {
        NativeAudioStats read;
        var status = Native.Stats(handle, &read);
        stats = read;
        return status;
    }

    public unsafe int Problem(nint handle, Span<byte> buffer, out uint length)
    {
        uint written;
        int status;
        fixed (byte* pointer = buffer)
        {
            status = Native.Problem(handle, pointer, (uint)buffer.Length, &written);
        }

        length = written;
        return status;
    }

    public int SetPaused(nint handle, bool paused) => Native.SetPaused(handle, paused ? (byte)1 : (byte)0);

    public int SetDelay(nint handle, uint delayMs) => Native.SetDelay(handle, delayMs);

    public int Stop(nint handle) => Native.Stop(handle);

    public unsafe int Muted(string deviceId, out bool muted)
    {
        byte read;
        int status;
        fixed (char* id = deviceId.AsSpan())
        {
            status = Native.Muted(id, (uint)deviceId.Length, &read);
        }

        muted = read == 1;
        return status;
    }

    public unsafe int SetMuted(string deviceId, bool muted)
    {
        fixed (char* id = deviceId.AsSpan())
        {
            return Native.SetMuted(id, (uint)deviceId.Length, muted ? (byte)1 : (byte)0);
        }
    }

    private static partial class Native
    {
        [LibraryImport(NativeEngineProbeApi.LibraryName, EntryPoint = "flint_audio_devices")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static unsafe partial int Devices(NativeAudioDevice* devices, nuint capacity, uint* found);

        [LibraryImport(NativeEngineProbeApi.LibraryName, EntryPoint = "flint_audio_start")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static unsafe partial int Start(NativeAudioConfig* config, nint* handle, uint* failure);

        [LibraryImport(NativeEngineProbeApi.LibraryName, EntryPoint = "flint_audio_config")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static unsafe partial int Config(nint handle, byte* buffer, uint capacity, uint* length);

        [LibraryImport(NativeEngineProbeApi.LibraryName, EntryPoint = "flint_audio_next")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static unsafe partial int Next(nint handle, byte* buffer, uint capacity, uint timeoutMs, uint* length, long* timeUs);

        [LibraryImport(NativeEngineProbeApi.LibraryName, EntryPoint = "flint_audio_stats")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static unsafe partial int Stats(nint handle, NativeAudioStats* stats);

        [LibraryImport(NativeEngineProbeApi.LibraryName, EntryPoint = "flint_audio_problem")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static unsafe partial int Problem(nint handle, byte* buffer, uint capacity, uint* length);

        [LibraryImport(NativeEngineProbeApi.LibraryName, EntryPoint = "flint_audio_set_paused")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial int SetPaused(nint handle, byte paused);

        [LibraryImport(NativeEngineProbeApi.LibraryName, EntryPoint = "flint_audio_set_delay")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial int SetDelay(nint handle, uint delayMs);

        [LibraryImport(NativeEngineProbeApi.LibraryName, EntryPoint = "flint_audio_stop")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial int Stop(nint handle);

        [LibraryImport(NativeEngineProbeApi.LibraryName, EntryPoint = "flint_audio_muted")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static unsafe partial int Muted(char* deviceId, uint deviceIdLength, byte* muted);

        [LibraryImport(NativeEngineProbeApi.LibraryName, EntryPoint = "flint_audio_set_muted")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static unsafe partial int SetMuted(char* deviceId, uint deviceIdLength, byte muted);
    }
}
