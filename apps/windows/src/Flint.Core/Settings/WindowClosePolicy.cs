namespace Flint.Core.Settings;

/// <summary>What happens next when the person closes the window or quits.</summary>
public enum CloseStep
{
    /// <summary>The window goes; Flint keeps running in the tray.</summary>
    Hide = 0,

    /// <summary>Ask, the first time, whether Flint should keep running in the tray.</summary>
    AskKeepRunning = 1,

    /// <summary>Ask first, because quitting would stop what is on the TV.</summary>
    ConfirmQuit = 2,

    /// <summary>Flint quits.</summary>
    Quit = 3,
}

/// <summary>Decides what closing the window and quitting do, from the settings and what is on the TV.</summary>
/// <remarks>
/// Without a tray icon there is nowhere for a hidden Flint to be found again, so closing always
/// quits and Flint never starts hidden: a running app with no window and no icon is one the person
/// cannot reach.
/// </remarks>
public static class WindowClosePolicy
{
    /// <summary>What closing the window does.</summary>
    /// <param name="general">The General settings.</param>
    /// <param name="tray">The tray settings.</param>
    /// <param name="somethingOnTheTv">Whether quitting would stop a share or playback.</param>
    public static CloseStep OnClose(GeneralSettings general, TraySettings tray, bool somethingOnTheTv)
    {
        ArgumentNullException.ThrowIfNull(general);
        ArgumentNullException.ThrowIfNull(tray);
        if (!tray.ShowIcon)
        {
            return OnQuit(general, somethingOnTheTv);
        }

        return general.CloseWindow switch
        {
            CloseWindowOutcome.KeepRunning => CloseStep.Hide,
            CloseWindowOutcome.Quit => OnQuit(general, somethingOnTheTv),
            _ => CloseStep.AskKeepRunning,
        };
    }

    /// <summary>What quitting does: asks first while something is on the TV, when the settings say to.</summary>
    public static CloseStep OnQuit(GeneralSettings general, bool somethingOnTheTv)
    {
        ArgumentNullException.ThrowIfNull(general);
        return somethingOnTheTv && general.ConfirmQuitWhileActive ? CloseStep.ConfirmQuit : CloseStep.Quit;
    }

    /// <summary>Whether Flint starts with its window hidden.</summary>
    /// <remarks>
    /// Only at sign-in, where "start in the tray" adds <c>--minimized</c> to the entry Windows runs:
    /// someone who opens Flint by hand wants its window.
    /// </remarks>
    /// <param name="tray">The tray settings.</param>
    /// <param name="minimizedArgument">Whether Flint was started with <c>--minimized</c>.</param>
    public static bool StartsHidden(TraySettings tray, bool minimizedArgument)
    {
        ArgumentNullException.ThrowIfNull(tray);
        return tray.ShowIcon && minimizedArgument;
    }
}
