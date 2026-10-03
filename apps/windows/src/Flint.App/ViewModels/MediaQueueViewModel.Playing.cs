using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flint.Core;
using Flint.Core.Media;
using Flint.Core.Settings;

namespace Flint.App.ViewModels;

/// <summary>
/// Moving through the queue: playing an item, moving on when it ends, and remembering where it stopped.
/// </summary>
public sealed partial class MediaQueueViewModel
{
    /// <summary>How often the playing file's position is kept.</summary>
    internal static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(5);

    /// <summary>How long the resume question waits before resuming by itself.</summary>
    internal static readonly TimeSpan ResumeWait = TimeSpan.FromSeconds(8);

    /// <summary>How far into a file Previous restarts it rather than going back an item.</summary>
    internal const long RestartWithinMs = 3_000;

    private PlaylistItem? waitingItem;
    private string? playingKey;
    private ITimer? saveTimer;
    private ITimer? pictureTimer;
    private ITimer? resumeTimer;
    private TaskCompletionSource<bool>? resumeAnswer;
    private PlaybackPhase lastPhase;

    /// <summary>
    /// Where the playing file last was, and how long it is. The card forgets both the moment it is
    /// stopped, before saying it has stopped, so the position to keep is the last one seen.
    /// </summary>
    private long knownPositionMs;
    private long knownDurationMs;

    /// <summary>The position last written, so a paused file is not written again every few seconds.</summary>
    private long savedPositionMs = -1;

    /// <summary>Whether the next item is waiting for a TV that shows something else.</summary>
    [ObservableProperty]
    private bool _isWaiting;

    /// <summary>Why the next item is waiting.</summary>
    [ObservableProperty]
    private string _waitingText = string.Empty;

    /// <summary>Whether the queue is asking whether to resume a file.</summary>
    [ObservableProperty]
    private bool _isAskingResume;

    /// <summary>The resume question.</summary>
    [ObservableProperty]
    private string _resumeText = string.Empty;

    private bool CanGoNext => cast.IsSessionConnected && playlist.PeekNext(ManualRepeat) is not null;

    private bool CanGoPrevious => cast.IsSessionConnected && (playlist.CanGoBack || playlist.Current is not null);

    /// <summary>The repeat a press of Next follows: the next item even when one is repeating.</summary>
    private RepeatMode ManualRepeat => Repeat is RepeatMode.All ? RepeatMode.All : RepeatMode.Off;

    /// <summary>Plays the next item.</summary>
    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private Task NextAsync() =>
        playlist.Advance(ManualRepeat) is { } next ? PlayAfterRebuildAsync(next) : Task.CompletedTask;

    /// <summary>Starts the playing item again, or goes back an item when it has only just started.</summary>
    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private Task PreviousAsync()
    {
        var card = cast.NowPlaying;
        if (playlist.Current is not null && card.IsActive && card.HasDuration && card.PositionMs > RestartWithinMs)
        {
            card.CommitSeek(0);
            return Task.CompletedTask;
        }

        return playlist.GoBack() is { } previous ? PlayAfterRebuildAsync(previous) : Task.CompletedTask;
    }

    /// <summary>Plays the item that was waiting, replacing what the TV shows after asking.</summary>
    [RelayCommand]
    private Task PlayWaitingAsync() =>
        waitingItem is { } item ? PlayItemAsync(item, MediaTakeover.Ask) : Task.CompletedTask;

    /// <summary>Carries on from where the file stopped.</summary>
    [RelayCommand]
    private void ResumeFromWhereItStopped() => AnswerResume(true);

    /// <summary>Plays the file from the start.</summary>
    [RelayCommand]
    private void StartOver() => AnswerResume(false);

    /// <summary>Plays <paramref name="item"/>, from where it stopped if the person wants that.</summary>
    private async Task PlayItemAsync(PlaylistItem item, MediaTakeover takeover)
    {
        StopTimers();
        var wasWaiting = waitingItem is not null;
        IsWaiting = false;
        waitingItem = null;
        if (wasWaiting)
        {
            RebuildRows();
        }

        playingKey = null;
        knownPositionMs = 0;
        knownDurationMs = 0;
        savedPositionMs = -1;
        var facts = files.Facts(item.Path);
        if (facts is null)
        {
            await SkipMissingAsync(item, takeover).ConfigureAwait(true);
            return;
        }

        var key = ResumePolicy.KeyFor(item.Path, facts.SizeBytes, facts.LastWritten);
        var start = await StartPositionAsync(item, key).ConfigureAwait(true);
        var result = await cast.PlayFileAsync(item.Path, start, takeover).ConfigureAwait(true);
        switch (result)
        {
            case MediaStart.Playing:
                playingKey = key;
                lastPhase = cast.NowPlaying.Phase;
                saveTimer = time.CreateTimer(_ => OnUi(SavePosition), null, SaveInterval, SaveInterval);
                StartPictureTimer();
                break;
            case MediaStart.Waiting:
                waitingItem = item;
                // Waiting means something else is on the TV, so there is always a notice to give.
                WaitingText = $"{item.Name} is waiting. {cast.TvShowingNotice}";
                IsWaiting = true;
                RebuildRows();
                break;
            case MediaStart.Refused:
                Status = $"The TV could not play {item.Name}. Press Next to skip it.";
                break;
        }

        NextCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Marks a file that has gone, and moves past it when the queue was moving on by itself.</summary>
    private async Task SkipMissingAsync(PlaylistItem item, MediaTakeover takeover)
    {
        RebuildRows();
        foreach (var row in Rows.Where(row => row.Item.Id == item.Id))
        {
            row.IsMissing = true;
        }

        Status = $"{item.Name} is no longer there, so it was skipped.";
        if (Rows.All(row => row.IsMissing))
        {
            Status = "None of the queued files are there any more.";
            return;
        }

        if (playlist.Advance(Repeat is RepeatMode.One ? RepeatMode.Off : Repeat) is not { } next)
        {
            Status = $"{item.Name} is no longer there, and nothing is queued after it.";
            return;
        }

        RebuildRows();
        await PlayItemAsync(next, takeover).ConfigureAwait(true);
    }

    /// <summary>Where to start a file: the start, where it stopped, or whichever the person picks.</summary>
    private async Task<long> StartPositionAsync(PlaylistItem item, string key)
    {
        var media = settings.Current.Media;
        if (!media.RememberPlayback || item.Type.IsPicture)
        {
            return 0;
        }

        var kept = history.Find(key);
        switch (ResumePolicy.Decide(media.PlayedBefore, kept))
        {
            case ResumeDecision.Resume:
                return kept!.PositionMs;
            case ResumeDecision.Ask:
                ResumeText = $"Resume {item.Name} from {PlaybackTimeText.Format(kept!.PositionMs)}?";
                resumeAnswer = new TaskCompletionSource<bool>();
                IsAskingResume = true;

                // Nobody answering is a queue left playing to itself: carry on, as most people would.
                resumeTimer = time.CreateTimer(_ => OnUi(() => AnswerResume(true)), null, ResumeWait, Timeout.InfiniteTimeSpan);
                return await resumeAnswer.Task.ConfigureAwait(true) ? kept.PositionMs : 0;
            default:
                return 0;
        }
    }

    private void AnswerResume(bool resume)
    {
        resumeTimer?.Dispose();
        resumeTimer = null;
        IsAskingResume = false;
        var answer = resumeAnswer;
        resumeAnswer = null;
        answer?.TrySetResult(resume);
    }

    private void OnMediaFinished(object? sender, EventArgs args)
    {
        if (playlist.Current is not { } current || current.Type.IsPicture)
        {
            // A picture is moved on by its own timer, or by the person; the TV's end means nothing here.
            return;
        }

        if (playingKey is { } key)
        {
            history.Forget(key);
        }

        if (settings.Current.Media.AutoPlayNext || Repeat is RepeatMode.One)
        {
            _ = RunUnobservedAsync(() => MoveOnAsync(current));
        }
    }

    /// <summary>Moves on by itself, as the settings say, at the end of a file or a picture's time.</summary>
    /// <param name="current">What has just ended.</param>
    private async Task MoveOnAsync(PlaylistItem current)
    {
        if (Repeat is RepeatMode.One && !current.Type.IsPicture)
        {
            // Played again from the start rather than sent again.
            cast.NowPlaying.PlayPauseCommand.Execute(null);
            return;
        }

        if (playlist.Advance(Repeat is RepeatMode.One ? RepeatMode.Off : Repeat) is { } next)
        {
            RebuildRows();
            await PlayItemAsync(next, MediaTakeover.OnlyIfFree).ConfigureAwait(true);
            return;
        }

        StopTimers();
        Status = "The queue has finished.";
        if (settings.Current.Media.QueueEnd is QueueEnd.TvHome)
        {
            await ((IMediaRemote)cast).StopAsync().ConfigureAwait(true);
        }
    }

    private void StartPictureTimer()
    {
        pictureTimer?.Dispose();
        pictureTimer = null;
        var seconds = settings.Current.Media.PictureSeconds;
        if (playlist.Current is { Type.IsPicture: true } picture && cast.NowPlaying.IsActive && seconds > 0)
        {
            pictureTimer = time.CreateTimer(
                _ => OnUi(() => _ = RunUnobservedAsync(() => MoveOnAsync(picture))),
                null,
                TimeSpan.FromSeconds(seconds),
                Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Keeps where the playing file is, when it is worth keeping.</summary>
    private void SavePosition()
    {
        NoteWhereTheFileIs();
        if (playingKey is not { } key || !settings.Current.Media.RememberPlayback || knownDurationMs <= 0)
        {
            return;
        }

        if (knownPositionMs != savedPositionMs && ResumePolicy.IsWorthKeeping(knownPositionMs, knownDurationMs))
        {
            history.Save(new MediaHistoryEntry(key, knownPositionMs, knownDurationMs, time.GetUtcNow()));
            savedPositionMs = knownPositionMs;
        }
    }

    /// <summary>Notes where the playing file is, while the card still knows.</summary>
    private void NoteWhereTheFileIs()
    {
        var card = cast.NowPlaying;
        if (card.IsActive && card.HasDuration && !card.IsSending)
        {
            knownPositionMs = card.PositionMs;
            knownDurationMs = card.DurationMs;
        }
    }

    private void OnNowPlayingChanged(object? sender, PropertyChangedEventArgs args)
    {
        // Every change, including the card's "everything changed" after a report from the TV. A card
        // being reset has already let go of its length, so this never notes the reset's zero.
        NoteWhereTheFileIs();

        // The bar moves four times a second; nothing else here depends on where it is.
        if (args.PropertyName is nameof(NowPlayingViewModel.PositionMs) or nameof(NowPlayingViewModel.SeekSeconds)
            or nameof(NowPlayingViewModel.ElapsedText) or nameof(NowPlayingViewModel.RemainingText)
            or nameof(NowPlayingViewModel.ScrubText) or nameof(NowPlayingViewModel.SpokenPosition))
        {
            return;
        }

        var card = cast.NowPlaying;
        if (card.Phase != lastPhase)
        {
            if (card.Phase is PlaybackPhase.Paused)
            {
                SavePosition();
            }

            lastPhase = card.Phase;
        }

        if (args.PropertyName == nameof(NowPlayingViewModel.IsConnectionLost) && card.IsConnectionLost)
        {
            SavePosition();
            StopTimers();
            AnswerResume(false);
            IsWaiting = false;
            FlintDiag.Info("FlintCast", "queue paused by a lost connection");
        }

        if (args.PropertyName == nameof(NowPlayingViewModel.IsActive) && !card.IsActive)
        {
            // Stopped or cleared: nothing is playing, so nothing is current in the list either.
            SavePosition();
            StopTimers();
            playingKey = null;
        }

        NextCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged();
    }

    private void StopTimers()
    {
        saveTimer?.Dispose();
        saveTimer = null;
        pictureTimer?.Dispose();
        pictureTimer = null;
    }
}
