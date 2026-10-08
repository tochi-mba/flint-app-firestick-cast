using System.Runtime.InteropServices;
using Flint.Core;

namespace Flint.Engine.Interop;

/// <summary>The production <see cref="IAudioEngine"/>, over the engine's sound exports.</summary>
/// <remarks>
/// An engine without them is "sound unavailable", never a broken picture: the mirror's own export
/// check does not include these calls.
/// </remarks>
public sealed class NativeAudioEngine : IAudioEngine
{
    /// <summary>More outputs than any PC has plugged in at once.</summary>
    internal const int MaxDevices = 32;

    private readonly IAudioExports exports;

    /// <summary>Creates the engine over the engine's own exports.</summary>
    public NativeAudioEngine()
        : this(EngineAudioExports.Instance)
    {
    }

    /// <summary>Creates the engine over <paramref name="exports"/>, for tests.</summary>
    internal NativeAudioEngine(IAudioExports exports)
    {
        this.exports = exports;
    }

    /// <inheritdoc />
    public IReadOnlyList<AudioDevice> ListDevices()
    {
        var native = new NativeAudioDevice[MaxDevices];
        try
        {
            var status = exports.Devices(native, out var found);
            return (FlintStatus)status is FlintStatus.Ok ? ToDevices(native, found) : [];
        }
        catch (Exception exception) when (IsMissingEngine(exception))
        {
            return [];
        }
    }

    /// <inheritdoc />
    public IAudioEngineSession Start(AudioShareOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        int status;
        nint handle;
        uint failure;
        try
        {
            status = exports.Start(
                options.DeviceId ?? string.Empty,
                (uint)options.BitrateKbps,
                options.StartOffsetUs,
                (uint)Math.Max(0, options.DelayMilliseconds),
                out handle,
                out failure);
        }
        catch (Exception exception) when (IsMissingEngine(exception))
        {
            throw new AudioEngineException(
                DescribeFailure(AudioStartFailure.EngineMissing),
                AudioStartFailure.EngineMissing,
                exception);
        }

        if ((FlintStatus)status is not FlintStatus.Ok || handle == 0)
        {
            var reason = ToFailure(failure);
            throw new AudioEngineException(DescribeFailure(reason), reason);
        }

        return new NativeAudioSession(exports, handle);
    }

    /// <inheritdoc />
    public bool? IsMuted(string? deviceId)
    {
        try
        {
            return (FlintStatus)exports.Muted(deviceId ?? string.Empty, out var muted) is FlintStatus.Ok ? muted : null;
        }
        catch (Exception exception) when (IsMissingEngine(exception))
        {
            return null;
        }
    }

    /// <inheritdoc />
    public bool SetMuted(string? deviceId, bool muted)
    {
        try
        {
            return (FlintStatus)exports.SetMuted(deviceId ?? string.Empty, muted) is FlintStatus.Ok;
        }
        catch (Exception exception) when (IsMissingEngine(exception))
        {
            return false;
        }
    }

    /// <summary>The outputs among the first <paramref name="found"/>, leaving out any the engine described badly.</summary>
    internal static IReadOnlyList<AudioDevice> ToDevices(NativeAudioDevice[] native, uint found)
    {
        var count = (int)Math.Min(found, (uint)native.Length);
        var devices = new List<AudioDevice>(count);
        for (var index = 0; index < count; index++)
        {
            if (native[index].ToModel() is { } device)
            {
                devices.Add(device);
            }
        }

        return devices;
    }

    /// <summary>The failure an engine code stands for.</summary>
    internal static AudioStartFailure ToFailure(uint code) => code switch
    {
        1 => AudioStartFailure.NoEncoder,
        2 => AudioStartFailure.UnsupportedBitrate,
        3 => AudioStartFailure.NoOutput,
        4 => AudioStartFailure.DeviceMissing,
        _ => AudioStartFailure.Platform,
    };

    /// <summary>What to tell the person, by why sound could not start.</summary>
    public static string DescribeFailure(AudioStartFailure reason) => reason switch
    {
        AudioStartFailure.NoEncoder =>
            "This edition of Windows has no AAC encoder. Installing the Media Feature Pack from Windows' optional features adds one.",
        AudioStartFailure.UnsupportedBitrate => "That sound quality is not one Windows offers.",
        AudioStartFailure.NoOutput => "This PC has no sound output to share.",
        AudioStartFailure.DeviceMissing => "The chosen sound output is not connected.",
        AudioStartFailure.EngineMissing => "This build of Flint cannot share sound.",
        _ => "Windows would not share this PC's sound.",
    };

    /// <summary>The state an engine code stands for; anything unknown is a problem.</summary>
    internal static AudioShareState ToState(uint code) => code switch
    {
        1 => AudioShareState.Ready,
        2 => AudioShareState.Sounding,
        _ => AudioShareState.Unavailable,
    };

    /// <summary>A level from the engine, kept between 0 and 1 whatever arrives.</summary>
    internal static float ToUnit(float level) => float.IsFinite(level) ? Math.Clamp(level, 0, 1) : 0;

    private static bool IsMissingEngine(Exception exception) =>
        exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or MarshalDirectiveException;

    /// <summary>One running native sound share.</summary>
    private sealed class NativeAudioSession : IAudioEngineSession
    {
        /// <summary>Room for any problem the engine describes; a longer one is cut short.</summary>
        private const int ProblemBytes = 512;

        private readonly IAudioExports exports;
        private nint handle;

        internal NativeAudioSession(IAudioExports exports, nint handle)
        {
            this.exports = exports;
            this.handle = handle;
            try
            {
                Config = ReadConfig();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public IReadOnlyList<byte> Config { get; }

        public int Next(Span<byte> buffer, TimeSpan timeout, out long presentationTimeUs)
        {
            ObjectDisposedException.ThrowIf(handle == 0, this);
            var status = (FlintStatus)exports.Next(handle, buffer, (uint)timeout.TotalMilliseconds, out var length, out var time);
            if (status is FlintStatus.BufferTooSmall)
            {
                throw new MirrorEngineException($"A sound packet of {length} bytes did not fit a {buffer.Length}-byte buffer.");
            }

            if (status is not FlintStatus.Ok)
            {
                throw new MirrorEngineException($"The Flint engine failed while reading sound (status {(int)status}).");
            }

            presentationTimeUs = time;
            return (int)length;
        }

        public AudioShareStats ReadStats()
        {
            ObjectDisposedException.ThrowIf(handle == 0, this);
            _ = exports.Stats(handle, out var native);
            return new AudioShareStats(
                (long)Math.Min(native.Packets, long.MaxValue),
                (long)Math.Min(native.Dropped, long.MaxValue),
                ToUnit(native.Level),
                ToState(native.State),
                ToUnit(native.Meter));
        }

        public string? ReadProblem()
        {
            ObjectDisposedException.ThrowIf(handle == 0, this);
            Span<byte> text = stackalloc byte[ProblemBytes];
            _ = exports.Problem(handle, text, out var length);
            var used = (int)Math.Min(length, (uint)text.Length);
            return used == 0 ? null : System.Text.Encoding.UTF8.GetString(text[..used]);
        }

        public void SetPaused(bool paused)
        {
            ObjectDisposedException.ThrowIf(handle == 0, this);
            _ = exports.SetPaused(handle, paused);
        }

        public void SetDelay(int milliseconds)
        {
            ObjectDisposedException.ThrowIf(handle == 0, this);
            _ = exports.SetDelay(handle, (uint)Math.Max(0, milliseconds));
        }

        public void Dispose()
        {
            var owned = handle;
            handle = 0;
            if (owned != 0)
            {
                _ = exports.Stop(owned);
            }
        }

        private byte[] ReadConfig()
        {
            Span<byte> config = stackalloc byte[16];
            var status = (FlintStatus)exports.Config(handle, config, out var length);
            if (status is not FlintStatus.Ok || length is 0 or > 16)
            {
                throw new AudioEngineException("The Flint engine started sound without its setup data.", AudioStartFailure.Platform);
            }

            return config[..(int)length].ToArray();
        }
    }
}
