using System.Text;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flint.App.Services;

namespace Flint.App.ViewModels;

/// <summary>
/// Host-side Network / VPN editor for the active TV profile.
///
/// Paste lives here because a Fire TV D-pad cannot reasonably enter a WireGuard config. The tunnel
/// still runs only on the television after system consent (ADR-0022 / ADR-0001).
/// </summary>
public sealed partial class BrowserNetworkViewModel : ObservableObject
{
    private static readonly Regex InterfaceHeader = new(
        @"^\s*\[Interface\]\s*$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PeerHeader = new(
        @"^\s*\[Peer\]\s*$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private IBrowserCockpitRemote? remote;
    private string activeTvProfileId = string.Empty;
    private long activeEpoch;
    private long lastRevision;
    private bool waitingForSnapshot;
    private bool clearDraftWhenProvisioned;
    private bool applyingSnapshot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool isAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(UnavailableReason))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    private bool isTelevisionProfileActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool vpnEnabled;

    /// <summary>
    /// Turning VPN on implies the tunnel should start with the browser unless the user opts out
    /// afterward. Without this default, Enable+Save left auto-connect false and the tunnel idle.
    /// </summary>
    partial void OnVpnEnabledChanged(bool value)
    {
        if (applyingSnapshot)
        {
            return;
        }

        if (value)
        {
            AutoConnectOnBrowserStart = true;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool autoConnectOnBrowserStart = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool requireVpnBeforeBrowse;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfigHint))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string configText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    private bool configPresentOnTv;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    private string sessionStatus = "Idle";

    [ObservableProperty]
    private string? syncError;

    [ObservableProperty]
    private string capabilityReason = string.Empty;

    /// <summary>Compact status for the card header.</summary>
    public string StatusLabel => !IsAvailable
        ? "RECEIVER UPDATE REQUIRED"
        : waitingForSnapshot
            ? "WAITING FOR TV"
            : ConfigPresentOnTv
                ? $"{SessionStatus} · CONFIG ON TV"
                : SessionStatus;

    /// <summary>Why the card is inert when a Windows-device profile is selected.</summary>
    public string UnavailableReason => IsTelevisionProfileActive
        ? string.Empty
        : "VPN settings apply to named TV profiles only. Switch to a TV profile first.";

    /// <summary>Structural hint under the paste box — never echoes secrets.</summary>
    public string ConfigHint
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ConfigText))
            {
                return ConfigPresentOnTv
                    ? "A config is already stored on the TV. Paste a replacement to overwrite it."
                    : "Paste a full WireGuard config ([Interface] and [Peer]). Never shared in logs.";
            }

            return IsConfigStructurallyValid(ConfigText)
                ? "Config shape looks complete."
                : "Needs both [Interface] and [Peer] sections.";
        }
    }

    public bool CanEdit => IsAvailable && IsTelevisionProfileActive && !string.IsNullOrEmpty(activeTvProfileId);

    internal void Bind(IBrowserCockpitRemote? cockpit)
    {
        remote = cockpit;
        if (cockpit is null)
        {
            Reset(available: false);
            return;
        }

        // Unlock the editor as soon as a secure browser session exists. Feature discovery used to
        // wait for a TV network snapshot that often never arrived before the first Open.
        IsAvailable = true;
        _ = RefreshAsync();
    }

    internal void Reset(bool available)
    {
        IsAvailable = available;
        waitingForSnapshot = false;
        SyncError = null;
        if (!available)
        {
            VpnEnabled = false;
            AutoConnectOnBrowserStart = false;
            RequireVpnBeforeBrowse = false;
            ConfigText = string.Empty;
            ConfigPresentOnTv = false;
            SessionStatus = "Idle";
            CapabilityReason = string.Empty;
            clearDraftWhenProvisioned = false;
            lastRevision = 0;
        }

        SaveCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(ConfigHint));
        OnPropertyChanged(nameof(CanEdit));
    }

    internal void ApplyActiveProfile(bool televisionActive, string? tvProfileId, long epoch)
    {
        var nextProfileId = televisionActive ? tvProfileId ?? string.Empty : string.Empty;
        if (!StringComparer.Ordinal.Equals(activeTvProfileId, nextProfileId))
        {
            ConfigText = string.Empty;
            clearDraftWhenProvisioned = false;
            lastRevision = 0;
        }
        if (activeEpoch != epoch) lastRevision = 0;
        IsTelevisionProfileActive = televisionActive;
        activeTvProfileId = nextProfileId;
        activeEpoch = epoch;
        OnPropertyChanged(nameof(UnavailableReason));
        OnPropertyChanged(nameof(CanEdit));
        SaveCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
        if (IsAvailable && CanEdit)
        {
            _ = RefreshAsync();
        }
    }

    internal void Apply(BrowserNetworkSnapshot snapshot)
    {
        if (!IsTelevisionProfileActive ||
            snapshot.Epoch != activeEpoch ||
            snapshot.Revision <= lastRevision ||
            !StringComparer.Ordinal.Equals(snapshot.ProfileId, activeTvProfileId))
        {
            return;
        }
        lastRevision = snapshot.Revision;
        waitingForSnapshot = false;

        applyingSnapshot = true;
        try
        {
            VpnEnabled = snapshot.VpnEnabled;
            AutoConnectOnBrowserStart = snapshot.AutoConnectOnBrowserStart;
            RequireVpnBeforeBrowse = snapshot.RequireVpnBeforeBrowse;
        }
        finally
        {
            applyingSnapshot = false;
        }

        ConfigPresentOnTv = snapshot.ConfigPresent;
        CapabilityReason = snapshot.CapabilityReason;
        SessionStatus = FormatSession(snapshot);
        SyncError = null;
        if (clearDraftWhenProvisioned && snapshot.ConfigPresent)
        {
            ConfigText = string.Empty;
            clearDraftWhenProvisioned = false;
        }
        // Do not overwrite a draft the user is editing with an empty paste box when TV still has config.
        if (string.IsNullOrWhiteSpace(ConfigText) && !snapshot.ConfigPresent)
        {
            ConfigText = string.Empty;
        }

        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(ConfigHint));
        OnPropertyChanged(nameof(CanEdit));
        SaveCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (remote is null || !CanSave())
        {
            return;
        }

        try
        {
            SyncError = null;
            // Auto-connect is on by default whenever VPN is enabled so Enable+Save starts the tunnel.
            var autoConnect = VpnEnabled && AutoConnectOnBrowserStart;
            if (VpnEnabled && !AutoConnectOnBrowserStart)
            {
                AutoConnectOnBrowserStart = true;
                autoConnect = true;
            }

            await remote.SendNetworkCommandAsync(
                new BrowserNetworkRequest(
                    BrowserNetworkOperation.Set,
                    activeTvProfileId,
                    VpnEnabled,
                    VpnEnabled ? BrowserVpnProviderKind.WireGuard : BrowserVpnProviderKind.None,
                    autoConnect,
                    RequireVpnBeforeBrowse && VpnEnabled,
                    ConfigText.Trim()),
                CancellationToken.None).ConfigureAwait(true);
            clearDraftWhenProvisioned = VpnEnabled && !string.IsNullOrWhiteSpace(ConfigText);
            waitingForSnapshot = true;
            OnPropertyChanged(nameof(StatusLabel));
        }
        catch (Exception)
        {
            SyncError = "VPN settings could not be sent to the TV. Reconnect and try again.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanClear))]
    private async Task ClearAsync()
    {
        if (remote is null || !CanClear())
        {
            return;
        }

        try
        {
            SyncError = null;
            await remote.SendNetworkCommandAsync(
                new BrowserNetworkRequest(BrowserNetworkOperation.Clear, activeTvProfileId),
                CancellationToken.None).ConfigureAwait(true);
            VpnEnabled = false;
            AutoConnectOnBrowserStart = false;
            RequireVpnBeforeBrowse = false;
            ConfigText = string.Empty;
            clearDraftWhenProvisioned = false;
            ConfigPresentOnTv = false;
            waitingForSnapshot = true;
            OnPropertyChanged(nameof(StatusLabel));
            OnPropertyChanged(nameof(ConfigHint));
        }
        catch (Exception)
        {
            SyncError = "VPN settings could not be removed from the TV. Reconnect and try again.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (remote is null || !CanRefresh())
        {
            return;
        }

        try
        {
            SyncError = null;
            waitingForSnapshot = true;
            OnPropertyChanged(nameof(StatusLabel));
            await remote.SendNetworkCommandAsync(
                new BrowserNetworkRequest(BrowserNetworkOperation.RequestSnapshot, activeTvProfileId),
                CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception)
        {
            waitingForSnapshot = false;
            SyncError = "VPN status could not be refreshed. Reconnect and try again.";
            OnPropertyChanged(nameof(StatusLabel));
        }
    }

    private bool CanSave() =>
        CanEdit &&
        (!VpnEnabled || (IsConfigStructurallyValid(ConfigText) || ConfigPresentOnTv));

    private bool CanClear() => CanEdit && (VpnEnabled || ConfigPresentOnTv || !string.IsNullOrWhiteSpace(ConfigText));

    private bool CanRefresh() => CanEdit;

    private static bool IsConfigStructurallyValid(string configText)
    {
        if (string.IsNullOrWhiteSpace(configText))
        {
            return false;
        }

        var utf8Length = Encoding.UTF8.GetByteCount(configText);
        if (utf8Length is 0 or > 65_536)
        {
            return false;
        }

        return InterfaceHeader.IsMatch(configText) && PeerHeader.IsMatch(configText);
    }

    private static string FormatSession(BrowserNetworkSnapshot snapshot) =>
        snapshot.SessionState switch
        {
            BrowserVpnSessionKind.NeedsConsent => "Needs TV consent",
            BrowserVpnSessionKind.Connecting => "Connecting",
            BrowserVpnSessionKind.Connected => "Connected",
            BrowserVpnSessionKind.Failed => string.IsNullOrWhiteSpace(snapshot.SessionDetail)
                ? "Failed"
                : $"Failed · {snapshot.SessionDetail}",
            BrowserVpnSessionKind.Unavailable => string.IsNullOrWhiteSpace(snapshot.CapabilityReason)
                ? "Unavailable"
                : snapshot.CapabilityReason,
            _ => "Idle",
        };
}
