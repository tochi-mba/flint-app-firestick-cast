using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Flint.Core;

namespace Flint.App.ViewModels;

/// <summary>What the Settings page shows: what Flint remembers, and how it updates itself.</summary>
/// <remarks>
/// Its own view model rather than the Cast page's. The update panel belongs on this page and nowhere
/// near casting, and a page whose card reaches up to the window for its state cannot be rendered on
/// its own — the first version of this one rendered an empty card with dead buttons in the snapshot
/// that was supposed to prove it looked right.
/// </remarks>
public sealed class SettingsPageViewModel
{
    /// <summary>Builds the page over the state it shows.</summary>
    /// <param name="cast">Where the remembered televisions live.</param>
    /// <param name="updates">Finding and installing a newer Flint.</param>
    public SettingsPageViewModel(CastPageViewModel cast, UpdatesViewModel updates)
    {
        ArgumentNullException.ThrowIfNull(cast);
        ArgumentNullException.ThrowIfNull(updates);
        RecentAddresses = cast.RecentAddresses;
        ClearRecentAddressesCommand = cast.ClearRecentAddressesCommand;
        Updates = updates;
    }

    /// <summary>The saved direct endpoints, newest first.</summary>
    public ObservableCollection<RecentAddress> RecentAddresses { get; }

    /// <summary>Forgets every saved direct address, here and on disk.</summary>
    public IRelayCommand ClearRecentAddressesCommand { get; }

    /// <summary>Finding and installing a newer Flint.</summary>
    public UpdatesViewModel Updates { get; }
}
