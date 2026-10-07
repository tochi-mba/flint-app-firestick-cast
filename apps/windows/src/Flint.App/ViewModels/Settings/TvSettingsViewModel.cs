using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Flint.Core;

namespace Flint.App.ViewModels.Settings;

/// <summary>The TVs section: the TVs Flint can reach again, and the addresses typed to find them.</summary>
public sealed class TvSettingsViewModel : SettingsSectionViewModel
{
    private readonly CastPageViewModel cast;

    /// <summary>Creates the section over the Cast page's remembered TVs and addresses.</summary>
    public TvSettingsViewModel(CastPageViewModel cast)
        : base("TVs")
    {
        this.cast = cast ?? throw new ArgumentNullException(nameof(cast));
        RecentAddresses = cast.RecentAddresses;
        RecentAddresses.CollectionChanged += (_, _) => RaiseHasAny();
        cast.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(CastPageViewModel.KnownTvs))
            {
                RefreshTvs();
            }
        };
        RefreshTvs();
        ForgetAll = new ConfirmableAction(
            "FORGET ALL TVS",
            "Forget every TV, its login and every address Flint remembers? Each TV will ask for its code again.",
            "FORGET",
            () =>
            {
                cast.ForgetAllTvs();
                cast.ClearRecentAddressesCommand.Execute(null);
            });
    }

    /// <summary>The remembered TVs.</summary>
    public SettingText RememberedText { get; } = new(
        "Remembered TVs",
        "TVs you paired with, newest first, kept only on this PC with their logins protected for your Windows account.");

    /// <summary>How long a login lasts.</summary>
    public SettingText LoginLimitText { get; } = new(
        "How long a TV remembers this PC",
        "A TV keeps this PC's login only while its Flint app keeps running. After the TV restarts, or "
            + "after New code is pressed on it, the first connection needs the code once.");

    /// <summary>The addresses typed to find a TV.</summary>
    public SettingText AddressesText { get; } = new(
        "Typed addresses",
        "Addresses you typed on the Cast page to reach a TV, newest first.");

    /// <inheritdoc />
    public override IReadOnlyList<SettingText> Settings => [RememberedText, LoginLimitText, AddressesText];

    /// <summary>The remembered TVs, newest first.</summary>
    public ObservableCollection<KnownTvRow> Tvs { get; } = [];

    /// <summary>The saved direct addresses, newest first.</summary>
    public ObservableCollection<RecentAddress> RecentAddresses { get; }

    /// <summary>Whether any TV is remembered.</summary>
    public bool HasTvs => Tvs.Count > 0;

    /// <summary>Whether any address is remembered.</summary>
    public bool HasRecentAddresses => RecentAddresses.Count > 0;

    /// <summary>Whether there is anything to forget.</summary>
    public bool HasAnything => HasTvs || HasRecentAddresses;

    /// <summary>Forgets every remembered TV and address.</summary>
    public ConfirmableAction ForgetAll { get; }

    private void RefreshTvs()
    {
        Tvs.Clear();
        foreach (var tv in cast.KnownTvs)
        {
            Tvs.Add(new KnownTvRow(tv, () => cast.ForgetTv(tv.Name)));
        }

        RaiseHasAny();
    }

    private void RaiseHasAny()
    {
        OnPropertyChanged(nameof(HasTvs));
        OnPropertyChanged(nameof(HasRecentAddresses));
        OnPropertyChanged(nameof(HasAnything));
    }
}

/// <summary>One remembered TV, as the TVs section lists it.</summary>
public sealed class KnownTvRow
{
    /// <summary>Wraps <paramref name="tv"/>, forgotten by <paramref name="forget"/>.</summary>
    public KnownTvRow(KnownTv tv, Action forget)
    {
        Tv = tv ?? throw new ArgumentNullException(nameof(tv));
        ArgumentNullException.ThrowIfNull(forget);
        ForgetCommand = new RelayCommand(forget);
    }

    /// <summary>The TV.</summary>
    public KnownTv Tv { get; }

    /// <summary>The TV's name.</summary>
    public string Name => Tv.Name;

    /// <summary>Where it is, when it was last used, and whether it still has a login.</summary>
    public string Detail => string.Create(
        CultureInfo.CurrentCulture,
        $"{Tv.Address}  ·  last used {Tv.LastConnected.LocalDateTime:d MMM}  ·  {(Tv.HasLogin ? "connects without a code" : "needs its code next time")}");

    /// <summary>Forgets this TV: its login and its address.</summary>
    public IRelayCommand ForgetCommand { get; }

    /// <summary>The Forget button's name for a screen reader.</summary>
    public string ForgetName => $"Forget {Name}";
}
