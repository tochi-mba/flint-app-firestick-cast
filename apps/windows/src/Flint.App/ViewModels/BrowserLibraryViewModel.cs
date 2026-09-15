using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flint.App.Services;
using Flint.Protocol;

namespace Flint.App.ViewModels;

/// <summary>
/// Presents the active browser library while keeping TV profiles and the Windows-device profile
/// under their respective storage owners.
/// </summary>
public sealed partial class BrowserLibraryViewModel : ObservableObject
{
    private readonly IBrowserProfileLibraryStore deviceStore;
    private BrowserProfileId deviceProfile;
    private IBrowserCockpitRemote? remote;
    private BrowserProfileStorageLocation activeStorage = BrowserProfileStorageLocation.Television;
    private long activeEpoch;
    private long profileRevision;
    private long tvLibraryRevision;
    private long deviceRevision;
    private long lastDeviceRequestId;
    private long lastVisitEpoch;
    private long lastVisitNavigationId;
    private bool waitingForTvLibrary;

    /// <summary>Creates a profile-aware library over a device-local persistent or test store.</summary>
    public BrowserLibraryViewModel(
        IBrowserProfileLibraryStore? deviceStore = null,
        BrowserProfileId? deviceProfile = null)
    {
        this.deviceStore = deviceStore ?? new InMemoryBrowserProfileLibraryStore();
        this.deviceProfile = deviceProfile ?? this.deviceStore.DeviceProfiles().Selected;
        RefreshDeviceProfiles();
    }

    /// <summary>Bookmarks belonging to the active storage owner.</summary>
    public ObservableCollection<BrowserLibraryItem> Bookmarks { get; } = [];

    /// <summary>History belonging to the active storage owner.</summary>
    public ObservableCollection<BrowserLibraryItem> History { get; } = [];

    /// <summary>The Windows device followed by every named persistent TV profile.</summary>
    public ObservableCollection<BrowserProfileOption> ProfileOptions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyCanExecuteChangedFor(nameof(AddBookmarkCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveBookmarkCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearHistoryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearBookmarksCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool isAvailable;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateTvProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameActiveTvProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowDeleteActiveTvProfileCommand))]
    private bool isProfileSelectionAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StorageHeading))]
    [NotifyPropertyChangedFor(nameof(StorageDetail))]
    [NotifyPropertyChangedFor(nameof(CanManageActiveTvProfile))]
    [NotifyCanExecuteChangedFor(nameof(RenameActiveTvProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowDeleteActiveTvProfileCommand))]
    private bool isUsingWindowsDevice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StorageHeading))]
    [NotifyPropertyChangedFor(nameof(StorageDetail))]
    [NotifyPropertyChangedFor(nameof(ProfileStatusLabel))]
    private string activeProfileName = "This TV";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfileStatusLabel))]
    private string? pendingProfileName;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateTvProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameActiveTvProfileCommand))]
    private string draftTvProfileName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TvProfileCountLabel))]
    [NotifyPropertyChangedFor(nameof(CanCreateTvProfile))]
    [NotifyPropertyChangedFor(nameof(CanDeleteActiveTvProfile))]
    [NotifyCanExecuteChangedFor(nameof(CreateTvProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowDeleteActiveTvProfileCommand))]
    private int tvProfileCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanManageActiveTvProfile))]
    [NotifyPropertyChangedFor(nameof(CanDeleteActiveTvProfile))]
    [NotifyCanExecuteChangedFor(nameof(RenameActiveTvProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowDeleteActiveTvProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmDeleteActiveTvProfileCommand))]
    private string? activeTvProfileId;

    [ObservableProperty]
    private bool deleteTvProfileConfirmationVisible;

    [ObservableProperty]
    private string? syncError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryRestoreCommand))]
    private bool canRetryRestore;

    internal Action? RetryProfileRestore;

    /// <summary>Compact count or loading/capability state.</summary>
    public string StatusLabel => !IsAvailable
        ? "RECEIVER UPDATE REQUIRED"
        : waitingForTvLibrary
            ? "WAITING FOR TV"
            : $"{Bookmarks.Count} SAVED · {History.Count} RECENT";

    /// <summary>The active choice, including an in-flight selection.</summary>
    public string ProfileStatusLabel => PendingProfileName is { Length: > 0 } pending
        ? $"Switching to {pending}…"
        : IsUsingWindowsDevice
            ? $"{ActiveProfileName} · DEVICE"
            : $"{ActiveProfileName} · TV";

    /// <summary>Short, explicit owner heading for the library below.</summary>
    public string StorageHeading => IsUsingWindowsDevice
        ? "SAVED ON THIS WINDOWS DEVICE"
        : ActiveProfileName == "This TV"
            ? "SAVED ON THE TV"
            : $"SAVED IN {ActiveProfileName.ToUpperInvariant()} ON THE TV";

    /// <summary>Explains persistence without implying the TV stores a connected device profile.</summary>
    public string StorageDetail => IsUsingWindowsDevice
        ? "Bookmarks and history are stored under this Windows account. The TV receives a temporary copy for this secure session and does not save it as a TV profile."
        : $"Bookmarks and history are stored on this television in the {ActiveProfileName} profile and are available without a connected device.";

    /// <summary>Profiles do not over-promise WebView data isolation that Fire OS cannot provide.</summary>
    public string ProfilePrivacyNote =>
        "Profile choices change where bookmarks and history are stored and do not separate cookies, site storage, or sign-ins on the TV.";

    /// <summary>How many named libraries already live on the television.</summary>
    public string TvProfileCountLabel =>
        $"{TvProfileCount} of {BrowserWireLimits.MaxTvProfiles} TV profiles";

    /// <summary>True when the receiver can still accept another named TV library.</summary>
    public bool CanCreateTvProfile =>
        IsProfileSelectionAvailable && TvProfileCount < BrowserWireLimits.MaxTvProfiles;

    /// <summary>Rename and delete only apply to a currently selected TV-owned profile.</summary>
    public bool CanManageActiveTvProfile =>
        IsProfileSelectionAvailable && !IsUsingWindowsDevice && !string.IsNullOrEmpty(ActiveTvProfileId);

    /// <summary>The television must keep at least one durable profile.</summary>
    public bool CanDeleteActiveTvProfile => CanManageActiveTvProfile && TvProfileCount > 1;

    /// <summary>Applies the receiver-authoritative catalog and active storage owner.</summary>
    internal void ApplyProfiles(BrowserProfilesSnapshot snapshot)
    {
        if (snapshot.Epoch < activeEpoch || snapshot.Epoch == activeEpoch && snapshot.Revision <= profileRevision)
        {
            return;
        }

        if (snapshot.Epoch != activeEpoch)
        {
            activeEpoch = snapshot.Epoch;
            profileRevision = 0;
            tvLibraryRevision = 0;
            deviceRevision = 0;
            lastDeviceRequestId = 0;
            lastVisitEpoch = 0;
            lastVisitNavigationId = 0;
        }
        profileRevision = snapshot.Revision;
        activeStorage = snapshot.ActiveStorage;
        IsUsingWindowsDevice = activeStorage == BrowserProfileStorageLocation.WindowsDevice;
        IsProfileSelectionAvailable = true;
        PendingProfileName = null;
        DeleteTvProfileConfirmationVisible = false;

        var tvProfiles = snapshot.TvProfiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.ProfileId))
            .DistinctBy(profile => profile.ProfileId, StringComparer.Ordinal)
            .Take(BrowserWireLimits.MaxTvProfiles)
            .ToArray();
        TvProfileCount = tvProfiles.Length;
        var activeTv = tvProfiles.FirstOrDefault(profile =>
            StringComparer.Ordinal.Equals(profile.ProfileId, snapshot.ActiveProfileId));
        ActiveTvProfileId = IsUsingWindowsDevice ? null : activeTv?.ProfileId;
        ActiveProfileName = IsUsingWindowsDevice
            ? DeviceProfiles.FirstOrDefault(p => p.Id == deviceProfile)?.Name ?? "This Windows device"
            : activeTv?.Name ?? "TV profile";
        if (CanManageActiveTvProfile && string.IsNullOrWhiteSpace(DraftTvProfileName))
        {
            DraftTvProfileName = activeTv?.Name ?? string.Empty;
        }

        ProfileOptions.Clear();
        ProfileOptions.Add(new BrowserProfileOption(
            ProfileId: string.Empty,
            Name: "This Windows device",
            Storage: BrowserProfileStorageLocation.WindowsDevice,
            IsActive: IsUsingWindowsDevice,
            DeviceName: snapshot.DeviceName));
        foreach (var profile in tvProfiles)
        {
            ProfileOptions.Add(new BrowserProfileOption(
                profile.ProfileId,
                profile.Name,
                BrowserProfileStorageLocation.Television,
                !IsUsingWindowsDevice && StringComparer.Ordinal.Equals(profile.ProfileId, snapshot.ActiveProfileId)));
        }

        if (IsUsingWindowsDevice)
        {
            waitingForTvLibrary = false;
            IsAvailable = true;
            RefreshDeviceView();
            QueueDeviceSnapshot();
        }
        else
        {
            waitingForTvLibrary = true;
            IsAvailable = true;
            ClearVisibleLibrary();
        }
        NotifyPresentation();
        NotifyProfileManagement();
    }

    /// <summary>Applies only a TV-owned library; stale TV data cannot overwrite a device profile.</summary>
    internal void Apply(BrowserLibrarySnapshot snapshot)
    {
        if (IsUsingWindowsDevice || snapshot.Epoch < activeEpoch ||
            snapshot.Epoch == activeEpoch && snapshot.Revision <= tvLibraryRevision)
        {
            return;
        }

        activeEpoch = snapshot.Epoch;
        tvLibraryRevision = snapshot.Revision;
        waitingForTvLibrary = false;
        IsAvailable = true;
        ReplaceVisibleLibrary(snapshot.Entries);
    }

    /// <summary>Applies a receiver-to-device mutation once and returns a full replacement snapshot.</summary>
    internal async Task HandleDeviceRequestAsync(
        BrowserLibraryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsUsingWindowsDevice || request.Epoch != activeEpoch ||
            request.CommandId <= lastDeviceRequestId)
        {
            return;
        }

        lastDeviceRequestId = request.CommandId;
        var saved = request.Operation switch
        {
            BrowserLibraryOperation.AddBookmark =>
                deviceStore.AddBookmark(deviceProfile, request.Url, request.Title),
            BrowserLibraryOperation.RemoveBookmark =>
                deviceStore.RemoveBookmark(deviceProfile, request.Url),
            BrowserLibraryOperation.ClearHistory => deviceStore.ClearHistory(deviceProfile),
            BrowserLibraryOperation.ClearBookmarks => deviceStore.ClearBookmarks(deviceProfile),
            BrowserLibraryOperation.RequestSnapshot => true,
            _ => false,
        };
        if (!saved && request.Operation is not BrowserLibraryOperation.RemoveBookmark)
        {
            SyncError = "The Windows-device browser profile could not be updated.";
        }

        RefreshDeviceView();
        await SendDeviceSnapshotAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Records one completed TV navigation when Windows owns the active library.</summary>
    internal async Task ObservePageAsync(
        BrowserStateMessage state,
        CancellationToken cancellationToken = default)
    {
        if (!IsUsingWindowsDevice || state.Epoch != activeEpoch || state.NavigationId <= 0 ||
            state.LoadState != BrowserLoadState.Loaded || string.IsNullOrWhiteSpace(state.Url) ||
            state.Epoch == lastVisitEpoch && state.NavigationId == lastVisitNavigationId)
        {
            return;
        }

        lastVisitEpoch = state.Epoch;
        lastVisitNavigationId = state.NavigationId;
        if (!deviceStore.RecordVisit(deviceProfile, state.Url, state.Title))
        {
            SyncError = "The completed page could not be saved to this Windows device.";
            return;
        }

        RefreshDeviceView();
        await SendDeviceSnapshotAsync(cancellationToken).ConfigureAwait(true);
    }

    internal void Reset(bool libraryAvailable, bool profilesAvailable = false)
    {
        IsAvailable = libraryAvailable;
        IsProfileSelectionAvailable = profilesAvailable;
        IsUsingWindowsDevice = false;
        activeStorage = BrowserProfileStorageLocation.Television;
        activeEpoch = 0;
        profileRevision = 0;
        tvLibraryRevision = 0;
        deviceRevision = 0;
        lastDeviceRequestId = 0;
        waitingForTvLibrary = false;
        ActiveProfileName = "This TV";
        ActiveTvProfileId = null;
        TvProfileCount = 0;
        DraftTvProfileName = string.Empty;
        PendingProfileName = null;
        DeleteTvProfileConfirmationVisible = false;
        SyncError = null;
        CanRetryRestore = false;
        ProfileOptions.Clear();
        ClearVisibleLibrary();
        NotifyPresentation();
        NotifyProfileManagement();
    }

    /// <summary>Binds the cockpit channel these commands write to, or clears it on disconnect.</summary>
    internal void Bind(IBrowserCockpitRemote? cockpit)
    {
        remote = cockpit;
        NotifyCommands();
    }

    [RelayCommand(CanExecute = nameof(IsProfileSelectionAvailable))]
    private async Task SelectProfileAsync(
        BrowserProfileOption? option,
        CancellationToken cancellationToken)
    {
        if (option is null || option.IsActive || remote is null)
        {
            return;
        }

        PendingProfileName = option.Name;
        DeleteTvProfileConfirmationVisible = false;
        try
        {
            await remote.SendProfileCommandAsync(
                option.Storage == BrowserProfileStorageLocation.WindowsDevice
                    ? new BrowserProfileRequest(BrowserProfileOperation.SelectWindowsDevice)
                    : new BrowserProfileRequest(BrowserProfileOperation.SelectTvProfile, option.ProfileId),
                cancellationToken).ConfigureAwait(true);
            SyncError = null;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or OperationCanceledException)
        {
            PendingProfileName = null;
            SyncError = SafeMessage(exception);
        }
    }

    [RelayCommand(CanExecute = nameof(CanSubmitNewTvProfile))]
    private async Task CreateTvProfileAsync(CancellationToken cancellationToken)
    {
        var name = DraftTvProfileName.Trim();
        if (name.Length == 0 || remote is null)
        {
            return;
        }

        PendingProfileName = name;
        try
        {
            await remote.SendProfileCommandAsync(
                new BrowserProfileRequest(BrowserProfileOperation.CreateTvProfile, Name: name),
                cancellationToken).ConfigureAwait(true);
            DraftTvProfileName = string.Empty;
            SyncError = null;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or OperationCanceledException)
        {
            PendingProfileName = null;
            SyncError = SafeMessage(exception);
        }
    }

    [RelayCommand(CanExecute = nameof(CanSubmitRenameActiveTvProfile))]
    private async Task RenameActiveTvProfileAsync(CancellationToken cancellationToken)
    {
        var name = DraftTvProfileName.Trim();
        if (!CanManageActiveTvProfile || name.Length == 0 || remote is null || ActiveTvProfileId is null)
        {
            return;
        }

        PendingProfileName = name;
        try
        {
            await remote.SendProfileCommandAsync(
                new BrowserProfileRequest(
                    BrowserProfileOperation.RenameTvProfile,
                    ActiveTvProfileId,
                    name),
                cancellationToken).ConfigureAwait(true);
            SyncError = null;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or OperationCanceledException)
        {
            PendingProfileName = null;
            SyncError = SafeMessage(exception);
        }
    }

    private bool CanSubmitNewTvProfile() =>
        CanCreateTvProfile && !string.IsNullOrWhiteSpace(DraftTvProfileName);

    private bool CanSubmitRenameActiveTvProfile() =>
        CanManageActiveTvProfile && !string.IsNullOrWhiteSpace(DraftTvProfileName);

    [RelayCommand(CanExecute = nameof(CanDeleteActiveTvProfile))]
    private void ShowDeleteActiveTvProfile() => DeleteTvProfileConfirmationVisible = true;

    [RelayCommand]
    private void CancelDeleteActiveTvProfile() => DeleteTvProfileConfirmationVisible = false;

    [RelayCommand(CanExecute = nameof(CanDeleteActiveTvProfile))]
    private async Task ConfirmDeleteActiveTvProfileAsync(CancellationToken cancellationToken)
    {
        if (!CanDeleteActiveTvProfile || remote is null || ActiveTvProfileId is null)
        {
            return;
        }

        var profileId = ActiveTvProfileId;
        var name = ActiveProfileName;
        PendingProfileName = name;
        DeleteTvProfileConfirmationVisible = false;
        try
        {
            await remote.SendProfileCommandAsync(
                new BrowserProfileRequest(BrowserProfileOperation.DeleteTvProfile, profileId),
                cancellationToken).ConfigureAwait(true);
            SyncError = null;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or OperationCanceledException)
        {
            PendingProfileName = null;
            SyncError = SafeMessage(exception);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRetryRestore))]
    private void RetryRestore()
    {
        CanRetryRestore = false;
        RetryProfileRestore?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private async Task AddBookmarkAsync(BrowserLibraryItem? item, CancellationToken cancellationToken)
    {
        if (item is null)
        {
            return;
        }

        if (!IsUsingWindowsDevice)
        {
            await SendTvRequestAsync(
                new BrowserLibraryRequest(BrowserLibraryOperation.AddBookmark, item.Url, item.Title),
                cancellationToken).ConfigureAwait(true);
            return;
        }

        if (deviceStore.AddBookmark(deviceProfile, item.Url, item.Title, item.FaviconId))
        {
            RefreshDeviceView();
            await SendDeviceSnapshotAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private async Task RemoveBookmarkAsync(BrowserLibraryItem? item, CancellationToken cancellationToken)
    {
        if (item is null)
        {
            return;
        }

        if (!IsUsingWindowsDevice)
        {
            await SendTvRequestAsync(
                new BrowserLibraryRequest(BrowserLibraryOperation.RemoveBookmark, item.Url, item.Title),
                cancellationToken).ConfigureAwait(true);
            return;
        }

        if (deviceStore.RemoveBookmark(deviceProfile, item.Url))
        {
            RefreshDeviceView();
            await SendDeviceSnapshotAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private Task ClearHistoryAsync(CancellationToken cancellationToken) =>
        ClearAsync(BrowserLibraryOperation.ClearHistory, cancellationToken);

    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private Task ClearBookmarksAsync(CancellationToken cancellationToken) =>
        ClearAsync(BrowserLibraryOperation.ClearBookmarks, cancellationToken);

    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!IsUsingWindowsDevice)
        {
            return SendTvRequestAsync(
                new BrowserLibraryRequest(BrowserLibraryOperation.RequestSnapshot),
                cancellationToken);
        }

        RefreshDeviceView();
        return SendDeviceSnapshotAsync(cancellationToken);
    }

    private async Task ClearAsync(BrowserLibraryOperation operation, CancellationToken cancellationToken)
    {
        if (!IsUsingWindowsDevice)
        {
            await SendTvRequestAsync(new BrowserLibraryRequest(operation), cancellationToken).ConfigureAwait(true);
            return;
        }

        var saved = operation == BrowserLibraryOperation.ClearHistory
            ? deviceStore.ClearHistory(deviceProfile)
            : deviceStore.ClearBookmarks(deviceProfile);
        if (saved)
        {
            RefreshDeviceView();
            await SendDeviceSnapshotAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    private void RefreshDeviceView()
    {
        var snapshot = deviceStore.Snapshot(deviceProfile);
        ReplaceVisibleLibrary(
        [
            .. snapshot.Bookmarks.Select(entry => ToLibraryItem(entry, BrowserLibraryItemKind.Bookmark)),
            .. snapshot.History.Select(entry => ToLibraryItem(entry, BrowserLibraryItemKind.History)),
        ]);
    }

    private void ReplaceVisibleLibrary(IEnumerable<BrowserLibraryItem> entries)
    {
        Bookmarks.Clear();
        History.Clear();
        foreach (var entry in entries)
        {
            (entry.Kind == BrowserLibraryItemKind.Bookmark ? Bookmarks : History).Add(entry);
        }
        NotifyPresentation();
    }

    private void ClearVisibleLibrary()
    {
        Bookmarks.Clear();
        History.Clear();
    }

    private void QueueDeviceSnapshot() => _ = SendDeviceSnapshotAsync(CancellationToken.None);

    private async Task SendDeviceSnapshotAsync(CancellationToken cancellationToken)
    {
        if (remote is null || !IsUsingWindowsDevice || activeEpoch <= 0)
        {
            return;
        }

        var stored = deviceStore.Snapshot(deviceProfile);
        var revision = deviceRevision == long.MaxValue ? long.MaxValue : ++deviceRevision;
        var snapshot = new BrowserLibrarySnapshot(
            activeEpoch,
            revision,
            [
                .. stored.Bookmarks.Select(entry => ToLibraryItem(entry, BrowserLibraryItemKind.Bookmark)),
                .. stored.History.Select(entry => ToLibraryItem(entry, BrowserLibraryItemKind.History)),
            ]);
        try
        {
            await remote.SendLibraryStateAsync(snapshot, cancellationToken).ConfigureAwait(true);
            SyncError = null;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or OperationCanceledException)
        {
            SyncError = SafeMessage(exception);
        }
    }

    private async Task SendTvRequestAsync(
        BrowserLibraryRequest request,
        CancellationToken cancellationToken)
    {
        if (remote is null)
        {
            return;
        }

        try
        {
            await remote.SendLibraryCommandAsync(request, cancellationToken).ConfigureAwait(true);
            SyncError = null;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or OperationCanceledException)
        {
            SyncError = SafeMessage(exception);
        }
    }

    private void NotifyPresentation()
    {
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(ProfileStatusLabel));
        OnPropertyChanged(nameof(StorageHeading));
        OnPropertyChanged(nameof(StorageDetail));
        OnPropertyChanged(nameof(TvProfileCountLabel));
        OnPropertyChanged(nameof(CanCreateTvProfile));
        OnPropertyChanged(nameof(CanManageActiveTvProfile));
        OnPropertyChanged(nameof(CanDeleteActiveTvProfile));
    }

    private void NotifyProfileManagement()
    {
        CreateTvProfileCommand.NotifyCanExecuteChanged();
        RenameActiveTvProfileCommand.NotifyCanExecuteChanged();
        ShowDeleteActiveTvProfileCommand.NotifyCanExecuteChanged();
        ConfirmDeleteActiveTvProfileCommand.NotifyCanExecuteChanged();
    }

    private void NotifyCommands()
    {
        AddBookmarkCommand.NotifyCanExecuteChanged();
        RemoveBookmarkCommand.NotifyCanExecuteChanged();
        ClearHistoryCommand.NotifyCanExecuteChanged();
        ClearBookmarksCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        SelectProfileCommand.NotifyCanExecuteChanged();
        RetryRestoreCommand.NotifyCanExecuteChanged();
        NotifyProfileManagement();
    }

    private static BrowserLibraryItem ToLibraryItem(
        BrowserProfileLibraryEntry entry,
        BrowserLibraryItemKind kind) =>
        new(
            kind,
            entry.Url,
            entry.Title,
            DateTimeOffset.FromUnixTimeMilliseconds(Math.Clamp(
                entry.LastVisitedMilliseconds,
                0,
                DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())),
            entry.FaviconId);

    private static string SafeMessage(Exception exception) =>
        string.IsNullOrWhiteSpace(exception.Message)
            ? "The browser profile could not be synchronized."
            : exception.Message.Length <= 240 ? exception.Message : exception.Message[..240];
}
