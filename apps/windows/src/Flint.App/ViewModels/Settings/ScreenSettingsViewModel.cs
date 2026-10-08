using Flint.Core.Settings;

namespace Flint.App.ViewModels.Settings;

/// <summary>One option of a Screen sharing setting, as its list names it.</summary>
/// <typeparam name="T">The value the option stands for.</typeparam>
/// <param name="Value">The value.</param>
/// <param name="Label">Its name in the list.</param>
public sealed record ScreenOption<T>(T Value, string Label)
{
    /// <inheritdoc />
    public override string ToString() => Label;
}

/// <summary>The Screen sharing section: which display, the picture, and how a share starts.</summary>
public sealed class ScreenSettingsViewModel : SettingsSectionViewModel
{
    private readonly ISettingsService settings;

    /// <summary>Creates the section over the live settings.</summary>
    public ScreenSettingsViewModel(ISettingsService settings)
        : base("Screen sharing")
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        settings.Changed += (_, change) =>
        {
            if (change.Previous.Screen != change.Current.Screen)
            {
                OnPropertyChanged(string.Empty);
            }
        };
        Reset = new ConfirmableAction(
            "RESET SCREEN SHARING SETTINGS",
            "Put every Screen sharing setting back to how Flint came?",
            "RESET",
            () => settings.Update(current => current with { Screen = new ScreenSettings() }));
    }

    /// <summary>Which display a share captures.</summary>
    public SettingText DisplayText { get; } = new(
        "Display to share",
        "The main display, or the one last chosen on the Screen page.");

    /// <summary>Whether pressing Share asks first.</summary>
    public SettingText PromptText { get; } = new(
        "When I press Share",
        "Share the display chosen last, or ask which one each time.");

    /// <summary>The picture mode.</summary>
    public SettingText PictureText { get; } = new(
        "Picture mode",
        "Balanced, Movie, Game, Text and slides, Data saver, or your own size, frame rate and data rate.");

    /// <summary>The custom size limit.</summary>
    public SettingText CustomSizeText { get; } = new(
        "Custom: largest picture",
        "Never larger than the TV's own screen.");

    /// <summary>The custom frame rate.</summary>
    public SettingText CustomFramesText { get; } = new(
        "Custom: frames a second",
        "More is smoother and uses more data.");

    /// <summary>The custom data rate.</summary>
    public SettingText CustomRateText { get; } = new(
        "Custom: data rate",
        "Lower suits weak Wi-Fi; higher gives a cleaner picture.");

    /// <summary>Whether the live numbers show.</summary>
    public SettingText LiveNumbersText { get; } = new(
        "Show live numbers on the Screen page",
        "Frames and data sent each second, the picture size, and what the TV dropped.");

    /// <summary>The countdown before sharing.</summary>
    public SettingText CountdownText { get; } = new(
        "Count down before sharing starts",
        "A few seconds to get the right window in front, with a button to cancel.");

    /// <summary>Whether Flint gets out of the way when a share starts.</summary>
    public SettingText MinimiseText { get; } = new(
        "Minimise Flint when sharing starts",
        "So the TV shows what you are working on rather than Flint.");

    /// <inheritdoc />
    public override IReadOnlyList<SettingText> Settings =>
    [
        DisplayText, PromptText, PictureText, CustomSizeText, CustomFramesText, CustomRateText,
        LiveNumbersText, CountdownText, MinimiseText,
    ];

    /// <summary>The display choices.</summary>
    public IReadOnlyList<ScreenOption<ShareDisplayChoice>> DisplayChoices { get; } =
    [
        new(ShareDisplayChoice.Main, "The main display"),
        new(ShareDisplayChoice.Remembered, "The one chosen last"),
    ];

    /// <summary>The prompt choices.</summary>
    public IReadOnlyList<ScreenOption<ShareDisplayPrompt>> PromptChoices { get; } =
    [
        new(ShareDisplayPrompt.UseLast, "Use the last display"),
        new(ShareDisplayPrompt.AskEveryTime, "Ask each time"),
    ];

    /// <summary>The picture modes.</summary>
    public IReadOnlyList<ScreenOption<PictureMode>> PictureChoices { get; } =
    [
        new(PictureMode.Balanced, "Balanced"),
        new(PictureMode.Movie, "Movie"),
        new(PictureMode.Game, "Game"),
        new(PictureMode.TextAndSlides, "Text and slides"),
        new(PictureMode.DataSaver, "Data saver"),
        new(PictureMode.Custom, "Custom"),
    ];

    /// <summary>The custom size limits.</summary>
    public IReadOnlyList<ScreenOption<SizeLimit>> SizeChoices { get; } =
    [
        new(SizeLimit.P720, "720p"),
        new(SizeLimit.P1080, "1080p"),
        new(SizeLimit.MatchTv, "Match the TV"),
        new(SizeLimit.Native, "This display's own size"),
    ];

    /// <summary>The custom frame rates.</summary>
    public IReadOnlyList<ScreenOption<int>> FrameChoices { get; } =
        [.. ScreenSettings.FrameRates.Select(rate => new ScreenOption<int>(rate, $"{rate} a second"))];

    /// <summary>The countdowns.</summary>
    public IReadOnlyList<ScreenOption<int>> CountdownChoices { get; } =
        [.. ScreenSettings.Countdowns.Select(seconds => new ScreenOption<int>(seconds, seconds == 0 ? "Off" : $"{seconds} seconds"))];

    /// <summary>The display choice in use.</summary>
    public ScreenOption<ShareDisplayChoice>? Display
    {
        get => DisplayChoices.FirstOrDefault(choice => choice.Value == Screen.Display);
        set => Choose(value, (screen, display) => screen with { Display = display });
    }

    /// <summary>The prompt choice in use.</summary>
    public ScreenOption<ShareDisplayPrompt>? Prompt
    {
        get => PromptChoices.FirstOrDefault(choice => choice.Value == Screen.DisplayPrompt);
        set => Choose(value, (screen, prompt) => screen with { DisplayPrompt = prompt });
    }

    /// <summary>The picture mode in use.</summary>
    public ScreenOption<PictureMode>? Picture
    {
        get => PictureChoices.FirstOrDefault(choice => choice.Value == Screen.PictureMode);
        set => Choose(value, (screen, mode) => screen with { PictureMode = mode });
    }

    /// <summary>Whether the custom rows are shown.</summary>
    public bool IsCustom => Screen.PictureMode is PictureMode.Custom;

    /// <summary>The custom size limit in use.</summary>
    public ScreenOption<SizeLimit>? CustomSize
    {
        get => SizeChoices.FirstOrDefault(choice => choice.Value == Screen.CustomSizeLimit);
        set => Choose(value, (screen, limit) => screen with { CustomSizeLimit = limit });
    }

    /// <summary>The custom frame rate in use.</summary>
    public ScreenOption<int>? CustomFrames
    {
        get => FrameChoices.FirstOrDefault(choice => choice.Value == Screen.CustomFramesPerSecond);
        set => Choose(value, (screen, rate) => screen with { CustomFramesPerSecond = rate });
    }

    /// <summary>The custom data rate, in megabits per second.</summary>
    public double CustomRate
    {
        get => Screen.CustomMegabitsPerSecond;
        set => Update(screen => screen with { CustomMegabitsPerSecond = (int)Math.Round(value) });
    }

    /// <summary>Whether the live numbers show.</summary>
    public bool ShowLiveNumbers
    {
        get => Screen.ShowLiveNumbers;
        set => Update(screen => screen with { ShowLiveNumbers = value });
    }

    /// <summary>The countdown in use.</summary>
    public ScreenOption<int>? Countdown
    {
        get => CountdownChoices.FirstOrDefault(choice => choice.Value == Screen.CountdownSeconds);
        set => Choose(value, (screen, seconds) => screen with { CountdownSeconds = seconds });
    }

    /// <summary>Whether Flint minimises itself when a share starts.</summary>
    public bool MinimiseWhenSharing
    {
        get => Screen.MinimiseWhenSharing;
        set => Update(screen => screen with { MinimiseWhenSharing = value });
    }

    /// <summary>Puts this section back to how Flint came.</summary>
    public ConfirmableAction Reset { get; }

    private ScreenSettings Screen => settings.Current.Screen;

    private void Choose<T>(ScreenOption<T>? option, Func<ScreenSettings, T, ScreenSettings> change)
    {
        if (option is not null)
        {
            Update(screen => change(screen, option.Value));
        }
    }

    private void Update(Func<ScreenSettings, ScreenSettings> change) =>
        settings.Update(current => current with { Screen = change(current.Screen) });
}
