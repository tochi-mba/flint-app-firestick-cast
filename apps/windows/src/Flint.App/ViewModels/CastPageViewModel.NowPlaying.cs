using Flint.Core.Media;
using Flint.Protocol;
using Flint.Session;

namespace Flint.App.ViewModels;

/// <summary>
/// The Now Playing card's link to the TV: its reports in, its controls out.
/// </summary>
/// <remarks>
/// The page owns the session, so the card asks the page to send rather than holding the session
/// itself. A control sent after the session has gone fails as any send would, and the card has
/// already been told the connection ended.
/// </remarks>
public sealed partial class CastPageViewModel : IMediaRemote
{
    private CancellationTokenSource? sendCancellation;
    private string? lastMediaPath;

    /// <summary>
    /// Whether this file's end has been told already. The TV repeats Ended every half second until
    /// something else happens, and a replay flips the card to playing before the TV has caught up,
    /// so the end is told once and told again only after the TV says it is playing.
    /// </summary>
    private bool endTold;

    /// <summary>The Now Playing card on the Media page.</summary>
    public NowPlayingViewModel NowPlaying { get; }

    /// <summary>Raised when the TV says the file from this PC played to its end.</summary>
    public event EventHandler? MediaFinished;

    /// <summary>What the TV shows instead of a file from this PC, in words, or null when it shows nothing else.</summary>
    internal string? TvShowingNotice => coordinator?.NoticeFor(TvSurfaceKind.Media);

    /// <inheritdoc />
    Task IMediaRemote.SendTransportAsync(TransportAction action, long positionMs) =>
        LiveSession().SendTransportAsync(action, positionMs);

    /// <inheritdoc />
    Task IMediaRemote.SetVolumeAsync(float level) => LiveSession().SetVolumeAsync(level);

    /// <inheritdoc />
    async Task IMediaRemote.StopAsync()
    {
        if (IsMediaPlaying)
        {
            await StopMediaAsync().ConfigureAwait(true);
            return;
        }

        // A file that never started playing, or a picture the TV is still showing: clearing the TV
        // is still what Stop means.
        await LiveSession().SendMediaAsync(new MediaCommandMessage(MediaAction.Clear)).ConfigureAwait(true);
        NowPlaying.Clear();
    }

    /// <inheritdoc />
    Task IMediaRemote.CancelSendAsync() => sendCancellation?.CancelAsync() ?? Task.CompletedTask;

    /// <inheritdoc />
    Task IMediaRemote.TryAgainAsync() =>
        lastMediaPath is { } path ? LoadMediaFileCommand.ExecuteAsync(path) : Task.CompletedTask;

    /// <summary>The session, or the same error a send on a closed one gives.</summary>
    /// <remarks>
    /// A session that has dropped but not yet been let go of is returned as it is: its own send
    /// refuses with exactly this error, so checking here as well would be a second copy of the rule.
    /// </remarks>
    private CastSession LiveSession() =>
        session ?? throw new IOException("The receiver session is no longer connected.");

    /// <summary>Passes the TV's playback reports to the card, on the thread the page was connected on.</summary>
    private void ListenForPlayback(CastSession watched)
    {
        var context = SynchronizationContext.Current;
        watched.PlaybackStateReceived += report =>
        {
            if (context is null)
            {
                OnPlaybackReported(watched, report);
            }
            else
            {
                context.Post(_ => OnPlaybackReported(watched, report), null);
            }
        };
    }

    /// <summary>One report from the TV's player.</summary>
    internal void OnPlaybackReported(CastSession from, PlaybackStateMessage report)
    {
        if (!ReferenceEquals(session, from))
        {
            return;
        }

        // Cleared on the TV itself, with its remote: nothing from this PC is playing any more.
        if (report.State is PlaybackState.Idle && NowPlaying.IsActive && !NowPlaying.IsSending)
        {
            IsMediaPlaying = false;
            MediaStatus = "Playback ended on the TV.";
            RaiseDerived();
        }

        NowPlaying.Apply(ToSnapshot(report));
        if (report.State is PlaybackState.Playing or PlaybackState.Buffering)
        {
            endTold = false;
        }
        else if (report.State is PlaybackState.Ended && NowPlaying.IsActive && !NowPlaying.IsSending && !endTold)
        {
            endTold = true;
            MediaFinished?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>A report as the card reads it, stamped with when it arrived.</summary>
    internal PlaybackSnapshot ToSnapshot(PlaybackStateMessage report) => new(
        report.State switch
        {
            PlaybackState.Buffering => PlaybackPhase.Buffering,
            PlaybackState.Playing => PlaybackPhase.Playing,
            PlaybackState.Paused => PlaybackPhase.Paused,
            PlaybackState.Ended => PlaybackPhase.Finished,
            PlaybackState.Error => PlaybackPhase.Problem,
            _ => PlaybackPhase.Idle,
        },
        report.PositionMs,
        report.DurationMs,
        report.State is PlaybackState.Error ? DescribePlaybackFailure(report.Detail) : report.Detail,
        time.GetUtcNow());
}
