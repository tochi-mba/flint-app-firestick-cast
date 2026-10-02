using Flint.Core.Settings;

namespace Flint.App.ViewModels.Settings;

/// <summary>The Media section's queue settings: what plays next, and what is remembered.</summary>
public sealed partial class MediaSettingsViewModel
{
    /// <summary>Playing the next item by itself.</summary>
    public SettingText AutoPlayNextText { get; } = new(
        "Play the next item automatically",
        "When a file ends, the next one in the queue starts.");

    /// <summary>Repeating.</summary>
    public SettingText RepeatText { get; } = new(
        "Repeat",
        "Play the queue once, start it again at the end, or keep playing the same file.");

    /// <summary>Shuffling.</summary>
    public SettingText ShuffleText { get; } = new(
        "Shuffle",
        "Play the queue in a random order. Every file plays once before any plays again.");

    /// <summary>Files played before.</summary>
    public SettingText PlayedBeforeText { get; } = new(
        "When a file was played before",
        "Carry on from where it stopped, ask each time, or start from the beginning.");

    /// <summary>Remembering positions.</summary>
    public SettingText RememberText { get; } = new(
        "Remember where files stopped",
        "Kept only on this PC, without file names. Off means nothing new is kept.");

    /// <summary>Subfolders.</summary>
    public SettingText SubfoldersText { get; } = new(
        "Include subfolders when a folder is dropped",
        "Off plays only the files directly in the folder.");

    /// <summary>Drop order.</summary>
    public SettingText DropOrderText { get; } = new(
        "Order of dropped files",
        "By name puts Episode 2 before Episode 10. Files inside a folder are always by name.");

    /// <summary>How long a picture shows.</summary>
    public SettingText PictureText { get; } = new(
        "Show each picture for",
        "How long a picture in the queue stays before the next item. The TV needs at least 6 seconds.");

    /// <summary>The end of the queue.</summary>
    public SettingText QueueEndText { get; } = new(
        "When the queue ends",
        "Leave the last item on the TV, or send the TV back to its home screen.");

    /// <summary>Whether the next item plays by itself.</summary>
    public bool AutoPlayNext
    {
        get => settings.Current.Media.AutoPlayNext;
        set => Update(media => media with { AutoPlayNext = value });
    }

    /// <summary>Whether the queue is shuffled.</summary>
    public bool Shuffle
    {
        get => settings.Current.Media.Shuffle;
        set => Update(media => media with { Shuffle = value });
    }

    /// <summary>Whether positions are kept.</summary>
    public bool RememberPlayback
    {
        get => settings.Current.Media.RememberPlayback;
        set => Update(media => media with { RememberPlayback = value });
    }

    /// <summary>Whether a dropped folder's subfolders are opened.</summary>
    public bool IncludeSubfolders
    {
        get => settings.Current.Media.IncludeSubfolders;
        set => Update(media => media with { IncludeSubfolders = value });
    }

    /// <summary>The repeat choices.</summary>
    public IReadOnlyList<Choice<RepeatMode>> RepeatChoices { get; } =
    [
        new(RepeatMode.Off, "Off"),
        new(RepeatMode.All, "The whole queue"),
        new(RepeatMode.One, "The same file"),
    ];

    /// <summary>The repeat in use.</summary>
    public Choice<RepeatMode>? Repeat
    {
        get => RepeatChoices.FirstOrDefault(choice => choice.Value == settings.Current.Media.Repeat);
        set => Pick(value, (media, repeat) => media with { Repeat = repeat });
    }

    /// <summary>The played-before choices.</summary>
    public IReadOnlyList<Choice<ResumeMode>> PlayedBeforeChoices { get; } =
    [
        new(ResumeMode.Ask, "Ask each time"),
        new(ResumeMode.Resume, "Carry on where it stopped"),
        new(ResumeMode.StartOver, "Start from the beginning"),
    ];

    /// <summary>The played-before choice in use.</summary>
    public Choice<ResumeMode>? PlayedBefore
    {
        get => PlayedBeforeChoices.FirstOrDefault(choice => choice.Value == settings.Current.Media.PlayedBefore);
        set => Pick(value, (media, mode) => media with { PlayedBefore = mode });
    }

    /// <summary>The drop orders.</summary>
    public IReadOnlyList<Choice<DropOrder>> DropOrderChoices { get; } =
    [
        new(DropOrder.ByName, "By name"),
        new(DropOrder.AsDropped, "As dropped"),
    ];

    /// <summary>The drop order in use.</summary>
    public Choice<DropOrder>? DroppedOrder
    {
        get => DropOrderChoices.FirstOrDefault(choice => choice.Value == settings.Current.Media.DropOrder);
        set => Pick(value, (media, order) => media with { DropOrder = order });
    }

    /// <summary>How long a picture can be shown for.</summary>
    public IReadOnlyList<Choice<int>> PictureChoices { get; } =
    [
        new(0, "Until I move on"),
        new(6, "6 seconds"),
        new(10, "10 seconds"),
        new(15, "15 seconds"),
        new(30, "30 seconds"),
        new(60, "1 minute"),
    ];

    /// <summary>How long a picture is shown for.</summary>
    /// <remarks>A value set by hand between the offered ones shows as the nearest offered.</remarks>
    public Choice<int>? PictureSeconds
    {
        get => PictureChoices.MinBy(choice => Math.Abs(choice.Value - settings.Current.Media.PictureSeconds));
        set => Pick(value, (media, seconds) => media with { PictureSeconds = seconds });
    }

    /// <summary>What the TV shows at the end of the queue.</summary>
    public IReadOnlyList<Choice<QueueEnd>> QueueEndChoices { get; } =
    [
        new(QueueEnd.LeaveLastItem, "Leave the last item up"),
        new(QueueEnd.TvHome, "Go back to the TV's home screen"),
    ];

    /// <summary>What the TV shows at the end of the queue, in use.</summary>
    public Choice<QueueEnd>? AtQueueEnd
    {
        get => QueueEndChoices.FirstOrDefault(choice => choice.Value == settings.Current.Media.QueueEnd);
        set => Pick(value, (media, end) => media with { QueueEnd = end });
    }

    private void Update(Func<MediaSettings, MediaSettings> change) =>
        settings.Update(current => current with { Media = change(current.Media) });

    private void Pick<T>(Choice<T>? choice, Func<MediaSettings, T, MediaSettings> change)
    {
        if (choice is not null)
        {
            Update(media => change(media, choice.Value));
        }
    }
}

/// <summary>One value a setting can take, with the words it is shown with.</summary>
/// <param name="Value">The value.</param>
/// <param name="Label">The words.</param>
public sealed record Choice<T>(T Value, string Label)
{
    /// <inheritdoc />
    public override string ToString() => Label;
}
