namespace Flint.App.ViewModels.Settings;

/// <summary>The Updates section: finding and installing a newer Flint.</summary>
public sealed class UpdatesSectionViewModel : SettingsSectionViewModel
{
    /// <summary>Creates the section over the update client.</summary>
    public UpdatesSectionViewModel(UpdatesViewModel updates)
        : base("Updates") =>
        Updates = updates ?? throw new ArgumentNullException(nameof(updates));

    /// <summary>The update client.</summary>
    public UpdatesViewModel Updates { get; }

    /// <summary>Checking for updates.</summary>
    public SettingText CheckText { get; } = new(
        "Check for updates when Flint starts",
        "Updates come from this project's GitHub releases. Flint never restarts while something is on the TV.");

    /// <inheritdoc />
    public override IReadOnlyList<SettingText> Settings => [CheckText];
}
