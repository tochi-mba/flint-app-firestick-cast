using Velopack;
using Velopack.Sources;

namespace Flint.Platform.Windows;

/// <summary>Where a newer Flint comes from, and how it is applied.</summary>
/// <remarks>
/// An interface so the update flow can be tested without a network, an installed application or a
/// process restart — none of which a unit test may do.
/// </remarks>
public interface IUpdateSource
{
    /// <summary>
    /// Whether this copy was installed rather than unpacked from the portable zip.
    /// </summary>
    /// <remarks>
    /// A portable copy has no installer to hand a new version to, so it is never offered one. Saying
    /// so plainly beats downloading something that cannot be applied.
    /// </remarks>
    bool IsInstalled { get; }

    /// <summary>The newer version on offer, or <see langword="null"/> when this build is current.</summary>
    Task<string?> CheckForNewVersionAsync(CancellationToken cancellationToken);

    /// <summary>Downloads the version the last check found.</summary>
    Task DownloadAsync(IProgress<int>? progress, CancellationToken cancellationToken);

    /// <summary>Applies the downloaded version and restarts Flint.</summary>
    void ApplyAndRestart();
}

/// <summary>The real source: the GitHub releases of this repository, through Velopack.</summary>
public sealed class VelopackUpdateSource : IUpdateSource
{
    /// <summary>The repository the installer and its updates are published from.</summary>
    public const string RepositoryUrl = "https://github.com/tochi-mba/flint-app-firestick-cast";

    private readonly UpdateManager manager;
    private UpdateInfo? pending;

    /// <summary>Creates a source over this installation.</summary>
    public VelopackUpdateSource()
    {
        // A rolling install follows rolling builds; a tagged install follows tagged releases. Which
        // one this is can be read off the installed version, so somebody who chose a stable release
        // is never moved onto a build that changes under them, and somebody on the rolling build is
        // not stranded on it until the next tag.
        var installed = new UpdateManager(RepositoryUrl);
        var prerelease = installed.IsInstalled && (installed.CurrentVersion?.IsPrerelease ?? false);
        manager = new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: prerelease));
    }

    /// <inheritdoc />
    public bool IsInstalled => manager.IsInstalled;

    /// <inheritdoc />
    public async Task<string?> CheckForNewVersionAsync(CancellationToken cancellationToken)
    {
        pending = await manager.CheckForUpdatesAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        return pending?.TargetFullRelease.Version.ToString();
    }

    /// <inheritdoc />
    public async Task DownloadAsync(IProgress<int>? progress, CancellationToken cancellationToken)
    {
        if (pending is null)
        {
            return;
        }

        await manager.DownloadUpdatesAsync(pending, progress is null ? null : progress.Report, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void ApplyAndRestart()
    {
        if (pending is not null)
        {
            manager.ApplyUpdatesAndRestart(pending);
        }
    }
}
