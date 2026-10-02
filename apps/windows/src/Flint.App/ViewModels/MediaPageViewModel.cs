using Flint.Core.Media;
using Flint.Core.Settings;

namespace Flint.App.ViewModels;

/// <summary>The Media page: choosing files, the card for what is playing, and the queue.</summary>
/// <remarks>
/// The connection and the file chooser stay on the Cast page's view model, which owns the session;
/// this page only brings them together with the card.
/// </remarks>
public sealed class MediaPageViewModel : IDisposable
{
    /// <summary>Builds the page over the Cast page, the live settings and what was played before.</summary>
    public MediaPageViewModel(
        CastPageViewModel cast,
        ISettingsService settings,
        IMediaHistoryStore history,
        IMediaFileSystem files)
    {
        Cast = cast ?? throw new ArgumentNullException(nameof(cast));
        ArgumentNullException.ThrowIfNull(settings);
        NowPlaying = cast.NowPlaying;
        NowPlaying.UseSettings(settings);
        Queue = new MediaQueueViewModel(cast, settings, history, files);
    }

    /// <summary>The connection, and the file chooser.</summary>
    public CastPageViewModel Cast { get; }

    /// <summary>What is playing, and its controls.</summary>
    public NowPlayingViewModel NowPlaying { get; }

    /// <summary>What plays next.</summary>
    public MediaQueueViewModel Queue { get; }

    /// <inheritdoc />
    public void Dispose() => Queue.Dispose();
}
