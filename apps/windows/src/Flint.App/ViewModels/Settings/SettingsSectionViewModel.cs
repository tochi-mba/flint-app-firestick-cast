using CommunityToolkit.Mvvm.ComponentModel;

namespace Flint.App.ViewModels.Settings;

/// <summary>A setting's name and the one sentence that explains it.</summary>
/// <remarks>
/// The page shows these and the search reads them, from the same object, so a setting can never be
/// found under words it is not shown with.
/// </remarks>
/// <param name="Title">What the setting is called on the page.</param>
/// <param name="Description">What it does, in one sentence.</param>
public sealed record SettingText(string Title, string Description)
{
    /// <summary>Whether every word of <paramref name="query"/> appears in the title or description.</summary>
    public bool Matches(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length > 0 && words.All(word =>
            Title.Contains(word, StringComparison.OrdinalIgnoreCase)
            || Description.Contains(word, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>One section of the Settings page.</summary>
public abstract class SettingsSectionViewModel : ObservableObject
{
    /// <summary>Creates a section.</summary>
    /// <param name="title">The section's name in the list.</param>
    protected SettingsSectionViewModel(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Title = title;
    }

    /// <summary>The section's name in the list.</summary>
    public string Title { get; }

    /// <summary>Every setting in the section, for search.</summary>
    public abstract IReadOnlyList<SettingText> Settings { get; }
}

/// <summary>One setting that matched a search, and the section it lives in.</summary>
/// <param name="Section">Where the setting is.</param>
/// <param name="Setting">The setting.</param>
public sealed record SettingSearchResult(SettingsSectionViewModel Section, SettingText Setting)
{
    /// <summary>The section's name, for the label above the result.</summary>
    public string SectionTitle => Section.Title;
}
