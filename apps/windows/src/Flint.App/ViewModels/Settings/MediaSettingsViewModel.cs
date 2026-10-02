using Flint.Core.Settings;

namespace Flint.App.ViewModels.Settings;

/// <summary>The Media section: the playback controls, and how the queue plays.</summary>
public sealed partial class MediaSettingsViewModel : SettingsSectionViewModel
{
    private readonly ISettingsService settings;

    /// <summary>Creates the section over the live settings.</summary>
    public MediaSettingsViewModel(ISettingsService settings)
        : base("Media")
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        settings.Changed += (_, change) =>
        {
            if (change.Previous.Media != change.Current.Media)
            {
                OnPropertyChanged(string.Empty);
            }
        };
        Reset = new ConfirmableAction(
            "RESET MEDIA SETTINGS",
            "Put every Media setting back to how Flint came?",
            "RESET",
            () => settings.Update(current => current with { Media = new MediaSettings() }));
    }

    /// <summary>The skip-back distance.</summary>
    public SettingText SkipBackText { get; } = new(
        "Skip back by",
        "How far the back button, and the Left key on the Media page, go back.");

    /// <summary>The skip-forward distance.</summary>
    public SettingText SkipForwardText { get; } = new(
        "Skip forward by",
        "How far the forward button, and the Right key on the Media page, go ahead.");

    /// <summary>The volume step.</summary>
    public SettingText VolumeStepText { get; } = new(
        "Volume step",
        "How much one press of the Up or Down key changes the TV's volume.");

    /// <inheritdoc />
    public override IReadOnlyList<SettingText> Settings =>
    [
        SkipBackText, SkipForwardText, VolumeStepText, AutoPlayNextText, RepeatText, ShuffleText,
        PlayedBeforeText, RememberText, SubfoldersText, DropOrderText, PictureText, QueueEndText,
    ];

    /// <summary>The skip-back distances offered.</summary>
    public IReadOnlyList<SecondsChoice> SkipBackChoices { get; } =
        [.. MediaSettings.SkipBackChoices.Select(seconds => new SecondsChoice(seconds))];

    /// <summary>The skip-forward distances offered.</summary>
    public IReadOnlyList<SecondsChoice> SkipForwardChoices { get; } =
        [.. MediaSettings.SkipForwardChoices.Select(seconds => new SecondsChoice(seconds))];

    /// <summary>The skip-back distance in use.</summary>
    public SecondsChoice? SkipBack
    {
        get => SkipBackChoices.FirstOrDefault(choice => choice.Seconds == settings.Current.Media.SkipBackSeconds);
        set
        {
            if (value is not null)
            {
                settings.Update(current => current with { Media = current.Media with { SkipBackSeconds = value.Seconds } });
            }
        }
    }

    /// <summary>The skip-forward distance in use.</summary>
    public SecondsChoice? SkipForward
    {
        get => SkipForwardChoices.FirstOrDefault(choice => choice.Seconds == settings.Current.Media.SkipForwardSeconds);
        set
        {
            if (value is not null)
            {
                settings.Update(current => current with { Media = current.Media with { SkipForwardSeconds = value.Seconds } });
            }
        }
    }

    /// <summary>The volume step, in percent.</summary>
    public double VolumeStep
    {
        get => settings.Current.Media.VolumeStepPercent;
        set => settings.Update(current => current with { Media = current.Media with { VolumeStepPercent = (int)Math.Round(value) } });
    }

    /// <summary>The volume step as it reads beside its slider.</summary>
    public string VolumeStepLabel => $"{settings.Current.Media.VolumeStepPercent}%";

    /// <summary>Puts this section back to how Flint came.</summary>
    public ConfirmableAction Reset { get; }
}

/// <summary>A distance in seconds, as offered.</summary>
/// <param name="Seconds">The distance.</param>
public sealed record SecondsChoice(int Seconds)
{
    /// <summary>How the choice reads.</summary>
    public string Label => $"{Seconds} seconds";

    /// <inheritdoc />
    public override string ToString() => Label;
}
