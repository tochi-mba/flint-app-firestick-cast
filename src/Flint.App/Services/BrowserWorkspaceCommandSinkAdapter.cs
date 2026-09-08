using Flint.App.ViewModels;
using Flint.Protocol;
using Flint.Session.Browser;

namespace Flint.App.Services;

/// <summary>
/// Translates semantic workspace intents from the Windows cockpit into ordered wire messages.
/// </summary>
internal sealed class BrowserWorkspaceCommandSinkAdapter : IBrowserWorkspaceCommandSink
{
    private readonly BrowserSession session;
    private readonly Func<long> epoch;
    private readonly Func<long> nextCommandId;
    private readonly Func<long> expectedRevision;
    private readonly Func<bool> isEnabled;
    private readonly Func<bool> canResize;

    public BrowserWorkspaceCommandSinkAdapter(
        BrowserSession session,
        Func<long> epoch,
        Func<long> nextCommandId,
        Func<long> expectedRevision,
        Func<bool> isEnabled, Func<bool>? canResize = null)
    {
        this.canResize = canResize ?? (() => false);
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.epoch = epoch ?? throw new ArgumentNullException(nameof(epoch));
        this.nextCommandId = nextCommandId ?? throw new ArgumentNullException(nameof(nextCommandId));
        this.expectedRevision = expectedRevision ?? throw new ArgumentNullException(nameof(expectedRevision));
        this.isEnabled = isEnabled ?? throw new ArgumentNullException(nameof(isEnabled));
    }

    public Task SendAsync(BrowserWorkspaceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!isEnabled())
        {
            throw new IOException("The TV workspace channel is no longer available.");
        }

        if (command is SwitchBrowserWorkspaceModeCommand mode)
        {
            if (!canResize()) throw new IOException("Workspace mode switching is not available.");
            return session.SendWorkspaceResizeAsync(new BrowserWorkspaceResizeMessage(
                epoch(), nextCommandId(), expectedRevision(), mode.Column, mode.Row, mode.Workspace ? (byte)2 : (byte)1), cancellationToken);
        }
        if (command is ResizeBrowserWorkspaceCommand resize)
        {
            if (!canResize()) throw new IOException("Workspace resizing is not available.");
            return session.SendWorkspaceResizeAsync(new BrowserWorkspaceResizeMessage(
                epoch(), nextCommandId(), expectedRevision(), resize.Column, resize.Row), cancellationToken);
        }

        if (command is SendBrowserWorkspaceInputCommand input)
        {
            return session.SendWorkspaceInputAsync(
                BrowserCockpitWireMapper.ToWorkspaceInput(
                    input,
                    epoch(),
                    nextCommandId(),
                    expectedRevision()),
                cancellationToken);
        }

        if (command is SendBrowserWorkspaceKeyCommand key)
        {
            return session.SendWorkspaceInputAsync(
                BrowserCockpitWireMapper.ToWorkspaceKey(
                    key,
                    epoch(),
                    nextCommandId(),
                    expectedRevision()),
                cancellationToken);
        }

        return session.SendWorkspaceCommandAsync(
            BrowserCockpitWireMapper.ToWorkspaceCommand(
                command,
                epoch(),
                nextCommandId(),
                expectedRevision()),
            cancellationToken);
    }
}
