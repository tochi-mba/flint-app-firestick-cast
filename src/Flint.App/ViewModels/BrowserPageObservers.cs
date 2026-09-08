using System.ComponentModel;

namespace Flint.App.ViewModels;

/// <summary>
/// Wires the page to the four things it has to react to, and unwires them again.
/// </summary>
/// <remarks>
/// Every handler here answers the same question — which of the page's computed properties stopped
/// being true because something else changed — and the answer is nowhere in the type system.
/// Keeping them in one file makes the set readable as a set, which is the only way an omission
/// shows up before a control is found stuck in the wrong state.
/// </remarks>
internal sealed class BrowserPageObservers(BrowserPageViewModel page, CastPageViewModel cast)
{
    /// <summary>Subscribes to everything the page derives state from.</summary>
    public void Attach()
    {
        cast.PropertyChanged += OnCast;
        page.ViewControls.PropertyChanged += OnViewControls;
        page.Workspace.PropertyChanged += OnWorkspace;
        page.Preview.PropertyChanged += OnPreview;
    }

    /// <summary>Unsubscribes. The cast page outlives this one, so this is not optional.</summary>
    public void Detach()
    {
        cast.PropertyChanged -= OnCast;
        page.ViewControls.PropertyChanged -= OnViewControls;
        page.Workspace.PropertyChanged -= OnWorkspace;
        page.Preview.PropertyChanged -= OnPreview;
    }

    private void OnCast(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is not (nameof(CastPageViewModel.Report)
            or nameof(CastPageViewModel.PairingCode)
            or nameof(CastPageViewModel.IsConnected)
            or null))
        {
            return;
        }

        page.SyncBrowserPortFromEvidence();
        page.RaiseVerdictChanged();
        page.RaiseNavigationAvailability();
        // Discovery, a pairing code and a port all arrive separately, and any of them can be the
        // last piece. Every one of them therefore asks for a reconnect pass rather than assuming
        // some other change will.
        page.RequestReconnect();
    }

    /// <summary>
    /// Points the desktop keyboard at the television the moment a page field takes focus.
    /// </summary>
    /// <remarks>
    /// Typing on a television is the slowest thing this feature asks of anyone, and the remote's
    /// on-screen keyboard is why. When the receiver reports that an editable element has focus,
    /// forwarding arms itself so the next keystroke simply lands — no capture pad to find, no Send
    /// button to press. It disarms when the field loses focus, because keystrokes aimed at a page
    /// with nothing listening are worse than none.
    /// </remarks>
    private void OnViewControls(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(BrowserViewControlsViewModel.IsEditing))
        {
            page.ApplyEditingFocus(page.ViewControls.IsEditing);
        }
    }

    private void OnWorkspace(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(BrowserWorkspaceViewModel.IsWorkspaceMode)
            or nameof(BrowserWorkspaceViewModel.HasOpenPanes)
            or nameof(BrowserWorkspaceViewModel.IsAvailable)
            or null)
        {
            page.RaiseStageLayout();
        }
    }

    private void OnPreview(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(BrowserPreviewViewModel.CanToggle)
            or nameof(BrowserPreviewViewModel.IsSupported)
            or null)
        {
            page.TogglePreviewCommand.NotifyCanExecuteChanged();
        }

        // Whether the preview can be *interacted with* follows the frame, not only the toggle:
        // pointer input is measured against a live frame, so a stale one must not accept clicks.
        if (eventArgs.PropertyName is nameof(BrowserPreviewViewModel.HasFrame)
            or nameof(BrowserPreviewViewModel.IsRequested)
            or nameof(BrowserPreviewViewModel.IsLive)
            or nameof(BrowserPreviewViewModel.Frame)
            or null)
        {
            page.RaisePreviewInteractivity();
        }
    }
}
