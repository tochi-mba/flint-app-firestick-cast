using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flint.App.Services;
using Flint.Protocol;

namespace Flint.App.ViewModels;

/// <summary>The TV-owned tab strip, including the honest legacy single-page projection.</summary>
public sealed partial class BrowserTabsViewModel : ObservableObject
{
    /// <summary>Open pages in TV order.</summary>
    public ObservableCollection<BrowserTabItemViewModel> Items { get; } = [];

    /// <summary>Whether the connected session supports tab commands and snapshots.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyCanExecuteChangedFor(nameof(NewTabCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectTabCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseTabCommand))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateTabCommand))]
    private bool isAvailable;

    /// <summary>Active TV tab id, or zero before the extension publishes one.</summary>
    [ObservableProperty]
    private long activeId;

    private IBrowserCockpitRemote? remote;
    private Func<BrowserTabRequest, CancellationToken, Task<bool>>? beforeSend;

    /// <summary>Whether New Tab is a real operation on this receiver.</summary>
    public bool CanCreate => IsAvailable && Items.Count < 8;

    /// <summary>Compact capability label for the strip.</summary>
    public string StatusLabel => IsAvailable ? $"{Items.Count} OF 8" : "SINGLE PAGE";

    internal void ApplyLegacy(BrowserStateMessage state)
    {
        if (IsAvailable)
        {
            return;
        }

        Items.Clear();
        if (state.LoadState is BrowserLoadState.Idle or BrowserLoadState.Closed
            && string.IsNullOrWhiteSpace(state.Url))
        {
            ActiveId = 0;
            NotifyCounts();
            return;
        }

        ActiveId = 1;
        Items.Add(new BrowserTabItemViewModel(
            1,
            string.IsNullOrWhiteSpace(state.Title) ? "Current page" : state.Title,
            state.Url,
            state.Progress,
            state.LoadState == BrowserLoadState.Loading,
            IsActive: true,
            IsFrozen: false));
        NotifyCounts();
    }

    internal void Apply(BrowserTabsSnapshot snapshot)
    {
        IsAvailable = true;
        ActiveId = snapshot.ActiveTabId;
        Items.Clear();
        foreach (var tab in snapshot.Tabs.Take(8))
        {
            Items.Add(new BrowserTabItemViewModel(
                tab.Id,
                string.IsNullOrWhiteSpace(tab.Title) ? HostOrFallback(tab.Url) : tab.Title,
                tab.Url,
                Math.Clamp(tab.Progress, 0, 100),
                tab.IsLoading,
                tab.Id == snapshot.ActiveTabId,
                tab.IsFrozen));
        }
        NotifyCounts();
    }

    internal void Reset(bool available)
    {
        IsAvailable = available;
        ActiveId = 0;
        Items.Clear();
        NotifyCounts();
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(StatusLabel));
        // CanCreate is not an [ObservableProperty]; RelayCommand will not refresh itself unless told.
        NewTabCommand.NotifyCanExecuteChanged();
        SelectTabCommand.NotifyCanExecuteChanged();
        CloseTabCommand.NotifyCanExecuteChanged();
        DuplicateTabCommand.NotifyCanExecuteChanged();
    }

    private static string HostOrFallback(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) && !string.IsNullOrWhiteSpace(parsed.Host)
            ? parsed.Host
            : "New tab";

    /// <summary>
    /// Binds the cockpit channel these commands write to, or clears it on disconnect.
    /// </summary>
    /// <param name="cockpit">Authenticated tab channel, or null on detach.</param>
    /// <param name="beforeSend">
    /// Optional gate run before every tab wire send. Return false to skip the wire command
    /// (e.g. after reclaiming the TV by Open/Navigate because Close destroyed the old tab ids).
    /// </param>
    internal void Bind(
        IBrowserCockpitRemote? cockpit,
        Func<BrowserTabRequest, CancellationToken, Task<bool>>? beforeSend = null)
    {
        remote = cockpit;
        this.beforeSend = beforeSend;
        NewTabCommand.NotifyCanExecuteChanged();
        SelectTabCommand.NotifyCanExecuteChanged();
        CloseTabCommand.NotifyCanExecuteChanged();
        DuplicateTabCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private Task NewTabAsync(CancellationToken cancellationToken) =>
        Send(new BrowserTabRequest(BrowserTabOperation.New), cancellationToken);

    /// <summary>
    /// Opens <paramref name="url"/> in a new TV tab. Used when reclaiming a closed surface so the
    /// rest of the app-held profile session strip can be rebuilt after CLOSE destroyed TV tab ids.
    /// </summary>
    internal Task OpenUrlAsync(string url, CancellationToken cancellationToken = default) =>
        Send(new BrowserTabRequest(BrowserTabOperation.New, Url: url), cancellationToken);

    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private Task SelectTabAsync(long tabId, CancellationToken cancellationToken) =>
        Send(new BrowserTabRequest(BrowserTabOperation.Select, tabId), cancellationToken);

    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private Task CloseTabAsync(long tabId, CancellationToken cancellationToken) =>
        Send(new BrowserTabRequest(BrowserTabOperation.Close, tabId), cancellationToken);

    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private Task DuplicateTabAsync(long tabId, CancellationToken cancellationToken) =>
        Send(new BrowserTabRequest(BrowserTabOperation.Duplicate, tabId), cancellationToken);

    // Silent when there is no channel. A tab button on a receiver that has never sent a tab
    // snapshot is already disabled; this covers the race where the session drops between the click
    // and the send. Wire/IO failures must never become unhandled crashes (epoch=0 Select did).
    private async Task Send(BrowserTabRequest request, CancellationToken cancellationToken)
    {
        if (remote is null)
        {
            return;
        }

        try
        {
            if (beforeSend is not null && !await beforeSend(request, cancellationToken).ConfigureAwait(true))
            {
                return;
            }

            await remote.SendTabCommandAsync(request, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is WireFormatException or ArgumentException or IOException)
        {
            Flint.Core.FlintDiag.Warn(
                "FlintBrowser",
                $"tab command {request.Operation} failed: {exception.GetType().Name}");
        }
    }
}

/// <summary>One tab chip presented by the Windows cockpit.</summary>
public sealed record BrowserTabItemViewModel(
    long Id,
    string Title,
    string Url,
    int Progress,
    bool IsLoading,
    bool IsActive,
    bool IsFrozen)
{
    /// <summary>Stable one-character fallback while favicons are unavailable.</summary>
    public string Initial => Title.FirstOrDefault(char.IsLetterOrDigit) is var value && value != default
        ? char.ToUpperInvariant(value).ToString()
        : "•";

    /// <summary>Short state announced with the tab title.</summary>
    public string StateLabel => IsActive
        ? IsFrozen ? "Active tab, suspended" : IsLoading ? $"Active tab, loading {Progress}%" : "Active tab"
        : IsFrozen ? "Suspended" : IsLoading ? $"Loading {Progress}%" : "Ready";
}
