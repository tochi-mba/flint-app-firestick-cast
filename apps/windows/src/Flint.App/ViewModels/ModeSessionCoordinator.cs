namespace Flint.App.ViewModels;

/// <summary>Which TV surface the Windows shell is about to own exclusively.</summary>
public enum TvSurfaceKind
{
    /// <summary>No exclusive surface request.</summary>
    None = 0,

    /// <summary>Local file / stream playback on the cast channel.</summary>
    Media = 1,

    /// <summary>Desktop mirror on the cast channel.</summary>
    Mirror = 2,

    /// <summary>TV-resident browser on the secure TLS channel.</summary>
    Browser = 3,
}

/// <summary>
/// Makes sure only one TV surface is active at a time from the Windows side.
/// </summary>
/// <remarks>
/// Cast media/mirror and the secure browser use different sockets. Without this, starting Web while
/// mirroring leaves both transports live and the television fighting over what to show. Call
/// <see cref="PrepareForAsync"/> immediately before claiming a surface.
/// </remarks>
public sealed class ModeSessionCoordinator
{
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>Builds a coordinator bound to the shell's Cast and Web pages.</summary>
    public ModeSessionCoordinator(CastPageViewModel cast, BrowserPageViewModel browser)
    {
        Cast = cast ?? throw new ArgumentNullException(nameof(cast));
        Browser = browser ?? throw new ArgumentNullException(nameof(browser));
        Cast.AttachCoordinator(this);
        Browser.AttachCoordinator(this);
    }

    /// <summary>The Cast / Media / Screen page.</summary>
    public CastPageViewModel Cast { get; }

    /// <summary>The Web page.</summary>
    public BrowserPageViewModel Browser { get; }

    /// <summary>Stops every other surface so <paramref name="next"/> can own the TV.</summary>
    public async Task PrepareForAsync(TvSurfaceKind next, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            Flint.Core.FlintDiag.Info(
                "FlintSession",
                $"surface prepare next={next} mirroring={Cast.IsMirroring} mediaPlaying={Cast.IsMediaPlaying} browserOpen={Browser.HasOpenBrowserSurface}");
            if (next != TvSurfaceKind.Mirror)
            {
                await Cast.StopMirrorAsync(cancellationToken).ConfigureAwait(true);
            }

            if (next != TvSurfaceKind.Media)
            {
                await Cast.StopMediaCoreAsync(cancellationToken).ConfigureAwait(true);
            }

            if (next != TvSurfaceKind.Browser)
            {
                await Browser.StopBrowserSurfaceAsync(cancellationToken).ConfigureAwait(true);
            }

            Flint.Core.FlintDiag.Info("FlintSession", $"surface ready for {next}");
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Stops whatever is currently on the TV from this PC.</summary>
    public Task StopEverythingAsync(CancellationToken cancellationToken = default) =>
        PrepareForAsync(TvSurfaceKind.None, cancellationToken);
}
