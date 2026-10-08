using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flint.Core;
using Flint.Protocol;
using Flint.Session;

namespace Flint.App.ViewModels;

/// <summary>The share's live counters, and the suggestion made when the TV cannot keep up.</summary>
public sealed partial class ScreenPageViewModel
{
    /// <summary>What a counter shows before there is a figure for it.</summary>
    public const string NoFigure = "-";

    /// <summary>How long frames must keep being dropped before Data saver is suggested.</summary>
    internal static readonly TimeSpan StrugglingFor = TimeSpan.FromSeconds(10);

    /// <summary>How long a pause in dropping still counts as the same run of drops.</summary>
    internal static readonly TimeSpan DropGap = TimeSpan.FromSeconds(3);

    /// <summary>How long a dismissed suggestion stays away.</summary>
    internal static readonly TimeSpan SuggestionRest = TimeSpan.FromMinutes(5);

    /// <summary>The shortest window a rate is measured over, so one frame cannot make a figure.</summary>
    internal static readonly TimeSpan RateWindow = TimeSpan.FromSeconds(1);

    private MirrorSessionStats? rateFrom;
    private DateTimeOffset rateFromAt;
    private long? lastDropped;
    private DateTimeOffset? lastDropAt;
    private DateTimeOffset? droppingSince;
    private DateTimeOffset? dismissedAt;

    /// <summary>Whether the live counters are shown, as the settings say.</summary>
    public bool ShowLiveNumbers
    {
        get => Screen.ShowLiveNumbers;
        set => UpdateScreen(screen => screen with { ShowLiveNumbers = value });
    }

    /// <summary>Frames sent each second.</summary>
    [ObservableProperty]
    private string _liveFramesPerSecond = NoFigure;

    /// <summary>Data sent each second.</summary>
    [ObservableProperty]
    private string _liveDataRate = NoFigure;

    /// <summary>The size of the picture sent.</summary>
    [ObservableProperty]
    private string _livePictureSize = NoFigure;

    /// <summary>Where the picture is encoded.</summary>
    [ObservableProperty]
    private string _liveEncoder = NoFigure;

    /// <summary>Frames the TV could not show.</summary>
    [ObservableProperty]
    private string _liveDroppedFrames = NoFigure;

    /// <summary>Frames waiting on the TV to be shown.</summary>
    [ObservableProperty]
    private string _liveTvQueue = NoFigure;

    /// <summary>Times capture had to start again.</summary>
    [ObservableProperty]
    private string _liveRestarts = NoFigure;

    /// <summary>Sound packets sent, and those dropped because the network fell behind.</summary>
    [ObservableProperty]
    private string _liveSound = NoFigure;

    /// <summary>Whether the page suggests Data saver because the TV is not keeping up.</summary>
    [ObservableProperty]
    private bool _showStrugglingSuggestion;

    /// <summary>Switches to Data saver, as suggested.</summary>
    [RelayCommand]
    private void UseDataSaver()
    {
        ShowStrugglingSuggestion = false;
        droppingSince = null;
        ChosenMode = PictureModes.First(choice => choice.Mode is Core.Settings.PictureMode.DataSaver);
    }

    /// <summary>Puts the suggestion away for a while.</summary>
    [RelayCommand]
    private void DismissSuggestion()
    {
        ShowStrugglingSuggestion = false;
        dismissedAt = time.GetUtcNow();
    }

    internal void OnStats(MirrorSessionStats stats)
    {
        LiveRestarts = stats.Recoveries.ToString(CultureInfo.InvariantCulture);
        var now = time.GetUtcNow();
        if (rateFrom is not { } from)
        {
            rateFrom = stats;
            rateFromAt = now;
            return;
        }

        var seconds = (now - rateFromAt).TotalSeconds;
        if (now - rateFromAt < RateWindow)
        {
            return;
        }

        var frames = (stats.FramesEncoded - from.FramesEncoded) / seconds;
        var megabits = (stats.BytesEncoded - from.BytesEncoded) * 8 / seconds / 1_000_000;
        LiveFramesPerSecond = frames.ToString("0", CultureInfo.InvariantCulture);
        LiveDataRate = $"{megabits.ToString("0.0", CultureInfo.InvariantCulture)} Mbps";
        rateFrom = stats;
        rateFromAt = now;
    }

    internal void OnSoundReported(AudioPumpReport report) =>
        LiveSound = string.Create(
            CultureInfo.InvariantCulture,
            $"{report.Stats.Packets:N0} sent, {report.Stats.Dropped:N0} dropped");

    internal void OnPictureStarted(MirrorPicture picture)
    {
        LivePictureSize = $"{picture.Width} × {picture.Height}";
        LiveEncoder = picture.Encoder switch
        {
            MirrorEncoderKind.Hardware => "Graphics card",
            MirrorEncoderKind.Software => "Software, on the processor",
            _ => NoFigure,
        };
    }

    internal void OnReceiverStats(StatsMessage stats)
    {
        LiveDroppedFrames = stats.DroppedVideoFrames.ToString(CultureInfo.InvariantCulture);
        LiveTvQueue = stats.ReceiverQueueDepth.ToString(CultureInfo.InvariantCulture);

        var now = time.GetUtcNow();
        if (lastDropped is { } before && stats.DroppedVideoFrames > before)
        {
            // Drops after a quiet spell are a new run, not the old one carrying on.
            if (droppingSince is null || lastDropAt is not { } previous || now - previous > DropGap)
            {
                droppingSince = now;
            }

            lastDropAt = now;
        }
        else if (lastDropAt is not { } last || now - last > DropGap)
        {
            droppingSince = null;
        }

        lastDropped = stats.DroppedVideoFrames;
        ShowStrugglingSuggestion = droppingSince is { } since
            && now - since >= StrugglingFor
            && (dismissedAt is not { } dismissed || now - dismissed >= SuggestionRest)
            && Screen.PictureMode is not Core.Settings.PictureMode.DataSaver;
    }

    private void ResetLiveNumbers()
    {
        rateFrom = null;
        lastDropped = null;
        lastDropAt = null;
        droppingSince = null;
        ShowStrugglingSuggestion = false;
        LiveFramesPerSecond = NoFigure;
        LiveDataRate = NoFigure;
        LivePictureSize = NoFigure;
        LiveEncoder = NoFigure;
        LiveDroppedFrames = NoFigure;
        LiveTvQueue = NoFigure;
        LiveRestarts = NoFigure;
        LiveSound = NoFigure;
    }
}
