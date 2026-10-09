using Flint.Core;
using Flint.Session;

namespace Flint.Cli;

/// <summary>Shares this screen, and its sound, from the command line until stopped.</summary>
/// <remarks>
/// This is the path that proves the host half of a share on real hardware, so it prints what the
/// engines actually produced rather than only whether they finished - a share that reports success
/// while sending nothing is the failure worth catching here.
/// </remarks>
/// <param name="mirrorEngine">Captures and encodes the screen.</param>
/// <param name="audioEngine">Captures and encodes the sound.</param>
/// <param name="output">Where progress and the outcome are written.</param>
internal sealed class MirrorCommand(IMirrorEngine mirrorEngine, IAudioEngine audioEngine, TextWriter output)
{
    /// <summary>Frames between two progress lines, so a minute of sharing is a readable log.</summary>
    internal const int FramesPerLine = 30;

    /// <summary>Shares until cancelled or until the TV goes away.</summary>
    /// <typeparam name="TTransport">The receiver session, which carries the picture and the sound.</typeparam>
    /// <param name="session">The connected receiver session.</param>
    /// <param name="choices">What the command line asked the share to send.</param>
    /// <param name="display">The display to share, or null for the first one capture finds.</param>
    /// <param name="tvWidth">The TV's own width, when it said.</param>
    /// <param name="surfaceName">The name the TV shows for the share.</param>
    /// <param name="cancellationToken">Stops sharing.</param>
    /// <returns>The exit code.</returns>
    internal async Task<int> RunAsync<TTransport>(
        TTransport session,
        MirrorChoices choices,
        DisplayInfo? display,
        int? tvWidth,
        string surfaceName,
        CancellationToken cancellationToken)
        where TTransport : IMirrorTransport, IAudioTransport
    {
        ArgumentNullException.ThrowIfNull(choices);

        var options = choices.Resolve(display, tvWidth);
        var runner = new ScreenMirrorRunner(mirrorEngine);
        var lastReported = 0L;
        runner.StatsUpdated += stats =>
        {
            if (stats.FramesEncoded - lastReported < FramesPerLine)
            {
                return;
            }

            lastReported = stats.FramesEncoded;
            output.WriteLine(
                $"  {stats.FramesEncoded} frames, {stats.BytesEncoded / 1024} KiB, "
                + $"{stats.FramesUnchanged} idle ticks, {stats.Recoveries} recoveries");
        };

        var what = choices.Sound ? " and its sound" : string.Empty;
        output.WriteLine($"  Sharing {display?.Describe() ?? "the first display capture finds"}{what}. Ctrl+C to stop.");
        output.WriteLine($"  {choices.Describe(options)}");

        // Sound waits on the share's control for the picture's clock, so the two start together.
        var control = new MirrorControl();
        control.SetShowPointer(choices.Pointer);
        using var soundStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var soundRun = choices.Sound
            ? new AudioPump(audioEngine).RunAsync(session, new AudioShareOptions(), control, soundStop.Token)
            : null;

        MirrorSessionStats stats;
        AudioPumpEnd? sound = null;
        try
        {
            stats = await runner.RunAsync(session, options, surfaceName, control, cancellationToken).ConfigureAwait(false);
        }
        catch (MirrorEngineException exception)
        {
            output.WriteLine();
            output.WriteLine($"  This PC cannot mirror: {exception.Message}");
            output.WriteLine();
            return FlintExitCode.MirrorUnsupported;
        }
        finally
        {
            // Sound never outlives the picture it plays beside. The pump never faults, so this
            // wait cannot hide the picture's own exception.
            await soundStop.CancelAsync().ConfigureAwait(false);
            if (soundRun is not null)
            {
                sound = await soundRun.ConfigureAwait(false);
            }
        }

        output.WriteLine();
        output.WriteLine($"  Mirror stopped: {stats.FramesEncoded} frames, {stats.BytesEncoded / 1024} KiB sent.");
        if (sound is { Problem: { } problem })
        {
            output.WriteLine($"  Sound did not play: {problem}");
        }
        else if (sound is { } played)
        {
            output.WriteLine($"  Sound: {played.Stats.Packets} packets sent, {played.Stats.Dropped} dropped.");
        }

        output.WriteLine();

        // Zero frames is a failure even though nothing threw: the television saw a mirror surface
        // and never a picture, which is exactly the outcome that must not read as success.
        if (stats.FramesEncoded == 0)
        {
            output.WriteLine("  No frames were encoded, so the television never showed anything.");
            return FlintExitCode.MirrorProducedNoFrames;
        }

        return FlintExitCode.Success;
    }
}
