using CommunityToolkit.Mvvm.Input;
using Flint.App.Services;
using Flint.Core;
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
    private async Task LoadMediaFileAsync(string path)
    {
        if (!IsSessionConnected)
        {
            Failure = "The receiver session ended. Connect it again before choosing media.";
            RaiseDerived();
            return;
        }

        // A connected page always has its session.
        var sending = session!;

        lastMediaPath = path;
        var fileName = System.IO.Path.GetFileName(path);
        var progress = new EndableProgress<double>(fraction =>
        {
            MediaStatus = $"Sending {fileName} to the TV ({(int)(fraction * 100)}%).";
            OnPropertyChanged(nameof(MediaStatus));
            NowPlaying.ReportSendProgress(fraction);
        });
        using var cancel = new CancellationTokenSource();
        try
        {
            // Asked, not assumed: the TV may be mirroring or showing the browser, and a person who
            // keeps it that way has chosen not to play this now.
            if (coordinator is not null && !await coordinator.TakeAsync(TvSurfaceKind.Media).ConfigureAwait(true))
            {
                return;
            }

            var mimeType = GetMimeType(path);
            MediaStatus = $"Sending {fileName} to the TV.";
            OnPropertyChanged(nameof(MediaStatus));
            sendCancellation = cancel;
            NowPlaying.BeginSending(fileName, mimeType.StartsWith("image/", StringComparison.Ordinal));
            FlintDiag.Info("FlintCast", $"media push begin mime={mimeType} nameLen={fileName.Length}");
            using var timeout = new CancellationTokenSource(MediaStartTimeout, time);
            using var playbackTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token, timeout.Token);
            var playback = await sending.PushMediaAndWaitForPlaybackStartAsync(
                path,
                fileName,
                mimeType,
                progress: progress,
                cancellationToken: playbackTimeout.Token).ConfigureAwait(true);
            progress.End();

            if (playback.State is PlaybackState.Playing)
            {
                IsMediaPlaying = true;
                MediaStatus = $"Playing {fileName} on the TV.";
                Failure = null;
                NowPlaying.SendFinished(ToSnapshot(playback));
                FlintDiag.Info("FlintCast", "media playback started");
            }
            else
            {
                IsMediaPlaying = false;
                MediaStatus = "The TV did not start playback.";
                Failure = DescribePlaybackFailure(playback.Detail);
                NowPlaying.ShowProblem(Failure);
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
            FlintDiag.Info("FlintCast", "media push cancelled");
            await DropPartialFileAsync(sending).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            progress.End();
            IsMediaPlaying = false;
            MediaStatus = "The TV did not confirm playback.";
            Failure = FirewallGuidance;
            NowPlaying.ShowProblem(FirewallGuidance);
            FlintDiag.Warn("FlintCast", "media push timed out waiting for playback");
        }
        catch (Exception exception)
        {
            progress.End();
            IsMediaPlaying = false;
            MediaStatus = "Media was not sent.";
            Failure = exception.Message;
            NowPlaying.ShowProblem(exception.Message);
            FlintDiag.Error("FlintCast", $"media push failed: {exception.GetType().Name}");
        }
        finally
        {
            if (ReferenceEquals(sendCancellation, cancel))
            {
                sendCancellation = null;
            }
        }

        RaiseDerived();
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

    private static string GetMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp4" => "video/mp4",
        ".mkv" => "video/x-matroska",
        ".webm" => "video/webm",
        ".mp3" => "audio/mpeg",
        ".m4a" => "audio/mp4",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        _ => "application/octet-stream",
    };
}
