using System.Collections.ObjectModel;
using Flint.Core;

namespace Flint.App.ViewModels.Settings;

/// <summary>The TVs section: the TVs Flint remembers how to reach.</summary>
public sealed class TvSettingsViewModel : SettingsSectionViewModel
{
    /// <summary>Creates the section over the Cast page's remembered addresses.</summary>
    public TvSettingsViewModel(CastPageViewModel cast)
        : base("TVs")
    {
        ArgumentNullException.ThrowIfNull(cast);
        RecentAddresses = cast.RecentAddresses;
        RecentAddresses.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecentAddresses));
        ForgetAll = new ConfirmableAction(
            "FORGET ALL TVS",
            "Forget every TV address Flint remembers? You can type one again on the Cast page.",
            "FORGET",
            () => cast.ClearRecentAddressesCommand.Execute(null));
    }

    /// <summary>The remembered addresses.</summary>
    public SettingText RememberedText { get; } = new(
        "Remembered TVs",
        "Addresses you typed to reach a TV, newest first. They are kept only on this PC.");

    /// <inheritdoc />
    public override IReadOnlyList<SettingText> Settings => [RememberedText];

    /// <summary>The saved direct addresses, newest first.</summary>
    public ObservableCollection<RecentAddress> RecentAddresses { get; }

    /// <summary>Whether there is anything to list or forget.</summary>
    public bool HasRecentAddresses => RecentAddresses.Count > 0;

    /// <summary>Forgets every remembered address.</summary>
    public ConfirmableAction ForgetAll { get; }
}
