using Flint.Core.Settings;

namespace Flint.App.ViewModels;

/// <summary>The Media page: choosing a file, and the card for what is playing.</summary>
/// <remarks>
/// The connection and the file chooser stay on the Cast page's view model, which owns the session;
/// this page only brings them together with the card.
/// </remarks>
public sealed class MediaPageViewModel
{
    /// <summary>Builds the page over the Cast page and the live settings.</summary>
    public MediaPageViewModel(CastPageViewModel cast, ISettingsService settings)
    {
        Cast = cast ?? throw new ArgumentNullException(nameof(cast));
        ArgumentNullException.ThrowIfNull(settings);
        NowPlaying = cast.NowPlaying;
        NowPlaying.UseSettings(settings);
    }

    /// <summary>The connection, and the file chooser.</summary>
    public CastPageViewModel Cast { get; }

    /// <summary>What is playing, and its controls.</summary>
    public NowPlayingViewModel NowPlaying { get; }
}
