using Flint.Core;

namespace Flint.App.Services;

/// <summary>What a look at a share's sound found, for "TV only".</summary>
internal enum MuteNews
{
    /// <summary>Nothing to say.</summary>
    None = 0,

    /// <summary>The person turned this PC's sound back on; Flint leaves it on for the rest of the share.</summary>
    PersonUnmuted = 1,

    /// <summary>Muting this output silences what Flint hears from it, so it now plays on both.</summary>
    MuteSilencesCapture = 2,
}

/// <summary>How a mute left behind by an earlier Flint was dealt with at launch.</summary>
internal enum LeftOverMute
{
    /// <summary>None was left behind.</summary>
    None = 0,

    /// <summary>The output is back as it was before that share.</summary>
    Restored = 1,

    /// <summary>The output is no longer connected, so there was nothing to put back.</summary>
    OutputGone = 2,

    /// <summary>Windows would not; Flint tries again at the next launch.</summary>
    NotRestored = 3,
}

/// <summary>"TV only": this PC's output is muted while its sound is shared, then put back exactly as it was.</summary>
/// <remarks>
/// <para>
/// Only the mute is touched, never the level, and an output that was already muted stays muted.
/// What to put back is written down before muting, so a Flint that is killed mid-share puts it back
/// at the next launch.
/// </para>
/// <para>
/// Some outputs stop delivering anything to capture once they are muted. Windows' own meter is read
/// before the mute, so sound on the meter while capture hears silence for two seconds is that
/// case: the mute is undone, and that output plays on both from then on.
/// </para>
/// </remarks>
internal sealed class TvOnlyMute(IAudioEngine engine, ISoundMemory memory, TimeProvider time)
{
    /// <summary>How long capture must be silent under a sounding meter before muting is blamed.</summary>
    internal static readonly TimeSpan SilenceProof = TimeSpan.FromSeconds(2);

    /// <summary>A meter reading that means something audible is playing.</summary>
    internal const float Audible = 0.01f;

    /// <summary>A captured level that means capture hears nothing, as the engine judges silence.</summary>
    internal const float Silent = 0.001f;

    private string? device;
    private bool muted;
    private bool wasMuted;
    private bool leftAlone;
    private DateTimeOffset? silentSince;

    /// <summary>Whether Flint is holding this PC's output muted right now.</summary>
    public bool IsMuting => muted;

    /// <summary>Takes <paramref name="deviceId"/> for a share and mutes it.</summary>
    /// <returns>
    /// Whether it is muted: not when Windows would not, nor when muting it is known to silence
    /// capture.
    /// </returns>
    public bool Begin(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        End();
        device = deviceId;
        leftAlone = memory.MuteSilences(deviceId);
        return Mute();
    }

    /// <summary>Puts the output back while the share is paused.</summary>
    public void Suspend() => Restore();

    /// <summary>Mutes again as the share resumes, unless the person turned sound back on.</summary>
    /// <returns>Whether it is muted.</returns>
    public bool Resume() => Mute();

    /// <summary>Puts the output back at the end of the share, however it ended.</summary>
    public void End()
    {
        Restore();
        device = null;
        leftAlone = false;
    }

    /// <summary>Looks at the share's latest counters.</summary>
    public MuteNews Observe(AudioShareStats stats)
    {
        if (!muted)
        {
            return MuteNews.None;
        }

        if (engine.IsMuted(device) == false)
        {
            muted = false;
            leftAlone = true;
            memory.SetPendingRestore(null);
            return MuteNews.PersonUnmuted;
        }

        if (stats.Meter < Audible || stats.Level >= Silent)
        {
            silentSince = null;
            return MuteNews.None;
        }

        var now = time.GetUtcNow();
        silentSince ??= now;
        if (now - silentSince.Value < SilenceProof)
        {
            return MuteNews.None;
        }

        Restore();
        leftAlone = true;
        memory.RememberMuteSilences(device!);
        return MuteNews.MuteSilencesCapture;
    }

    /// <summary>Puts back a mute an earlier Flint left behind, at launch.</summary>
    public static LeftOverMute RestoreLeftOver(IAudioEngine engine, ISoundMemory memory)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(memory);
        if (memory.PendingRestore is not { } pending)
        {
            return LeftOverMute.None;
        }

        if (!engine.ListDevices().Any(output => string.Equals(output.Id, pending.DeviceId, StringComparison.OrdinalIgnoreCase)))
        {
            memory.SetPendingRestore(null);
            return LeftOverMute.OutputGone;
        }

        if (!engine.SetMuted(pending.DeviceId, pending.WasMuted))
        {
            return LeftOverMute.NotRestored;
        }

        memory.SetPendingRestore(null);
        return LeftOverMute.Restored;
    }

    private bool Mute()
    {
        if (muted || device is null || leftAlone)
        {
            return muted;
        }

        // A mute this run could not put back is still on record, and says how it was before Flint.
        var before = memory.PendingRestore is { } left && string.Equals(left.DeviceId, device, StringComparison.OrdinalIgnoreCase)
            ? left.WasMuted
            : engine.IsMuted(device);
        if (before is not { } wasBefore)
        {
            return false;
        }

        // Written down first: if Flint dies between these two lines, the next launch still knows.
        memory.SetPendingRestore(new MuteToRestore(device, wasBefore));
        if (!engine.SetMuted(device, true))
        {
            return false;
        }

        muted = true;
        wasMuted = wasBefore;
        silentSince = null;
        return true;
    }

    private void Restore()
    {
        if (!muted)
        {
            return;
        }

        muted = false;

        // Kept on record when Windows would not, so the next launch tries again.
        if (engine.SetMuted(device, wasMuted))
        {
            memory.SetPendingRestore(null);
        }
    }
}
