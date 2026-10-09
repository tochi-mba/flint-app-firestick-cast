using Flint.Core;
using Flint.Core.Settings;

namespace Flint.App.ViewModels;

/// <summary>What the shell asks of its window and the application: show, hide and quit.</summary>
public interface IWindowControl
{
    /// <summary>Shows the window in front, restoring it if it was minimised or hidden.</summary>
    void Show();

    /// <summary>Hides the window; Flint keeps running in the tray.</summary>
    void Hide();

    /// <summary>Quits Flint.</summary>
    void Shutdown();
}

/// <summary>Closing the window, quitting, and bringing the window back.</summary>
public sealed partial class MainWindowViewModel
{
    private IWindowControl? window;

    /// <summary>The question asked about closing the window or quitting.</summary>
    public CloseQuestion CloseQuestion { get; } = new();

    /// <summary>Whether Flint is quitting, so the window's own close goes through rather than being asked about again.</summary>
    public bool IsQuitting { get; private set; }

    /// <summary>Whether the shell has been handed a real window to show, hide and quit.</summary>
    public bool HasWindowControl => window is not null;

    /// <summary>Whether quitting now would stop something this PC is showing on the TV.</summary>
    internal bool SomethingOnTheTv => Cast.IsMirroring || Media.NowPlaying.IsActive || Browser.HasLiveSession;

    /// <summary>Hands the shell its window, which it shows, hides and quits through.</summary>
    internal void UseWindow(IWindowControl control)
    {
        window = control ?? throw new ArgumentNullException(nameof(control));

        // The TV is never changed behind a hidden window: a question brings the window forward.
        SwitchPrompt.PropertyChanged += (_, change) => ShowForQuestion(control, change.PropertyName, SwitchPrompt.IsOpen);
        CloseQuestion.PropertyChanged += (_, change) => ShowForQuestion(control, change.PropertyName, CloseQuestion.IsOpen);
    }

    /// <summary>Brings the window forward.</summary>
    internal void ShowWindow() => window?.Show();

    /// <summary>The window's close button: hides it, asks, or quits, as the settings say.</summary>
    public async Task CloseWindowAsync()
    {
        var general = settingsService.Current.General;
        switch (WindowClosePolicy.OnClose(general, settingsService.Current.Tray, SomethingOnTheTv))
        {
            case CloseStep.Hide:
                window?.Hide();
                break;
            case CloseStep.AskKeepRunning:
                await AskKeepRunningAsync().ConfigureAwait(true);
                break;
            default:
                await QuitAsync().ConfigureAwait(true);
                break;
        }
    }

    /// <summary>Quits Flint, asking first while something is on the TV when the settings say to.</summary>
    public async Task QuitAsync()
    {
        if (WindowClosePolicy.OnQuit(settingsService.Current.General, SomethingOnTheTv) is CloseStep.ConfirmQuit)
        {
            var answer = await CloseQuestion.AskAsync(
                $"Quit and stop {WhatIsOnTheTv(Cast.IsMirroring, Media.NowPlaying.IsActive)}?",
                "Quitting Flint stops what it is showing on the TV.",
                "QUIT",
                "CANCEL").ConfigureAwait(true);
            if (answer is not CloseAnswer.First)
            {
                return;
            }
        }

        FlintDiag.Info("FlintShell", "quit");
        IsQuitting = true;
        window?.Shutdown();
    }

    private async Task AskKeepRunningAsync()
    {
        var answer = await CloseQuestion.AskAsync(
            "Keep Flint running in the tray?",
            "Sharing and playback carry on while the window is closed. You can change this in Settings, General.",
            "KEEP RUNNING",
            "QUIT FLINT").ConfigureAwait(true);
        if (answer is CloseAnswer.Cancelled)
        {
            return;
        }

        var keepRunning = answer is CloseAnswer.First;
        settingsService.Update(current => current with
        {
            General = current.General with { CloseWindow = keepRunning ? CloseWindowOutcome.KeepRunning : CloseWindowOutcome.Quit },
        });
        if (keepRunning)
        {
            window?.Hide();
            return;
        }

        await QuitAsync().ConfigureAwait(true);
    }

    /// <summary>What quitting would stop, in the question's words; the TV browser when it is neither of the others.</summary>
    internal static string WhatIsOnTheTv(bool sharing, bool playing) =>
        sharing ? "sharing your screen"
        : playing ? "what is playing"
        : "the TV browser";

    private static void ShowForQuestion(IWindowControl control, string? property, bool isOpen)
    {
        if (property is "IsOpen" && isOpen)
        {
            control.Show();
        }
    }
}
