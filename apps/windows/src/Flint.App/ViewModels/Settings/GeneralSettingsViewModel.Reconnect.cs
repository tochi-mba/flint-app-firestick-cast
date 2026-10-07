using Flint.Core.Settings;

namespace Flint.App.ViewModels.Settings;

/// <summary>The General section's reconnect settings: reaching the TV again without its code.</summary>
public sealed partial class GeneralSettingsViewModel
{
    /// <summary>Reconnecting when Flint starts.</summary>
    public SettingText ReconnectOnStartText { get; } = new(
        "Reconnect to the last TV when Flint starts",
        "Uses the login the TV gave when you paired, so no code is needed until its app restarts.");

    /// <summary>Reconnecting after a drop.</summary>
    public SettingText ReconnectAfterDropText { get; } = new(
        "Keep trying if the connection drops",
        "Tries again after 1, 2, 4 and 8 seconds, then every 15, until the time below is up.");

    /// <summary>How long to keep trying.</summary>
    public SettingText ReconnectTimeText { get; } = new(
        "How long to keep trying",
        "After this, Flint stops and says so. Connect again on the Cast page when the TV is back.");

    /// <summary>What happens once reconnected.</summary>
    public SettingText AfterReconnectText { get; } = new(
        "After reconnecting",
        "Ask before carrying on with what was on the TV, carry on, or leave the TV alone. "
            + "Sharing your screen always counts down first, so you can stop it.");

    /// <summary>Opening the TV app for a new code.</summary>
    public SettingText OpenReceiverText { get; } = new(
        "Open Flint on the TV when a new code is needed",
        "When the TV no longer takes its login, Flint opens its app so the code is on screen. Needs ADB.");

    /// <summary>Whether Flint reconnects when it starts.</summary>
    public bool ReconnectOnStart
    {
        get => settings.Current.General.ReconnectOnStart;
        set => Update(general => general with { ReconnectOnStart = value });
    }

    /// <summary>Whether Flint keeps trying after a drop.</summary>
    public bool ReconnectAfterDrop
    {
        get => settings.Current.General.ReconnectAfterDrop;
        set => Update(general => general with { ReconnectAfterDrop = value });
    }

    /// <summary>Whether Flint opens its TV app when a new code is needed.</summary>
    public bool OpenReceiverForNewCode
    {
        get => settings.Current.General.OpenReceiverForNewCode;
        set => Update(general => general with { OpenReceiverForNewCode = value });
    }

    /// <summary>How long Flint can keep trying.</summary>
    public IReadOnlyList<Choice<int>> ReconnectTimeChoices { get; } =
    [
        new(30, "30 seconds"),
        new(60, "1 minute"),
        new(120, "2 minutes"),
        new(300, "5 minutes"),
        new(600, "10 minutes"),
    ];

    /// <summary>How long Flint keeps trying.</summary>
    /// <remarks>A time set by hand between the offered ones shows as the nearest offered.</remarks>
    public Choice<int>? ReconnectTime
    {
        get => ReconnectTimeChoices.MinBy(choice => Math.Abs(choice.Value - settings.Current.General.ReconnectSeconds));
        set
        {
            if (value is not null)
            {
                Update(general => general with { ReconnectSeconds = value.Value });
            }
        }
    }

    /// <summary>What can happen once reconnected.</summary>
    public IReadOnlyList<Choice<ReconnectOutcome>> AfterReconnectChoices { get; } =
    [
        new(ReconnectOutcome.Ask, "Ask"),
        new(ReconnectOutcome.CarryOn, "Carry on"),
        new(ReconnectOutcome.DoNothing, "Do nothing"),
    ];

    /// <summary>What happens once reconnected.</summary>
    public Choice<ReconnectOutcome>? AfterReconnect
    {
        get => AfterReconnectChoices.FirstOrDefault(choice => choice.Value == settings.Current.General.AfterReconnect);
        set
        {
            if (value is not null)
            {
                Update(general => general with { AfterReconnect = value.Value });
            }
        }
    }

    private void Update(Func<GeneralSettings, GeneralSettings> change) =>
        settings.Update(current => current with { General = change(current.General) });
}
