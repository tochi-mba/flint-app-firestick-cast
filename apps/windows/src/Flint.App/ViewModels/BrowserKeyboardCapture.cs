namespace Flint.App.ViewModels;

/// <summary>
/// Whether desktop keystrokes are currently pointed at the television, and why.
/// </summary>
/// <remarks>
/// Typing on a television is the slowest thing this feature asks of anyone, and the remote's
/// on-screen keyboard is why. When the receiver reports that a page field has focus, forwarding
/// arms itself so the next keystroke simply lands — no capture pad to find, no Send button.
///
/// The "why" matters as much as the "whether". Capture someone armed by hand must survive a field
/// losing focus, and capture that armed itself must not outlive the field it was armed for. Keeping
/// that distinction here rather than in a page-level boolean is what stops the two cases blurring.
///
/// Pure, so the whole policy is testable without a session or a television.
/// </remarks>
internal sealed class BrowserKeyboardCapture
{
    private bool armedByEditing;

    /// <summary>Whether keystrokes are being forwarded.</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>Whether the receiver currently reports a focused page field.</summary>
    public bool IsEditing { get; private set; }

    /// <summary>
    /// What the control says.
    /// </summary>
    /// <remarks>
    /// Names where the keys are going, not merely that something is on: while a field is waiting,
    /// "typing to TV" is the fact worth reading.
    /// </remarks>
    public string Label => (IsEnabled, IsEditing) switch
    {
        (true, true) => "TYPING TO TV · ESC RELEASES",
        (true, false) => "CAPTURE ON · ESC RELEASES",
        _ => "CAPTURE OFF",
    };

    /// <summary>Applies the receiver's focused-field report. Returns whether anything changed.</summary>
    public bool SetEditing(bool editing)
    {
        if (IsEditing == editing)
        {
            return false;
        }

        IsEditing = editing;

        if (editing)
        {
            // Remember whether this arming was ours, so hand-armed capture is not switched off
            // later by a field the user never asked about.
            armedByEditing = !IsEnabled;
            IsEnabled = true;
            return true;
        }

        if (armedByEditing)
        {
            armedByEditing = false;
            IsEnabled = false;
        }

        return true;
    }

    /// <summary>Flips capture by hand. Doing so takes ownership away from the editing signal.</summary>
    public void Toggle()
    {
        IsEnabled = !IsEnabled;
        armedByEditing = false;
    }

    /// <summary>Turns forwarding off, as Escape does. Ownership resets with it.</summary>
    public void Release()
    {
        IsEnabled = false;
        armedByEditing = false;
    }
}
