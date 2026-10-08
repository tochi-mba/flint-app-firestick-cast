using Flint.Core.Settings;

namespace Flint.App.ViewModels;

/// <summary>One picture mode, as the Screen page's segmented row names it.</summary>
/// <param name="Mode">The mode.</param>
/// <param name="Label">Its name on the page.</param>
public sealed record PictureModeChoice(PictureMode Mode, string Label)
{
    /// <inheritdoc />
    public override string ToString() => Label;
}

/// <summary>One size limit a custom picture can have.</summary>
/// <param name="Limit">The limit.</param>
/// <param name="Label">Its name on the page.</param>
public sealed record SizeLimitChoice(SizeLimit Limit, string Label)
{
    /// <inheritdoc />
    public override string ToString() => Label;
}

/// <summary>One frame rate a custom picture can have.</summary>
/// <param name="FramesPerSecond">The frame rate.</param>
public sealed record FrameRateChoice(int FramesPerSecond)
{
    /// <inheritdoc />
    public override string ToString() => $"{FramesPerSecond} a second";
}

/// <summary>The picture a share sends.</summary>
public sealed partial class ScreenPageViewModel
{
    /// <summary>The picture modes, in the order the page offers them.</summary>
    public IReadOnlyList<PictureModeChoice> PictureModes { get; } =
    [
        new(PictureMode.Balanced, "Balanced"),
        new(PictureMode.Movie, "Movie"),
        new(PictureMode.Game, "Game"),
        new(PictureMode.TextAndSlides, "Text and slides"),
        new(PictureMode.DataSaver, "Data saver"),
        new(PictureMode.Custom, "Custom"),
    ];

    /// <summary>The size limits a custom picture can have.</summary>
    public IReadOnlyList<SizeLimitChoice> SizeLimits { get; } =
    [
        new(SizeLimit.P720, "720p"),
        new(SizeLimit.P1080, "1080p"),
        new(SizeLimit.MatchTv, "Match the TV"),
        new(SizeLimit.Native, "This display's own size"),
    ];

    /// <summary>The frame rates a custom picture can have.</summary>
    public IReadOnlyList<FrameRateChoice> FrameRates { get; } =
        [.. ScreenSettings.FrameRates.Select(rate => new FrameRateChoice(rate))];

    /// <summary>The chosen picture mode.</summary>
    public PictureModeChoice ChosenMode
    {
        get => PictureModes.First(choice => choice.Mode == Screen.PictureMode);
        set
        {
            if (value is not null)
            {
                UpdateScreen(screen => screen with { PictureMode = value.Mode });
            }
        }
    }

    /// <summary>Whether the custom controls are shown.</summary>
    public bool IsCustom => Screen.PictureMode is PictureMode.Custom;

    /// <summary>What the chosen mode is for and exactly what it sends.</summary>
    public string ModeSentence =>
        ScreenQualityPreset.Describe(Screen.PictureMode, ShareOptions());

    /// <summary>The largest custom picture.</summary>
    public SizeLimitChoice CustomSizeLimit
    {
        get => SizeLimits.First(choice => choice.Limit == Screen.CustomSizeLimit);
        set
        {
            if (value is not null)
            {
                UpdateScreen(screen => screen with { CustomSizeLimit = value.Limit });
            }
        }
    }

    /// <summary>The custom frame rate.</summary>
    public FrameRateChoice CustomFrameRate
    {
        get => FrameRates.First(choice => choice.FramesPerSecond == Screen.CustomFramesPerSecond);
        set
        {
            if (value is not null)
            {
                UpdateScreen(screen => screen with { CustomFramesPerSecond = value.FramesPerSecond });
            }
        }
    }

    /// <summary>The custom data rate, in megabits per second.</summary>
    public double CustomMegabits
    {
        get => Screen.CustomMegabitsPerSecond;
        set => UpdateScreen(screen => screen with { CustomMegabitsPerSecond = (int)Math.Round(value) });
    }

    private ScreenSettings Screen => settings.Current.Screen;

    /// <summary>What a share of the chosen display sends, as this page's settings ask.</summary>
    private Core.MirrorSessionOptions ShareOptions() =>
        ScreenQualityPreset.ToOptions(Screen, selected?.Index ?? 0, Cast.TvScreenWidth, selected?.Width);

    private void UpdateScreen(Func<ScreenSettings, ScreenSettings> change) =>
        settings.Update(current => current with { Screen = change(current.Screen) });

    private void RaiseModes()
    {
        OnPropertyChanged(nameof(ChosenMode));
        OnPropertyChanged(nameof(IsCustom));
        OnPropertyChanged(nameof(ModeSentence));
        OnPropertyChanged(nameof(CustomSizeLimit));
        OnPropertyChanged(nameof(CustomFrameRate));
        OnPropertyChanged(nameof(CustomMegabits));
    }
}
