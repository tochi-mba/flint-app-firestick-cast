using System.Diagnostics.CodeAnalysis;
using Flint.Core.Settings;

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
/// Makes sure only one TV surface is active at a time from the Windows side, and that nothing on
/// the TV is replaced without the person saying so.
/// </summary>
/// <remarks>
/// Cast media/mirror and the secure browser use different sockets. Without this, starting Web while
/// mirroring leaves both transports live and the television fighting over what to show. Anything
/// that claims a surface calls <see cref="TakeAsync"/>, which asks first whenever something else is
/// showing; <see cref="PrepareForAsync"/> is the part that follows the answer.
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "SemaphoreSlim needs disposing only once its AvailableWaitHandle is read, which this coordinator never does.")]
public sealed class ModeSessionCoordinator
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ISettingsService? settings;

    /// <summary>Builds a coordinator bound to the shell's Cast and Web pages.</summary>
    /// <param name="cast">The Cast / Media / Screen page.</param>
    /// <param name="browser">The Web page.</param>
    /// <param name="prompt">
    /// Where the switch question is asked. Without one nothing is asked and a claim simply
    /// proceeds, which is what a coordinator built only to stop surfaces needs.
    /// </param>
    /// <param name="settings">
    /// Whether the person wants to be asked at all. Without settings, a coordinator with a prompt
    /// always asks.
    /// </param>
    public ModeSessionCoordinator(
        CastPageViewModel cast,
        BrowserPageViewModel browser,
        SurfaceSwitchPrompt? prompt = null,
        ISettingsService? settings = null)
    {
        Cast = cast ?? throw new ArgumentNullException(nameof(cast));
        Browser = browser ?? throw new ArgumentNullException(nameof(browser));
        Prompt = prompt;
        this.settings = settings;
        Cast.AttachCoordinator(this);
        Browser.AttachCoordinator(this);
    }

    /// <summary>The Cast / Media / Screen page.</summary>
    public CastPageViewModel Cast { get; }

    /// <summary>The Web page.</summary>
    public BrowserPageViewModel Browser { get; }

    /// <summary>Where the switch question is asked, when anywhere.</summary>
    public SurfaceSwitchPrompt? Prompt { get; }

    /// <summary>Whether a claim asks before replacing what the TV shows.</summary>
    /// <remarks>
    /// The person can turn the question off in Settings. Arrival offers ask the same question, so
    /// they stop with it: an offer that cannot be declined would replace the TV on a page visit.
    /// </remarks>
    public bool AsksBeforeSwitching => Prompt is not null && (settings?.Current.General.AskBeforeSwitching ?? true);

    /// <summary>What this PC has on the TV right now.</summary>
    /// <remarks>A mirror that is still starting counts: the TV is already waiting for it.</remarks>
    public TvSurfaceKind Current =>
        Cast.IsMirroring ? TvSurfaceKind.Mirror
        : Cast.IsMediaPlaying ? TvSurfaceKind.Media
        : Browser.HasOpenBrowserSurface ? TvSurfaceKind.Browser
        : TvSurfaceKind.None;

    /// <summary>
    /// Takes the TV for <paramref name="next"/>, asking first when something else is on it.
    /// </summary>
    /// <returns>False when the person keeps what the TV is showing; nothing was changed.</returns>
    public async Task<bool> TakeAsync(TvSurfaceKind next, CancellationToken cancellationToken = default)
    {
        var current = Current;
        if (AsksBeforeSwitching && current is not TvSurfaceKind.None && current != next)
        {
            var copy = SurfaceSwitchCopy.For(current, next, Cast.Report?.Device?.FriendlyName);
            if (!await Prompt!.AskAsync(copy).ConfigureAwait(true))
            {
                Flint.Core.FlintDiag.Info("FlintSession", $"surface kept current={current} declined={next}");
                return false;
            }
        }

        await PrepareForAsync(next, cancellationToken).ConfigureAwait(true);
        return true;
    }

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

    /// <summary>
    /// Offers to switch the TV over when the person arrives at the page for <paramref name="page"/>.
    /// </summary>
    /// <remarks>
    /// Arriving at Web or Screen while the TV shows something else is the moment to ask, rather
    /// than leaving the switch to be discovered on the first page opened or the first press of
    /// Start. Only offered when the page could put its surface up right now; otherwise the notice
    /// says what the TV is showing and nothing is asked. The question is the one every claim asks,
    /// so answering it once is all it takes.
    /// </remarks>
    /// <param name="page">The surface the page the person arrived at puts on the TV.</param>
    /// <param name="stillThere">Whether the person is still on that page once the browser has reconnected.</param>
    public async Task OfferOnArrivalAsync(TvSurfaceKind page, Func<bool> stillThere)
    {
        ArgumentNullException.ThrowIfNull(stillThere);
        switch (page)
        {
            case TvSurfaceKind.Browser:
                await Browser.ActivateAsync().ConfigureAwait(true);
                if (AsksBeforeSwitching && stillThere() && Current is TvSurfaceKind.Mirror or TvSurfaceKind.Media && Browser.CanNavigate)
                {
                    await Browser.ShowOnTvAsync().ConfigureAwait(true);
                }

                break;

            case TvSurfaceKind.Mirror:
                if (AsksBeforeSwitching && Current is TvSurfaceKind.Browser or TvSurfaceKind.Media && Cast.CanStartMirrorNow)
                {
                    await Cast.StartScreenSessionCommand.ExecuteAsync(null).ConfigureAwait(true);
                }

                break;
        }
    }

    /// <summary>
    /// What to tell a person on the page for <paramref name="page"/> when the TV is showing
    /// something else from this PC; null when it is not.
    /// </summary>
    public string? NoticeFor(TvSurfaceKind page)
    {
        var current = Current;
        if (page is TvSurfaceKind.None || current is TvSurfaceKind.None || current == page)
        {
            return null;
        }

        var tv = Cast.Report?.Device?.FriendlyName is { Length: > 0 } name ? name : "The TV";
        return current switch
        {
            TvSurfaceKind.Mirror => $"{tv} is showing your screen.",
            TvSurfaceKind.Media => $"{tv} is playing a file from this PC.",
            _ => $"{tv} is showing the browser.",
        };
    }

    /// <summary>Whether the page for <paramref name="page"/> can take the TV over right now.</summary>
    public bool CanSwitchTo(TvSurfaceKind page) =>
        NoticeFor(page) is not null && page switch
        {
            TvSurfaceKind.Browser => Browser.CanNavigate,
            TvSurfaceKind.Mirror => Cast.CanStartMirrorNow,
            _ => false,
        };

    /// <summary>
    /// Switches the TV to <paramref name="page"/> because the person pressed the button that says
    /// so, which is the answer the question would have asked for.
    /// </summary>
    public async Task SwitchToAsync(TvSurfaceKind page, CancellationToken cancellationToken = default)
    {
        if (!CanSwitchTo(page))
        {
            return;
        }

        await PrepareForAsync(page, cancellationToken).ConfigureAwait(true);
        if (page is TvSurfaceKind.Browser)
        {
            await Browser.ShowOnTvAsync(cancellationToken).ConfigureAwait(true);
        }
        else
        {
            await Cast.StartScreenSessionCommand.ExecuteAsync(null).ConfigureAwait(true);
        }
    }
}
