using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Flint.Protocol;

namespace Flint.App.ViewModels;

/// <summary>Observable state for the TV preview (on by default when the receiver supports it).</summary>
public sealed partial class BrowserPreviewViewModel : ObservableObject, IDisposable
{
    private DateTimeOffset rateWindowStarted = DateTimeOffset.UtcNow;
    private int framesInWindow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggle))]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyPropertyChangedFor(nameof(StageHeadline))]
    [NotifyPropertyChangedFor(nameof(StageDetail))]
    private bool isSupported;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggle))]
    [NotifyPropertyChangedFor(nameof(ToggleLabel))]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyPropertyChangedFor(nameof(StageHeadline))]
    [NotifyPropertyChangedFor(nameof(StageDetail))]
    private bool isRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLive))]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyPropertyChangedFor(nameof(StageHeadline))]
    [NotifyPropertyChangedFor(nameof(StageDetail))]
    private BrowserPreviewState state = BrowserPreviewState.Disabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFrame))]
    [NotifyPropertyChangedFor(nameof(ShowStageHints))]
    private Bitmap? frame;

    [ObservableProperty]
    private int frameWidth;

    [ObservableProperty]
    private int frameHeight;

    [ObservableProperty]
    private long frameId;

    [ObservableProperty]
    private long navigationId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TelemetryLabel))]
    private int framesPerSecond;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TelemetryLabel))]
    private long droppedFrames;

    public bool HasFrame => Frame is not null;
    public bool IsLive => State == BrowserPreviewState.Enabled;
    public bool CanToggle => IsSupported;
    public string ToggleLabel => IsRequested ? "PREVIEW OFF" : "PREVIEW ON";
    public string StatusLabel => !IsSupported || State == BrowserPreviewState.Unavailable
        ? "PREVIEW UNAVAILABLE"
        : IsRequested
            ? IsLive ? "LIVE · TV IS CANONICAL" : "STARTING PREVIEW"
            : "OFF · TV IS CANONICAL";
    /// <summary>
    /// The headline shown on an empty stage.
    /// </summary>
    /// <remarks>
    /// "Waiting for TV preview" used to show for every frameless state, including the two where no
    /// frame is ever coming: preview switched off, and a receiver that cannot capture at all. Naming
    /// the actual reason is the difference between a page that is loading and one that is finished.
    /// </remarks>
    public string StageHeadline => !IsSupported || State == BrowserPreviewState.Unavailable
        ? "This receiver cannot send a preview"
        : !IsRequested
            ? "Preview is off"
            : "Starting preview…";

    /// <summary>
    /// What to do while the stage is empty.
    /// </summary>
    /// <remarks>
    /// The same in every empty case on purpose: whether a preview is coming or not, the television
    /// is the real screen and the controls that drive it are the same ones. Someone opening this
    /// page for the first time should not have to infer that from a blank rectangle.
    /// </remarks>
    public string StageDetail => IsRequested && IsSupported && State != BrowserPreviewState.Unavailable
        ? "The TV is already showing the page. A preview mirrors it here so you can click and scroll with a mouse."
        : "The TV is showing the page. Everything still works from here — this stage just cannot mirror it.";

    /// <summary>Whether the first-run hints belong on screen, which is whenever there is no frame.</summary>
    public bool ShowStageHints => !HasFrame;

    public string TelemetryLabel => $"{FramesPerSecond} FPS · {DroppedFrames} REPLACED";

    /// <summary>
    /// Applies the receiver capability answer. Preview prefers on when supported; the user can
    /// still turn it off for the rest of the secure session.
    /// </summary>
    internal void Configure(bool supported, bool enableByDefault = true)
    {
        IsSupported = supported;
        State = supported ? BrowserPreviewState.Disabled : BrowserPreviewState.Unavailable;
        IsRequested = supported && enableByDefault;
        ClearFrame();
    }

    internal void SetRequested(bool requested)
    {
        IsRequested = requested && IsSupported;
        if (!IsRequested)
        {
            State = IsSupported ? BrowserPreviewState.Disabled : BrowserPreviewState.Unavailable;
            ClearFrame();
        }
    }

    internal void ApplyState(BrowserPreviewState value)
    {
        State = value;
        if (value != BrowserPreviewState.Enabled)
        {
            ClearFrame();
        }
    }

    internal void Accept(BrowserPreviewMessage message, Bitmap decoded)
    {
        if (!IsRequested || message.Width <= 0 || message.Height <= 0)
        {
            decoded.Dispose();
            return;
        }

        var previous = Frame;
        Frame = decoded;
        previous?.Dispose();
        FrameWidth = message.Width;
        FrameHeight = message.Height;
        FrameId = message.FrameId;
        NavigationId = message.NavigationId;
        State = BrowserPreviewState.Enabled;

        framesInWindow++;
        var now = DateTimeOffset.UtcNow;
        var elapsed = now - rateWindowStarted;
        if (elapsed >= TimeSpan.FromSeconds(1))
        {
            FramesPerSecond = (int)Math.Round(framesInWindow / elapsed.TotalSeconds);
            framesInWindow = 0;
            rateWindowStarted = now;
        }
    }

    internal void MarkDropped() => DroppedFrames++;

    internal void Reset()
    {
        // Session teardown — preference returns to the product default on the next Configure.
        IsRequested = false;
        State = IsSupported ? BrowserPreviewState.Disabled : BrowserPreviewState.Unavailable;
        DroppedFrames = 0;
        FramesPerSecond = 0;
        ClearFrame();
    }

    public void Dispose() => ClearFrame();

    private void ClearFrame()
    {
        var previous = Frame;
        Frame = null;
        previous?.Dispose();
        FrameWidth = 0;
        FrameHeight = 0;
        FrameId = 0;
        NavigationId = 0;
    }
}
