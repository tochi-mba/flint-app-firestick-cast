namespace Flint.Protocol;

/// <summary>Strict payload codec for additive browser workspace messages 32 through 34.</summary>
internal static partial class BrowserWireCodec
{
    private static void EncodeWorkspaceCommand(
        WireCodec.PayloadWriter writer,
        BrowserWorkspaceCommandMessage message)
    {
        writer.Int64(message.Epoch);
        writer.Int64(message.CommandId);
        writer.Int64(message.ExpectedRevision);
        writer.UInt8((int)message.Action);
        writer.Int64(message.PaneId);
        writer.UInt8(message.Value);
        writer.Utf8UInt16(message.Url, BrowserWireLimits.MaxUrlBytes, "browser workspace command URL");
    }

    private static BrowserWorkspaceCommandMessage DecodeWorkspaceCommand(ref WireCodec.PayloadReader reader)
    {
        var message = new BrowserWorkspaceCommandMessage(
            reader.Int64("browser workspace command epoch"),
            reader.Int64("browser workspace command ID"),
            reader.Int64("browser workspace command expected revision"),
            ReadEnum<BrowserWorkspaceCommandAction>(ref reader, "browser workspace command action"),
            reader.Int64("browser workspace command pane ID"),
            (byte)reader.UInt8("browser workspace command value"),
            reader.Utf8UInt16(BrowserWireLimits.MaxUrlBytes, "browser workspace command URL"));
        Validate(message);
        return message;
    }

    private static void EncodeWorkspaceState(
        WireCodec.PayloadWriter writer,
        BrowserWorkspaceStateMessage message)
    {
        writer.Int64(message.Epoch);
        writer.Int64(message.Revision);
        writer.UInt8((int)message.Layout);
        writer.Int64(message.FocusedPaneId);
        writer.UInt8((int)message.InteractionMode);
        writer.Int64(message.PageFullscreenPaneId);
        writer.Int64(message.TheaterPaneId);
        writer.UInt8(message.MaxLiveRenderers);
        writer.UInt8(message.MaxOpenPanes);
        writer.UInt8(message.Panes.Count);
        foreach (var pane in message.Panes)
        {
            EncodeWorkspacePane(writer, pane);
        }
    }

    private static BrowserWorkspaceStateMessage DecodeWorkspaceState(ref WireCodec.PayloadReader reader)
    {
        var epoch = reader.Int64("browser workspace state epoch");
        var revision = reader.Int64("browser workspace state revision");
        var layout = ReadEnum<BrowserWorkspaceWireLayout>(ref reader, "browser workspace layout");
        var focusedPaneId = reader.Int64("browser workspace focused pane ID");
        var interactionMode = ReadEnum<BrowserWorkspaceWireInteractionMode>(
            ref reader,
            "browser workspace interaction mode");
        var pageFullscreenPaneId = reader.Int64("browser workspace page-fullscreen pane ID");
        var theaterPaneId = reader.Int64("browser workspace theater pane ID");
        var maxLiveRenderers = reader.UInt8("browser workspace max live renderers");
        var maxOpenPanes = reader.UInt8("browser workspace max open panes");
        var count = reader.UInt8("browser workspace pane count");
        Require(count <= BrowserWireLimits.MaxWorkspacePanes, $"Too many browser workspace panes: {count}.");

        var panes = new List<BrowserWorkspacePaneStateEntry>(count);
        for (var index = 0; index < count; index++)
        {
            panes.Add(DecodeWorkspacePane(ref reader));
        }

        var message = new BrowserWorkspaceStateMessage(
            epoch,
            revision,
            layout,
            focusedPaneId,
            interactionMode,
            pageFullscreenPaneId,
            theaterPaneId,
            (byte)maxLiveRenderers,
            (byte)maxOpenPanes,
            ValueList<BrowserWorkspacePaneStateEntry>.From(panes));
        Validate(message);
        return message;
    }

    private static void EncodeWorkspaceInput(
        WireCodec.PayloadWriter writer,
        BrowserWorkspaceInputMessage message)
    {
        writer.Int64(message.Epoch);
        writer.Int64(message.CommandId);
        writer.Int64(message.ExpectedRevision);
        writer.Int64(message.PaneId);
        writer.UInt8((int)message.Kind);
        switch (message.Kind)
        {
            case BrowserWorkspaceInputKind.Key:
                writer.UInt8((int)message.Key!.Value);
                break;

            case BrowserWorkspaceInputKind.Text:
                writer.Utf8UInt16(message.Text, BrowserWireLimits.MaxTextBytes, "browser workspace input text");
                break;
        }
    }

    private static BrowserWorkspaceInputMessage DecodeWorkspaceInput(ref WireCodec.PayloadReader reader)
    {
        var epoch = reader.Int64("browser workspace input epoch");
        var commandId = reader.Int64("browser workspace input command ID");
        var expectedRevision = reader.Int64("browser workspace input expected revision");
        var paneId = reader.Int64("browser workspace input pane ID");
        var kind = ReadEnum<BrowserWorkspaceInputKind>(ref reader, "browser workspace input kind");
        var message = kind switch
        {
            BrowserWorkspaceInputKind.Key => new BrowserWorkspaceInputMessage(
                epoch,
                commandId,
                expectedRevision,
                paneId,
                kind,
                ReadEnum<BrowserSemanticKey>(ref reader, "browser workspace input key")),
            BrowserWorkspaceInputKind.Text => new BrowserWorkspaceInputMessage(
                epoch,
                commandId,
                expectedRevision,
                paneId,
                kind,
                Text: reader.Utf8UInt16(BrowserWireLimits.MaxTextBytes, "browser workspace input text")),
            _ => throw new WireFormatException($"Unknown browser workspace input kind: {(int)kind}."),
        };
        Validate(message);
        return message;
    }

    private static void EncodeWorkspacePane(
        WireCodec.PayloadWriter writer,
        BrowserWorkspacePaneStateEntry pane)
    {
        writer.Int64(pane.PaneId);
        writer.UInt8(pane.Slot);
        writer.UInt8((int)pane.Residency);
        writer.Utf8UInt16(pane.Url, BrowserWireLimits.MaxUrlBytes, "browser workspace pane URL");
        writer.Utf8UInt16(pane.Title, BrowserWireLimits.MaxTitleBytes, "browser workspace pane title");
        writer.UInt8(pane.Loading ? 1 : 0);
        writer.UInt8(pane.Progress);
        writer.UInt8(pane.CanGoBack ? 1 : 0);
        writer.UInt8(pane.CanGoForward ? 1 : 0);
        writer.UInt8(pane.DesiredMuted ? 1 : 0);
        writer.UInt8((int)pane.MuteApplication);
        writer.UInt8((int)pane.ObservedPlayback);
    }

    private static BrowserWorkspacePaneStateEntry DecodeWorkspacePane(ref WireCodec.PayloadReader reader) => new(
        reader.Int64("browser workspace pane ID"),
        (byte)reader.UInt8("browser workspace pane slot"),
        ReadEnum<BrowserWorkspaceWirePaneResidency>(ref reader, "browser workspace pane residency"),
        reader.Utf8UInt16(BrowserWireLimits.MaxUrlBytes, "browser workspace pane URL"),
        reader.Utf8UInt16(BrowserWireLimits.MaxTitleBytes, "browser workspace pane title"),
        reader.Boolean("browser workspace pane loading flag"),
        (byte)reader.UInt8("browser workspace pane progress"),
        reader.Boolean("browser workspace pane can-go-back flag"),
        reader.Boolean("browser workspace pane can-go-forward flag"),
        reader.Boolean("browser workspace pane desired-muted flag"),
        ReadEnum<BrowserWorkspaceWireMuteApplication>(ref reader, "browser workspace pane mute application"),
        ReadEnum<BrowserWorkspaceWireObservedPlayback>(ref reader, "browser workspace pane observed playback"));

    private static void Validate(BrowserWorkspaceCommandMessage message)
    {
        RequirePositive(message.Epoch, "browser workspace command epoch");
        RequirePositive(message.CommandId, "browser workspace command ID");
        RequirePositive(message.ExpectedRevision, "browser workspace command expected revision");
        RequireEnum(message.Action, "browser workspace command action");
        RequireNotNull(message.Url, "Browser workspace command URL is null.");
        RequireTextLength(message.Url, BrowserWireLimits.MaxUrlBytes, "Browser workspace command URL");

        switch (message.Action)
        {
            case BrowserWorkspaceCommandAction.Focus:
            case BrowserWorkspaceCommandAction.ClosePane:
            case BrowserWorkspaceCommandAction.Reload:
            case BrowserWorkspaceCommandAction.Back:
            case BrowserWorkspaceCommandAction.Forward:
            case BrowserWorkspaceCommandAction.PlayPause:
            case BrowserWorkspaceCommandAction.EnterTheater:
                RequirePositive(message.PaneId, "browser workspace command pane ID");
                Require(message.Value == 0, "Pane-targeted browser workspace command carries a value.");
                Require(message.Url.Length == 0, "Pane-targeted browser workspace command carries a URL.");
                break;

            case BrowserWorkspaceCommandAction.OpenPane:
                Require(message.PaneId == 0, "Open-pane command carries a pane ID.");
                Require(message.Value == 0, "Open-pane command carries a value.");
                break;

            case BrowserWorkspaceCommandAction.SetLayout:
                Require(message.PaneId == 0, "Set-layout command carries a pane ID.");
                RequireEnumValue<BrowserWorkspaceWireLayout>(message.Value, "browser workspace layout");
                Require(message.Url.Length == 0, "Set-layout command carries a URL.");
                break;

            case BrowserWorkspaceCommandAction.Navigate:
                RequirePositive(message.PaneId, "browser workspace command pane ID");
                Require(message.Value == 0, "Navigate command carries a value.");
                RequireNotBlank(message.Url, "Browser workspace navigate URL is blank.");
                break;

            case BrowserWorkspaceCommandAction.SetMute:
                RequirePositive(message.PaneId, "browser workspace command pane ID");
                Require(message.Value is 0 or 1, "Browser workspace mute value is not boolean.");
                Require(message.Url.Length == 0, "Set-mute command carries a URL.");
                break;

            case BrowserWorkspaceCommandAction.SetInteraction:
                Require(message.PaneId == 0, "Set-interaction command carries a pane ID.");
                RequireEnumValue<BrowserWorkspaceWireInteractionMode>(
                    message.Value,
                    "browser workspace interaction mode");
                Require(message.Url.Length == 0, "Set-interaction command carries a URL.");
                break;

            case BrowserWorkspaceCommandAction.ExitTheater:
            case BrowserWorkspaceCommandAction.RequestSnapshot:
                Require(message.PaneId == 0, "Parameterless browser workspace command carries a pane ID.");
                Require(message.Value == 0, "Parameterless browser workspace command carries a value.");
                Require(message.Url.Length == 0, "Parameterless browser workspace command carries a URL.");
                break;

            case BrowserWorkspaceCommandAction.MovePane:
                RequirePositive(message.PaneId, "browser workspace command pane ID");
                Require(message.Value <= 3, "Move-pane command slot is out of range.");
                Require(message.Url.Length == 0, "Move-pane command carries a URL.");
                break;
        }
    }

    private static void Validate(BrowserWorkspaceStateMessage message)
    {
        RequirePositive(message.Epoch, "browser workspace state epoch");
        RequirePositive(message.Revision, "browser workspace state revision");
        RequireEnum(message.Layout, "browser workspace layout");
        RequireEnum(message.InteractionMode, "browser workspace interaction mode");
        Require(message.Panes.Count <= BrowserWireLimits.MaxWorkspacePanes,
            "Browser workspace pane count is out of range.");
        Require(message.MaxLiveRenderers > 0, "Browser workspace max live renderers must be positive.");
        Require(message.MaxOpenPanes > 0, "Browser workspace max open panes must be positive.");
        Require(message.MaxLiveRenderers <= message.MaxOpenPanes,
            "Browser workspace max live renderers exceeds max open panes.");

        var paneIds = new HashSet<long>();
        foreach (var pane in message.Panes)
        {
            if (pane is null)
            {
                throw new WireFormatException("Browser workspace pane entry is null.");
            }

            ValidateWorkspacePane(pane);
            Require(paneIds.Add(pane.PaneId), "Browser workspace pane IDs must be unique.");
        }

        if (message.Panes.IsEmpty)
        {
            Require(message.FocusedPaneId == 0, "Empty browser workspace carries a focused pane ID.");
            Require(message.PageFullscreenPaneId == 0, "Empty browser workspace carries page fullscreen.");
            Require(message.TheaterPaneId == 0, "Empty browser workspace carries theatre mode.");
        }
        else
        {
            if (message.FocusedPaneId > 0)
            {
                Require(paneIds.Contains(message.FocusedPaneId), "Browser workspace focused pane is not present.");
            }

            if (message.PageFullscreenPaneId > 0)
            {
                Require(
                    paneIds.Contains(message.PageFullscreenPaneId),
                    "Browser workspace page-fullscreen pane is not present.");
            }

            if (message.TheaterPaneId > 0)
            {
                Require(paneIds.Contains(message.TheaterPaneId), "Browser workspace theatre pane is not present.");
            }
        }
    }

    private static void ValidateWorkspacePane(BrowserWorkspacePaneStateEntry pane)
    {
        RequirePositive(pane.PaneId, "browser workspace pane ID");
        RequireEnum(pane.Residency, "browser workspace pane residency");
        Require(pane.Progress <= 100, "Browser workspace pane progress is out of range.");
        RequireNotNull(pane.Url, "Browser workspace pane URL is null.");
        RequireNotNull(pane.Title, "Browser workspace pane title is null.");
        RequireTextLength(pane.Url, BrowserWireLimits.MaxUrlBytes, "Browser workspace pane URL");
        RequireTextLength(pane.Title, BrowserWireLimits.MaxTitleBytes, "Browser workspace pane title");
        RequireEnum(pane.MuteApplication, "browser workspace pane mute application");
        RequireEnum(pane.ObservedPlayback, "browser workspace pane observed playback");
        if (pane.Residency is BrowserWorkspaceWirePaneResidency.Live
            or BrowserWorkspaceWirePaneResidency.Failed)
        {
            RequireNotBlank(pane.Url, "Active browser workspace pane URL is blank.");
        }
    }

    private static void Validate(BrowserWorkspaceInputMessage message)
    {
        RequirePositive(message.Epoch, "browser workspace input epoch");
        RequirePositive(message.CommandId, "browser workspace input command ID");
        RequirePositive(message.ExpectedRevision, "browser workspace input expected revision");
        RequirePositive(message.PaneId, "browser workspace input pane ID");
        RequireEnum(message.Kind, "browser workspace input kind");
        switch (message.Kind)
        {
            case BrowserWorkspaceInputKind.Key:
                if (message.Key is not BrowserSemanticKey key)
                {
                    throw new WireFormatException("Browser workspace key input is missing a key.");
                }

                RequireEnum(key, "browser workspace input key");
                Require(message.Text.Length == 0, "Browser workspace key input carries text.");
                break;

            case BrowserWorkspaceInputKind.Text:
                Require(message.Key is null, "Browser workspace text input carries a key.");
                RequireNotBlank(message.Text, "Browser workspace input text is blank.");
                RequireTextLength(message.Text, BrowserWireLimits.MaxTextBytes, "Browser workspace input text");
                break;
        }
    }

    private static void RequireEnumValue<TEnum>(byte value, string field)
        where TEnum : struct, Enum
    {
        Require(Enum.IsDefined(typeof(TEnum), (int)value), $"Unknown {field}: {value}.");
    }
}
