using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flint.App.ViewModels.Settings;

namespace Flint.App.ViewModels;

/// <summary>
/// The Settings page: a list of sections, the one that is open, and a search across all of them.
/// </summary>
/// <remarks>
/// <para>
/// Every change applies the moment it is made; there is no Save button. Each section is its own
/// view model, so a section can be tested and rendered alone and none of them has to know about the
/// others.
/// </para>
/// <para>
/// Search reads the same title and sentence each setting is shown with, and every word typed must
/// appear in one or the other. Choosing a result opens its section and clears the search.
/// </para>
/// </remarks>
public sealed partial class SettingsPageViewModel : ObservableObject
{
    /// <summary>Builds the page over its sections, in the order they are listed.</summary>
    public SettingsPageViewModel(IReadOnlyList<SettingsSectionViewModel> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        if (sections.Count == 0)
        {
            throw new ArgumentException("The Settings page needs at least one section.", nameof(sections));
        }

        Sections = sections;
        _selectedSection = sections[0];
    }

    /// <summary>The sections, in list order.</summary>
    public IReadOnlyList<SettingsSectionViewModel> Sections { get; }

    /// <summary>The section that is open.</summary>
    [ObservableProperty]
    private SettingsSectionViewModel _selectedSection;

    /// <summary>What has been typed into the search box.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching), nameof(HasNoResults), nameof(NoResultsText))]
    private string _searchText = string.Empty;

    /// <summary>The settings that match the search, in section order.</summary>
    public ObservableCollection<SettingSearchResult> SearchResults { get; } = [];

    /// <summary>Whether a search is showing instead of a section.</summary>
    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

    /// <summary>Whether a search is showing and found nothing.</summary>
    public bool HasNoResults => IsSearching && SearchResults.Count == 0;

    /// <summary>What an empty search says.</summary>
    public string NoResultsText => $"No settings match “{SearchText.Trim()}”.";

    /// <summary>The section of a given kind, for the shell's own wiring and for tests.</summary>
    public T Section<T>()
        where T : SettingsSectionViewModel => Sections.OfType<T>().Single();

    partial void OnSearchTextChanged(string value)
    {
        SearchResults.Clear();
        if (!string.IsNullOrWhiteSpace(value))
        {
            foreach (var section in Sections)
            {
                foreach (var setting in section.Settings.Where(setting => setting.Matches(value)))
                {
                    SearchResults.Add(new SettingSearchResult(section, setting));
                }
            }
        }

        OnPropertyChanged(nameof(HasNoResults));
    }

    /// <summary>Opens the section a search result lives in and clears the search.</summary>
    [RelayCommand]
    private void OpenResult(SettingSearchResult? result)
    {
        if (result is null)
        {
            return;
        }

        SelectedSection = result.Section;
        SearchText = string.Empty;
    }

    /// <summary>Clears the search and returns to the section that was open.</summary>
    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;
}
