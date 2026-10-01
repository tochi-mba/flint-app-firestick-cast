using Flint.Core.Settings;

namespace Flint.App.ViewModels.Settings;

/// <summary>The General section: how Flint behaves around the TV and how large it draws itself.</summary>
public sealed class GeneralSettingsViewModel : SettingsSectionViewModel
{
    private readonly ISettingsService settings;

    /// <summary>Creates the section over the live settings.</summary>
    public GeneralSettingsViewModel(ISettingsService settings)
        : base("General")
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        settings.Changed += (_, change) =>
        {
            if (change.Previous.General != change.Current.General)
            {
                OnPropertyChanged(string.Empty);
            }
        };
        Reset = new ConfirmableAction(
            "RESET GENERAL SETTINGS",
            "Put every General setting back to how Flint came?",
            "RESET",
            () => settings.Update(current => current with { General = new GeneralSettings() }));
    }

    /// <summary>The question before switching what the TV shows.</summary>
    public SettingText AskBeforeSwitchingText { get; } = new(
        "Ask before switching what the TV shows",
        "When something from this PC is already on the TV, Flint checks with you before replacing it.");

    /// <summary>Keeping the PC awake.</summary>
    public SettingText KeepAwakeText { get; } = new(
        "Keep this PC awake while sharing or playing",
        "Stops Windows sleeping or turning the screen off while your screen or a file is on the TV.");

    /// <summary>The interface size.</summary>
    public SettingText InterfaceSizeText { get; } = new(
        "Interface size",
        "Makes everything in the Flint window larger or smaller.");

    /// <inheritdoc />
    public override IReadOnlyList<SettingText> Settings => [AskBeforeSwitchingText, KeepAwakeText, InterfaceSizeText];

    /// <summary>Whether Flint asks before switching what the TV shows.</summary>
    public bool AskBeforeSwitching
    {
        get => settings.Current.General.AskBeforeSwitching;
        set => settings.Update(current => current with { General = current.General with { AskBeforeSwitching = value } });
    }

    /// <summary>Whether this PC is kept awake while something from it is on the TV.</summary>
    public bool KeepAwake
    {
        get => settings.Current.General.KeepAwake;
        set => settings.Update(current => current with { General = current.General with { KeepAwake = value } });
    }

    /// <summary>The interface sizes offered.</summary>
    public IReadOnlyList<InterfaceScaleChoice> InterfaceScales { get; } =
        [.. GeneralSettings.InterfaceScales.Select(percent => new InterfaceScaleChoice(percent))];

    /// <summary>The interface size in use.</summary>
    public InterfaceScaleChoice? InterfaceScale
    {
        get => InterfaceScales.FirstOrDefault(choice => choice.Percent == settings.Current.General.InterfaceScalePercent);
        set
        {
            if (value is not null)
            {
                settings.Update(current => current with { General = current.General with { InterfaceScalePercent = value.Percent } });
            }
        }
    }

    /// <summary>Puts this section back to how Flint came.</summary>
    public ConfirmableAction Reset { get; }
}

/// <summary>One interface size, as offered.</summary>
/// <param name="Percent">The size as a percentage.</param>
public sealed record InterfaceScaleChoice(int Percent)
{
    /// <summary>How the choice reads.</summary>
    public string Label => $"{Percent}%";

    /// <inheritdoc />
    public override string ToString() => Label;
}
