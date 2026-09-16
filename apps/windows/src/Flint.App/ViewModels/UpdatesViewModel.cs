using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flint.App.Services;
using Flint.Platform.Windows;

namespace Flint.App.ViewModels;

/// <summary>
/// Finding a newer Flint, fetching it, and restarting into it when that is safe.
/// </summary>
/// <remarks>
/// <para>
/// An update is never applied under a running session. Mirroring or a browser session on the
/// television ends the moment this process exits, and losing what somebody is doing to install
/// something they did not ask for at that moment is not a trade Flint makes.
/// </para>
/// <para>
/// The portable zip is told plainly that it does not update itself, rather than being offered a
/// download it has no installer to apply.
/// </para>
/// </remarks>
public sealed partial class UpdatesViewModel : ObservableObject
{
    private readonly IUpdateSource source;
    private readonly IUpdatePreference preference;
    private readonly Func<bool> isSessionLive;

    /// <summary>Creates the view model over an update source and the stored preference.</summary>
    /// <param name="source">Where updates come from.</param>
    /// <param name="preference">Whether checks may run unprompted.</param>
    /// <param name="isSessionLive">
    /// Answers whether the television is in use. Restarting is refused while it is true.
    /// </param>
    public UpdatesViewModel(
        IUpdateSource source,
        IUpdatePreference preference,
        Func<bool>? isSessionLive = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(preference);
        this.source = source;
        this.preference = preference;
        this.isSessionLive = isSessionLive ?? (() => false);
        checksAutomatically = preference.ChecksAutomatically;
        status = source.IsInstalled
            ? "Flint checks for updates when it starts."
            : "This is the portable build, so it does not update itself.";
    }

    /// <summary>What is happening, in one sentence a person can act on.</summary>
    [ObservableProperty]
    private string status;

    /// <summary>Whether a check or a download is in flight.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheck))]
    private bool isWorking;

    /// <summary>The version waiting to be installed, when there is one.</summary>
    [ObservableProperty]
    private string? readyVersion;

    /// <summary>How much of the download has arrived, as a percentage.</summary>
    [ObservableProperty]
    private int downloadedPercent;

    private bool checksAutomatically;

    /// <summary>Whether Flint may look for updates by itself.</summary>
    public bool ChecksAutomatically
    {
        get => checksAutomatically;
        set
        {
            if (SetProperty(ref checksAutomatically, value))
            {
                preference.ChecksAutomatically = value;
            }
        }
    }

    /// <summary>Whether this copy can update itself; the portable zip cannot.</summary>
    public bool IsInstalled => source.IsInstalled;

    /// <summary>Whether the check command can run.</summary>
    public bool CanCheck => source.IsInstalled && !IsWorking;

    /// <summary>Whether a restart into the new version is possible right now.</summary>
    public bool CanRestart => ReadyVersion is not null && !isSessionLive();

    /// <summary>Runs a check at launch, when the preference allows one.</summary>
    /// <remarks>
    /// Quiet by design: a launch that cannot reach GitHub says so on this page and nowhere else.
    /// </remarks>
    public async Task CheckAtLaunchAsync(CancellationToken cancellationToken = default)
    {
        if (ChecksAutomatically && source.IsInstalled)
        {
            await CheckAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Looks for a newer version, and downloads it when there is one.</summary>
    [RelayCommand]
    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!source.IsInstalled)
        {
            Status = "This is the portable build, so it does not update itself.";
            return;
        }

        IsWorking = true;
        DownloadedPercent = 0;
        Status = "Looking for a newer build.";
        try
        {
            var version = await source.CheckForNewVersionAsync(cancellationToken).ConfigureAwait(false);
            if (version is null)
            {
                Status = "Flint is up to date.";
                return;
            }

            Status = $"Downloading version {version}.";
            // Never backwards. Progress<T> hands its callbacks to the scheduler rather than running
            // them where Report was called, so a late one can arrive after the download has already
            // finished — and a bar that jumps back to half is a download that looks stuck.
            var progress = new Progress<int>(percent => DownloadedPercent = Math.Max(DownloadedPercent, percent));
            await source.DownloadAsync(progress, cancellationToken).ConfigureAwait(false);

            ReadyVersion = version;
            DownloadedPercent = 100;
            Status = isSessionLive()
                ? $"Version {version} is ready, and will be installed after this session."
                : $"Version {version} is ready. Restart Flint to use it.";
            OnPropertyChanged(nameof(CanRestart));
            RestartCommand.NotifyCanExecuteChanged();
        }
        catch (OperationCanceledException)
        {
            Status = "The update check stopped before it finished.";
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
        {
            // Named rather than swallowed: "could not check" with no reason is the kind of message
            // that leaves somebody unable to tell a broken build from a network they are not on.
            Status = $"The update check did not finish: {exception.Message}";
        }
        finally
        {
            IsWorking = false;
        }
    }

    /// <summary>Applies the downloaded version and restarts.</summary>
    [RelayCommand(CanExecute = nameof(CanRestart))]
    public void Restart()
    {
        if (CanRestart)
        {
            source.ApplyAndRestart();
        }
    }

    /// <summary>Re-evaluates whether restarting is safe, after a session starts or ends.</summary>
    public void SessionStateChanged()
    {
        OnPropertyChanged(nameof(CanRestart));
        RestartCommand.NotifyCanExecuteChanged();
        if (ReadyVersion is { } version)
        {
            Status = isSessionLive()
                ? $"Version {version} is ready, and will be installed after this session."
                : $"Version {version} is ready. Restart Flint to use it.";
        }
    }
}
