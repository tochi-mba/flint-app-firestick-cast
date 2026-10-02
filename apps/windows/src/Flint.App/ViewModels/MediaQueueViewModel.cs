using System.Collections.ObjectModel;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flint.Core.Media;
using Flint.Core.Settings;

namespace Flint.App.ViewModels;

/// <summary>
/// The Up next list on the Media page: what is queued, and how the queue moves through it.
/// </summary>
/// <remarks>
/// <para>
/// Files added while nothing plays start at once; files added while something plays wait their
/// turn and never interrupt it. The queue moves on when the TV says a file has ended, as the
/// person's settings say. It never takes the TV from something else on it: it waits, and says so.
/// </para>
/// <para>
/// The TV keeps one sent file at a time, so the next file is sent only when the current one has
/// finished. The gap between two items is the time that takes, and the card shows it happening.
/// </para>
/// </remarks>
public sealed partial class MediaQueueViewModel : ObservableObject, IDisposable
{
    private readonly CastPageViewModel cast;
    private readonly ISettingsService settings;
    private readonly IMediaHistoryStore history;
    private readonly IMediaFileSystem files;
    private readonly TimeProvider time;
    private readonly SynchronizationContext? context;
    private readonly Playlist playlist;

    /// <summary>Builds the queue over the Cast page's connection.</summary>
    /// <param name="cast">The connection, and the Now Playing card.</param>
    /// <param name="settings">How the queue moves on, repeats, shuffles and resumes.</param>
    /// <param name="history">Where played files stopped.</param>
    /// <param name="files">The disk.</param>
    /// <param name="time">The clock and timers; the system's unless a test supplies its own.</param>
    /// <param name="random">Where shuffle orders come from; a fixed seed in tests.</param>
    public MediaQueueViewModel(
        CastPageViewModel cast,
        ISettingsService settings,
        IMediaHistoryStore history,
        IMediaFileSystem files,
        TimeProvider? time = null,
        Random? random = null)
    {
        this.cast = cast ?? throw new ArgumentNullException(nameof(cast));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.history = history ?? throw new ArgumentNullException(nameof(history));
        this.files = files ?? throw new ArgumentNullException(nameof(files));
        this.time = time ?? TimeProvider.System;
        context = SynchronizationContext.Current;
        playlist = new Playlist(random) { Shuffle = settings.Current.Media.Shuffle };
        settings.Changed += OnSettingsChanged;
        cast.MediaFinished += OnMediaFinished;
        cast.NowPlaying.PropertyChanged += OnNowPlayingChanged;
    }

    /// <summary>The queued files, in the order shown.</summary>
    public ObservableCollection<QueueRowViewModel> Rows { get; } = [];

    /// <summary>Whether anything is queued.</summary>
    public bool HasItems => Rows.Count > 0;

    /// <summary>The last thing the queue has to say: what was added, why it stopped.</summary>
    [ObservableProperty]
    private string? _status;

    /// <summary>Whether the queue plays in a random order.</summary>
    public bool Shuffle
    {
        get => settings.Current.Media.Shuffle;
        set => settings.Update(current => current with { Media = current.Media with { Shuffle = value } });
    }

    /// <summary>How the queue repeats.</summary>
    public RepeatMode Repeat => settings.Current.Media.Repeat;

    /// <summary>The repeat button's words.</summary>
    public string RepeatLabel => Repeat switch
    {
        RepeatMode.All => "REPEAT ALL",
        RepeatMode.One => "REPEAT ONE",
        _ => "REPEAT OFF",
    };

    /// <summary>The shuffle button's words.</summary>
    public string ShuffleLabel => Shuffle ? "SHUFFLE ON" : "SHUFFLE OFF";

    /// <summary>Adds what was dropped on Flint or chosen in its picker, opening folders.</summary>
    public Task AddDroppedAsync(IReadOnlyList<string> dropped)
    {
        ArgumentNullException.ThrowIfNull(dropped);
        var media = settings.Current.Media;
        var contents = DropExpander.Expand(dropped, files, media.IncludeSubfolders, media.DropOrder);
        return AddAsync(contents);
    }

    /// <summary>Queues files, and starts the first when nothing from this PC is on the TV.</summary>
    internal async Task AddAsync(DropContents contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        if (contents.Files.Count == 0)
        {
            Status = contents.Skipped > 0
                ? "Those folders could not be opened, so nothing was added."
                : "None of those are files Flint can play.";
            return;
        }

        // Nothing from this PC on the TV, even if the queue remembers what played last.
        var idle = !cast.NowPlaying.IsActive;
        var added = playlist.Add(contents.Files);
        RebuildRows();
        var said = added.Count == 1 ? $"Added {added[0].Name} to the queue." : $"Added {added.Count} to the queue.";
        if (contents.Capped)
        {
            said = $"Added the first {DropExpander.MaximumFiles} files to the queue.";
        }

        if (contents.Skipped > 0)
        {
            said += $" {contents.Skipped} folder{(contents.Skipped == 1 ? " was" : "s were")} not readable.";
        }

        Status = said;
        if (idle && cast.IsSessionConnected)
        {
            var first = playlist.Select(added[0].Id)!;
            RebuildRows();
            await PlayItemAsync(first, MediaTakeover.Ask).ConfigureAwait(true);
        }
        else if (idle)
        {
            Status = said + " Connect to your TV on the Cast page to play them.";
        }
    }

    /// <summary>Plays a queued file now, in place of whatever is playing.</summary>
    [RelayCommand]
    private Task PlayNowAsync(QueueRowViewModel? row) =>
        row is not null && playlist.Select(row.Item.Id) is { } item
            ? PlayAfterRebuildAsync(item)
            : Task.CompletedTask;

    /// <summary>Moves a queued file to play straight after the current one.</summary>
    [RelayCommand]
    private void PlayNext(QueueRowViewModel? row)
    {
        if (row is null || playlist.Current?.Id == row.Item.Id)
        {
            return;
        }

        var after = playlist.Current is { } current ? IndexOf(current.Id) + 1 : 0;
        var from = IndexOf(row.Item.Id);
        playlist.Move(row.Item.Id, from < after ? after - 1 : after);
        RebuildRows();
    }

    /// <summary>Takes a file out of the queue. The file playing carries on.</summary>
    [RelayCommand]
    private void Remove(QueueRowViewModel? row)
    {
        if (row is not null && playlist.Remove(row.Item.Id))
        {
            RebuildRows();
        }
    }

    /// <summary>Moves a file one place up.</summary>
    [RelayCommand]
    private void MoveUp(QueueRowViewModel? row) => MoveBy(row, -1);

    /// <summary>Moves a file one place down.</summary>
    [RelayCommand]
    private void MoveDown(QueueRowViewModel? row) => MoveBy(row, 1);

    /// <summary>Empties the queue. What is playing carries on.</summary>
    [RelayCommand]
    private void Clear()
    {
        playlist.Clear();
        waitingItem = null;
        RebuildRows();
        Status = "The queue is empty.";
    }

    /// <summary>Turns shuffle on or off.</summary>
    [RelayCommand]
    private void ToggleShuffle() => Shuffle = !Shuffle;

    /// <summary>Goes from repeat off, to repeat all, to repeat one, and round again.</summary>
    [RelayCommand]
    private void CycleRepeat()
    {
        var next = Repeat switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.Off,
        };
        settings.Update(current => current with { Media = current.Media with { Repeat = next } });
    }

    /// <summary>The Up next list's keys: Alt with Up or Down moves a row, Delete removes it, Enter plays it.</summary>
    /// <returns>Whether the key did something.</returns>
    public bool HandleRowKey(QueueRowViewModel? row, Key key, KeyModifiers modifiers)
    {
        if (row is null)
        {
            return false;
        }

        switch (key)
        {
            case Key.Up when modifiers == KeyModifiers.Alt:
                MoveBy(row, -1);
                return true;
            case Key.Down when modifiers == KeyModifiers.Alt:
                MoveBy(row, 1);
                return true;
            case Key.Delete when modifiers == KeyModifiers.None:
                Remove(row);
                return true;
            case Key.Enter when modifiers == KeyModifiers.None:
                _ = PlayNowAsync(row);
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        StopTimers();
        settings.Changed -= OnSettingsChanged;
        cast.MediaFinished -= OnMediaFinished;
        cast.NowPlaying.PropertyChanged -= OnNowPlayingChanged;
    }

    private Task PlayAfterRebuildAsync(PlaylistItem item)
    {
        RebuildRows();
        return PlayItemAsync(item, MediaTakeover.Ask);
    }

    private void MoveBy(QueueRowViewModel? row, int step)
    {
        if (row is null)
        {
            return;
        }

        var index = IndexOf(row.Item.Id);
        if (index + step < 0 || index + step >= Rows.Count)
        {
            return;
        }

        playlist.Move(row.Item.Id, index + step);
        RebuildRows();
    }

    private int IndexOf(long id)
    {
        for (var index = 0; index < playlist.Items.Count; index++)
        {
            if (playlist.Items[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }

    private void RebuildRows()
    {
        var missing = Rows.Where(row => row.IsMissing).Select(row => row.Item.Id).ToHashSet();
        Rows.Clear();
        foreach (var item in playlist.Items)
        {
            // Waiting is not playing: what the TV shows is still the item before it.
            var waiting = item.Id == waitingItem?.Id;
            Rows.Add(new QueueRowViewModel(item)
            {
                IsCurrent = item.Id == playlist.Current?.Id && !waiting,
                IsWaiting = waiting,
                IsMissing = missing.Contains(item.Id),
            });
        }

        OnPropertyChanged(nameof(HasItems));
        NextCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged();
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs change)
    {
        if (change.Previous.Media == change.Current.Media)
        {
            return;
        }

        playlist.Shuffle = change.Current.Media.Shuffle;
        OnPropertyChanged(nameof(Shuffle));
        OnPropertyChanged(nameof(ShuffleLabel));
        OnPropertyChanged(nameof(Repeat));
        OnPropertyChanged(nameof(RepeatLabel));
        NextCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged();
        StartPictureTimer();
    }

    /// <summary>Runs <paramref name="action"/> where the list's bindings read it.</summary>
    private void OnUi(Action action)
    {
        if (context is null)
        {
            action();
        }
        else
        {
            context.Post(_ => action(), null);
        }
    }
}
