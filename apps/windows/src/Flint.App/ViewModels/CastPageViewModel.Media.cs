using CommunityToolkit.Mvvm.Input;
using Flint.App.Services;
using Flint.Core;
using Flint.Core.Media;
using Flint.Protocol;
using Flint.Session;

namespace Flint.App.ViewModels;

/// <summary>
/// Sending a file from this PC to the television and stopping it.
/// </summary>
/// <remarks>
/// One concern of the Cast page, in its own file so the page stays readable.
/// </remarks>
public sealed partial class CastPageViewModel
{
    /// <summary>Starts media selection when a live receiver session exists.</summary>
    [RelayCommand]
    private void ChooseMediaFile()
    {
        if (!IsSessionConnected)
        {
            Failure = "Enter the pairing code on Cast and connect the receiver first.";
            RaiseDerived();
            return;
        }

        MediaFileSelectionRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// What to tell the user when the TV's own player could not reach this PC's media server.
    /// </summary>
    /// <remarks>
    /// Pairing dials out from this PC to the TV and generally works even on the restrictive
    /// "Public" network profile, because outbound connections are allowed by default. Serving a
    /// media file needs the reverse: the TV dials in to an ephemeral port on this PC, which
    /// Windows Firewall blocks by default for an application with no inbound rule. The two paths
    /// fail independently, so pairing succeeding proves nothing about whether this will work.
    /// </remarks>
    internal const string FirewallGuidance =
        "The TV could not reach this PC to fetch the file. This is almost always Windows Firewall "
        + "blocking the connection, not the TV or the network: allow Flint through Windows "
        + "Defender Firewall for Private and Public networks, then try again.";

    /// <summary>
    /// Turns the receiver's own playback failure into a message that names a cause, when the
    /// wording lets it.
    /// </summary>
    /// <remarks>
    /// The receiver reports Media3's error code name, lowercased with underscores turned to
    /// spaces - for example "error code io network connection timeout". That text describes what
    /// ExoPlayer saw, not what to do about it, so a network-shaped failure is translated into the
    /// same actionable guidance <see cref="FirewallGuidance"/> gives when this PC times out
    /// waiting instead. Anything else - a bad file, an unsupported codec, a permission error - is
    /// shown as the receiver reported it rather than guessed at.
    /// </remarks>
    internal static string DescribePlaybackFailure(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return "The receiver ended playback before it started.";
        }

        return detail.Contains("network connection", StringComparison.OrdinalIgnoreCase)
            ? FirewallGuidance
            : detail;
    }

    /// <summary>Hosts and sends the selected local file to the authenticated receiver.</summary>
    [RelayCommand]
    private Task LoadMediaFileAsync(string path) => PlayFileAsync(path, startPositionMs: 0, MediaTakeover.Ask);

    /// <summary>
    /// Sends a file to the TV and starts it at <paramref name="startPositionMs"/>.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="startPositionMs">Where to start, for a file being resumed.</param>
    /// <param name="takeover">
    /// Whether the person asked for this, so the TV may be taken after asking, or the queue moved
    /// on by itself, so it may only use a TV that shows nothing or a file already.
    /// </param>
    /// <returns>
    /// What became of it: playing, refused by the TV, waiting for a TV showing something else, or
    /// not sent at all.
    /// </returns>
    internal async Task<MediaStart> PlayFileAsync(string path, long startPositionMs, MediaTakeover takeover)
    {
        if (!IsSessionConnected)
        {
            Failure = "The receiver session ended. Connect it again before choosing media.";
            RaiseDerived();
            return MediaStart.NotSent;
        }

        if (takeover is MediaTakeover.OnlyIfFree
            && coordinator?.Current is { } showing and not TvSurfaceKind.None and not TvSurfaceKind.Media)
        {
            FlintDiag.Info("FlintCast", $"queue waits current={showing}");
            return MediaStart.Waiting;
        }

        CastSession? sending = null;
        lastMediaPath = path;
        var start = MediaStart.NotSent;
        var fileName = System.IO.Path.GetFileName(path);
        var progress = new EndableProgress<double>(fraction =>
        {
            MediaStatus = $"Sending {fileName} to the TV ({(int)(fraction * 100)}%).";
            OnPropertyChanged(nameof(MediaStatus));
            NowPlaying.ReportSendProgress(fraction);
        });
        using var cancel = new CancellationTokenSource();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            // Asked, not assumed: the TV may be mirroring or showing the browser, and a person who
            // keeps it that way has chosen not to play this now.
            if (takeover is MediaTakeover.Ask
                && coordinator is not null
                && !await coordinator.TakeAsync(TvSurfaceKind.Media).ConfigureAwait(true))
            {
                return MediaStart.Kept;
            }

            // The TV builds a sent file from its chunks in the order they arrive, one file at a time.
            // Two sends side by side would weave two files into one broken one, so the newer request
            // ends the older and waits until it has let go. Of several waiting, the last one wins.
            while (sendInFlight is { IsCompleted: false } earlier)
            {
                await (sendCancellation?.CancelAsync() ?? Task.CompletedTask).ConfigureAwait(true);
                await earlier.ConfigureAwait(true);
            }

            if (session is not { IsConnected: true } live)
            {
                // The send just ended may have been the connection's last.
                return MediaStart.NotSent;
            }

            sending = live;
            sendInFlight = done.Task;
            var type = MediaFileTypes.For(path);
            var mimeType = type.MimeType;
            MediaStatus = $"Sending {fileName} to the TV.";
            OnPropertyChanged(nameof(MediaStatus));
            sendCancellation = cancel;
            NowPlaying.BeginSending(fileName, type.IsPicture);
            FlintDiag.Info("FlintCast", $"media push begin mime={mimeType} nameLen={fileName.Length}");
            using var timeout = new CancellationTokenSource(MediaStartTimeout, time);
            using var playbackTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token, timeout.Token);
            var playback = await sending.PushMediaAndWaitForPlaybackStartAsync(
                path,
                fileName,
                mimeType,
                startPositionMs: startPositionMs,
                progress: progress,
                cancellationToken: playbackTimeout.Token).ConfigureAwait(true);
            progress.End();

            if (playback.State is PlaybackState.Playing)
            {
                IsMediaPlaying = true;
                MediaStatus = $"Playing {fileName} on the TV.";
                Failure = null;
                NowPlaying.SendFinished(ToSnapshot(playback));
                start = MediaStart.Playing;
                FlintDiag.Info("FlintCast", "media playback started");
            }
            else
            {
                IsMediaPlaying = false;
                MediaStatus = "The TV did not start playback.";
                Failure = DescribePlaybackFailure(playback.Detail);
                NowPlaying.ShowProblem(Failure);
                start = MediaStart.Refused;
                FlintDiag.Warn("FlintCast", $"media playback not started state={playback.State}");
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            progress.End();
            IsMediaPlaying = false;
            MediaStatus = "Sending cancelled.";
            Failure = null;
            NowPlaying.Clear();
            start = MediaStart.Cancelled;
            FlintDiag.Info("FlintCast", "media push cancelled");
            // Cancellation is only possible once the send began, and with it the session was chosen.
            await DropPartialFileAsync(sending!).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            progress.End();
            IsMediaPlaying = false;
            MediaStatus = "The TV did not confirm playback.";
            Failure = FirewallGuidance;
            NowPlaying.ShowProblem(FirewallGuidance);
            start = MediaStart.Refused;
            FlintDiag.Warn("FlintCast", "media push timed out waiting for playback");
        }
        catch (Exception exception)
        {
            progress.End();
            IsMediaPlaying = false;
            MediaStatus = "Media was not sent.";
            Failure = exception.Message;
            NowPlaying.ShowProblem(exception.Message);
            start = MediaStart.Refused;
            FlintDiag.Error("FlintCast", $"media push failed: {exception.GetType().Name}");
        }
        finally
        {
            if (ReferenceEquals(sendCancellation, cancel))
            {
                sendCancellation = null;
            }

            done.SetResult();
        }

        RaiseDerived();
        return start;
    }

    /// <summary>Tells the TV to drop the part of a file it was sent before the send was cancelled.</summary>
    internal static async Task DropPartialFileAsync(CastSession sending)
    {
        try
        {
            await sending.SendMediaAsync(new MediaCommandMessage(MediaAction.Clear)).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // The connection has gone too; the TV drops a partial file when its session ends.
        }
    }

    /// <summary>Stops media if this PC started it; safe no-op otherwise.</summary>
    internal async Task StopMediaAsync(CancellationToken cancellationToken = default)
    {
        if (!IsMediaPlaying)
        {
            return;
        }

        try
        {
            if (session is { IsConnected: true })
            {
                await session.SendMediaAsync(new MediaCommandMessage(MediaAction.Clear), cancellationToken)
                    .ConfigureAwait(true);
            }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            // Best-effort stop: the next exclusive start still proceeds.
        }
        finally
        {
            IsMediaPlaying = false;
            NowPlaying.Clear();
            MediaStatus = "Playback stopped.";
            OnPropertyChanged(nameof(MediaStatus));
        }
    }
}
