using CommunityToolkit.Mvvm.ComponentModel;
using Flint.Core.Media;

namespace Flint.App.ViewModels;

/// <summary>One file in the Up next list.</summary>
public sealed partial class QueueRowViewModel : ObservableObject
{
    /// <summary>Wraps one queued file.</summary>
    public QueueRowViewModel(PlaylistItem item) => Item = item ?? throw new ArgumentNullException(nameof(item));

    /// <summary>The file.</summary>
    public PlaylistItem Item { get; }

    /// <summary>The file's name.</summary>
    public string Name => Item.Name;

    /// <summary>What sort of thing it is, in a word.</summary>
    public string KindLabel => Item.Type.Kind switch
    {
        MediaKind.Video => "VIDEO",
        MediaKind.Audio => "MUSIC",
        MediaKind.Picture => "PICTURE",
        _ => "FILE",
    };

    /// <summary>Whether this is the item playing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Note), nameof(HasNote), nameof(SpokenName))]
    private bool _isCurrent;

    /// <summary>Whether the file was not there when its turn came.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Note), nameof(HasNote), nameof(SpokenName))]
    private bool _isMissing;

    /// <summary>Whether this is next, waiting for a TV that shows something else.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Note), nameof(HasNote), nameof(SpokenName))]
    private bool _isWaiting;

    /// <summary>The one thing worth saying about the file, if anything.</summary>
    public string? Note => IsMissing ? "File not found"
        : IsWaiting ? "Waiting for the TV"
        : !Item.Type.Tried ? "Flint has not tried this kind of file"
        : null;

    /// <summary>Whether there is a note to show.</summary>
    public bool HasNote => Note is not null;

    /// <summary>The row's name for a screen reader.</summary>
    public string SpokenName => (IsCurrent, Note) switch
    {
        (true, { } note) => $"{Name}, playing, {note}",
        (true, _) => $"{Name}, playing",
        (_, { } note) => $"{Name}, {note}",
        _ => Name,
    };
}
