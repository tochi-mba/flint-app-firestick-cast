namespace Flint.Protocol;

/// <summary>Strict v2 browser payload encoding shared by the future TLS BrowserSession endpoints.</summary>
/// <remarks>
/// This codec intentionally knows nothing about sockets, certificates, WebView, or UI state. The
/// ordinary CastSession must reject these typed values before dispatch; the TLS BrowserSession is
/// the only route that may call it in production. Keeping the bounded model here lets all three
/// implementations prove byte-level agreement before that transport exists.
/// </remarks>
internal static partial class BrowserWireCodec
{
    private const int MaxViewportDimension = 16_384;

    internal static byte[] Encode(WireMessage message)
    {
        var writer = new WireCodec.PayloadWriter();
        switch (message)
        {
            case BrowserCapabilityMessage capability:
                Validate(capability);
                EncodeCapability(writer, capability);
                break;

            case BrowserCommandMessage command:
                Validate(command);
                EncodeCommand(writer, command);
                break;

            case BrowserInputMessage input:
                Validate(input);
                EncodeInput(writer, input);
                break;

            case BrowserStateMessage state:
                Validate(state);
                EncodeState(writer, state);
                break;

            case BrowserPreviewMessage preview:
                Validate(preview);
                EncodePreview(writer, preview);
                break;

            case BrowserDialogMessage dialog:
                Validate(dialog);
                EncodeDialog(writer, dialog);
                break;

            case BrowserDialogReplyMessage reply:
                Validate(reply);
                EncodeDialogReply(writer, reply);
                break;

            case BrowserTabCommandMessage tabCommand:
                Validate(tabCommand);
                EncodeTabCommand(writer, tabCommand);
                break;

            case BrowserTabStateMessage tabState:
                Validate(tabState);
                EncodeTabState(writer, tabState);
                break;

            case BrowserViewCommandMessage viewCommand:
                Validate(viewCommand);
                EncodeViewCommand(writer, viewCommand);
                break;

            case BrowserViewStateMessage viewState:
                Validate(viewState);
                EncodeViewState(writer, viewState);
                break;

            case BrowserFaviconMessage favicon:
                Validate(favicon);
                EncodeFavicon(writer, favicon);
                break;

            case BrowserLibraryCommandMessage libraryCommand:
                Validate(libraryCommand);
                EncodeLibraryCommand(writer, libraryCommand);
                break;

            case BrowserLibraryStateMessage libraryState:
                Validate(libraryState);
                EncodeLibraryState(writer, libraryState);
                break;

            case BrowserProfileCommandMessage profileCommand:
                Validate(profileCommand);
                EncodeProfileCommand(writer, profileCommand);
                break;

            case BrowserProfileStateMessage profileState:
                Validate(profileState);
                EncodeProfileState(writer, profileState);
                break;

            case BrowserNetworkCommandMessage networkCommand:
                Validate(networkCommand);
                EncodeNetworkCommand(writer, networkCommand);
                break;

            case BrowserNetworkStateMessage networkState:
                Validate(networkState);
                EncodeNetworkState(writer, networkState);
                break;

            case BrowserWorkspaceResizeMessage resize:
                ValidateGeometry(resize.Epoch, resize.ExpectedRevision, resize.Column, resize.Row);
                if (resize.CommandId <= 0) throw new WireFormatException("Resize command ID must be positive.");
                writer.Int64(resize.Epoch); writer.Int64(resize.CommandId); writer.Int64(resize.ExpectedRevision);
                writer.UInt16(resize.Column); writer.UInt16(resize.Row);
                if (resize.Mode > 2) throw new WireFormatException("Invalid workspace mode.");
                writer.UInt8(resize.Mode);
                break;
            case BrowserWorkspaceGeometryMessage geometry:
                ValidateGeometry(geometry.Epoch, geometry.Revision, geometry.Column, geometry.Row);
                writer.Int64(geometry.Epoch); writer.Int64(geometry.Revision);
                writer.UInt16(geometry.Column); writer.UInt16(geometry.Row);
                if (geometry.Mode > 2) throw new WireFormatException("Invalid workspace mode.");
                writer.UInt8(geometry.Mode);
                break;
            case BrowserWorkspaceCommandMessage workspaceCommand:
                Validate(workspaceCommand);
                EncodeWorkspaceCommand(writer, workspaceCommand);
                break;

            case BrowserWorkspaceStateMessage workspaceState:
                Validate(workspaceState);
                EncodeWorkspaceState(writer, workspaceState);
                break;

            case BrowserWorkspaceInputMessage workspaceInput:
                Validate(workspaceInput);
                EncodeWorkspaceInput(writer, workspaceInput);
                break;

            default:
                throw new WireFormatException($"Unhandled browser message type: {message.GetType().Name}.");
        }

        return writer.ToArray();
    }

    internal static WireMessage Decode(WireMessageType type, ReadOnlySpan<byte> payload)
    {
        var reader = new WireCodec.PayloadReader(payload);
        WireMessage message = type switch
        {
            WireMessageType.BrowserCapability => DecodeCapability(ref reader),
            WireMessageType.BrowserCommand => DecodeCommand(ref reader),
            WireMessageType.BrowserInput => DecodeInput(ref reader),
            WireMessageType.BrowserState => DecodeState(ref reader),
            WireMessageType.BrowserPreview => DecodePreview(ref reader),
            WireMessageType.BrowserDialog => DecodeDialog(ref reader),
            WireMessageType.BrowserDialogReply => DecodeDialogReply(ref reader),
            WireMessageType.BrowserTabCommand => DecodeTabCommand(ref reader),
            WireMessageType.BrowserTabState => DecodeTabState(ref reader),
            WireMessageType.BrowserViewCommand => DecodeViewCommand(ref reader),
            WireMessageType.BrowserViewState => DecodeViewState(ref reader),
            WireMessageType.BrowserFavicon => DecodeFavicon(ref reader),
            WireMessageType.BrowserLibraryCommand => DecodeLibraryCommand(ref reader),
            WireMessageType.BrowserLibraryState => DecodeLibraryState(ref reader),
            WireMessageType.BrowserProfileCommand => DecodeProfileCommand(ref reader),
            WireMessageType.BrowserProfileState => DecodeProfileState(ref reader),
            WireMessageType.BrowserNetworkCommand => DecodeNetworkCommand(ref reader),
            WireMessageType.BrowserNetworkState => DecodeNetworkState(ref reader),
            WireMessageType.BrowserWorkspaceResize => DecodeResize(ref reader),
            WireMessageType.BrowserWorkspaceGeometry => DecodeGeometry(ref reader),
            WireMessageType.BrowserWorkspaceCommand => DecodeWorkspaceCommand(ref reader),
            WireMessageType.BrowserWorkspaceState => DecodeWorkspaceState(ref reader),
            WireMessageType.BrowserWorkspaceInput => DecodeWorkspaceInput(ref reader),
            _ => throw new WireFormatException($"Unhandled browser message type: {(int)type}."),
        };
        reader.RequireFinished();
        return message;
    }

    private static void EncodeCapability(WireCodec.PayloadWriter writer, BrowserCapabilityMessage message)
    {
        writer.UInt8((int)message.Status);
        writer.UInt16(message.SecureEndpointPort);
        writer.UInt16(message.ApiLevel);
        writer.Utf8UInt16(message.WebViewVersion, BrowserWireLimits.MaxTitleBytes, "WebView version");
        writer.UInt8(message.PreviewSupported ? 1 : 0);
        writer.UInt16(message.PreviewMaxWidth);
        writer.UInt16(message.PreviewMaxHeight);
        writer.UInt8(message.InteractivePreviewFramesPerSecond);
        writer.UInt8(message.IdlePreviewFramesPerSecond);
        writer.Int32(message.PreviewMaxBytes);
        writer.Utf8UInt16(message.Detail, BrowserWireLimits.MaxDetailBytes, "browser capability detail");
    }

    private static BrowserCapabilityMessage DecodeCapability(ref WireCodec.PayloadReader reader)
    {
        var message = new BrowserCapabilityMessage(
            ReadEnum<BrowserCapabilityStatus>(ref reader, "browser capability status"),
            reader.UInt16("browser secure endpoint port"),
            reader.UInt16("browser API level"),
            reader.Utf8UInt16(BrowserWireLimits.MaxTitleBytes, "WebView version"),
            reader.Boolean("browser preview supported"),
            reader.UInt16("browser preview maximum width"),
            reader.UInt16("browser preview maximum height"),
            reader.UInt8("browser interactive preview rate"),
            reader.UInt8("browser idle preview rate"),
            reader.Int32("browser preview maximum bytes"),
            reader.Utf8UInt16(BrowserWireLimits.MaxDetailBytes, "browser capability detail"));
        Validate(message);
        return message;
    }

    private static void EncodeCommand(WireCodec.PayloadWriter writer, BrowserCommandMessage message)
    {
        writer.Int64(message.Epoch);
        writer.Int64(message.CommandId);
        writer.UInt8((int)message.Action);
        switch (message.Action)
        {
            case BrowserCommandAction.Open:
            case BrowserCommandAction.Navigate:
                writer.Utf8UInt16(message.Url!, BrowserWireLimits.MaxUrlBytes, "browser command URL");
                break;

            case BrowserCommandAction.SetPreviewEnabled:
                writer.UInt8(message.PreviewEnabled!.Value ? 1 : 0);
                break;
        }
    }

    private static BrowserCommandMessage DecodeCommand(ref WireCodec.PayloadReader reader)
    {
        var epoch = reader.Int64("browser command epoch");
        var commandId = reader.Int64("browser command ID");
        var action = ReadEnum<BrowserCommandAction>(ref reader, "browser command action");
        var message = action switch
        {
            BrowserCommandAction.Open or BrowserCommandAction.Navigate => new BrowserCommandMessage(
                epoch,
                commandId,
                action,
                reader.Utf8UInt16(BrowserWireLimits.MaxUrlBytes, "browser command URL")),
            BrowserCommandAction.SetPreviewEnabled => new BrowserCommandMessage(
                epoch,
                commandId,
                action,
                PreviewEnabled: reader.Boolean("browser preview enabled")),
            _ => new BrowserCommandMessage(epoch, commandId, action),
        };
        Validate(message);
        return message;
    }

    private static void EncodeInput(WireCodec.PayloadWriter writer, BrowserInputMessage message)
    {
        writer.Int64(message.Epoch);
        writer.Int64(message.Sequence);
        writer.UInt8(message.Event.EventId);
        switch (message.Event)
        {
            case BrowserPointerInput pointer:
                writer.UInt8((int)pointer.Action);
                WritePreviewReference(writer, pointer.NavigationId, pointer.FrameId, pointer.X, pointer.Y);
                writer.Int32(pointer.Buttons);
                break;

            case BrowserScrollInput scroll:
                WritePreviewReference(writer, scroll.NavigationId, scroll.FrameId, scroll.X, scroll.Y);
                writer.Int32(scroll.DeltaX);
                writer.Int32(scroll.DeltaY);
                break;

            case BrowserSemanticKeyInput key:
                writer.UInt8((int)key.Key);
                break;

            case BrowserTextInput text:
                writer.Utf8UInt16(text.Text, BrowserWireLimits.MaxTextBytes, "browser text input");
                break;

            default:
                throw new WireFormatException($"Unhandled browser input type: {message.Event.GetType().Name}.");
        }
    }

    private static BrowserInputMessage DecodeInput(ref WireCodec.PayloadReader reader)
    {
        var epoch = reader.Int64("browser input epoch");
        var sequence = reader.Int64("browser input sequence");
        var eventId = reader.UInt8("browser input type");
        BrowserInputEvent input = eventId switch
        {
            1 => DecodePointerInput(ref reader),
            2 => DecodeScrollInput(ref reader),
            3 => new BrowserSemanticKeyInput(
                ReadEnum<BrowserSemanticKey>(ref reader, "browser semantic key")),
            4 => new BrowserTextInput(reader.Utf8UInt16(BrowserWireLimits.MaxTextBytes, "browser text input")),
            _ => throw new WireFormatException($"Unknown browser input type: {eventId}."),
        };
        var message = new BrowserInputMessage(epoch, sequence, input);
        Validate(message);
        return message;
    }

    private static BrowserPointerInput DecodePointerInput(ref WireCodec.PayloadReader reader)
    {
        var action = ReadEnum<BrowserPointerAction>(ref reader, "browser pointer action");
        var reference = ReadPreviewReference(ref reader);
        return new BrowserPointerInput(
            action,
            reference.NavigationId,
            reference.FrameId,
            reference.X,
            reference.Y,
            reader.Int32("browser pointer buttons"));
    }

    private static BrowserScrollInput DecodeScrollInput(ref WireCodec.PayloadReader reader)
    {
        var reference = ReadPreviewReference(ref reader);
        return new BrowserScrollInput(
            reference.NavigationId,
            reference.FrameId,
            reference.X,
            reference.Y,
            reader.Int32("browser scroll delta X"),
            reader.Int32("browser scroll delta Y"));
    }

    private static void WritePreviewReference(
        WireCodec.PayloadWriter writer,
        long navigationId,
        long frameId,
        int x,
        int y)
    {
        writer.Int64(navigationId);
        writer.Int64(frameId);
        writer.UInt16(x);
        writer.UInt16(y);
    }

    private static BrowserPreviewReference ReadPreviewReference(ref WireCodec.PayloadReader reader) => new(
        reader.Int64("browser input navigation ID"),
        reader.Int64("browser input frame ID"),
        reader.UInt16("browser input X"),
        reader.UInt16("browser input Y"));

    private static void EncodeState(WireCodec.PayloadWriter writer, BrowserStateMessage message)
    {
        writer.Int64(message.Epoch);
        writer.Int64(message.Revision);
        writer.Int64(message.NavigationId);
        writer.Int64(message.LastAcceptedCommandId);
        writer.Int64(message.LastAcceptedInputSequence);
        writer.UInt8((int)message.LoadState);
        writer.Utf8UInt16(message.Url, BrowserWireLimits.MaxUrlBytes, "browser state URL");
        writer.Utf8UInt16(message.Title, BrowserWireLimits.MaxTitleBytes, "browser state title");
        writer.UInt8(message.Progress);
        writer.UInt8(message.CanGoBack ? 1 : 0);
        writer.UInt8(message.CanGoForward ? 1 : 0);
        writer.Int32(message.ViewportWidth);
        writer.Int32(message.ViewportHeight);
        writer.UInt8((int)message.PreviewState);
        writer.Utf8UInt16(message.ErrorDetail, BrowserWireLimits.MaxDetailBytes, "browser state error detail");
    }

    private static BrowserStateMessage DecodeState(ref WireCodec.PayloadReader reader)
    {
        var message = new BrowserStateMessage(
            reader.Int64("browser state epoch"),
            reader.Int64("browser state revision"),
            reader.Int64("browser state navigation ID"),
            reader.Int64("browser state last accepted command ID"),
            reader.Int64("browser state last accepted input sequence"),
            ReadEnum<BrowserLoadState>(ref reader, "browser load state"),
            reader.Utf8UInt16(BrowserWireLimits.MaxUrlBytes, "browser state URL"),
            reader.Utf8UInt16(BrowserWireLimits.MaxTitleBytes, "browser state title"),
            reader.UInt8("browser state progress"),
            reader.Boolean("browser state can go back"),
            reader.Boolean("browser state can go forward"),
            reader.Int32("browser viewport width"),
            reader.Int32("browser viewport height"),
            ReadEnum<BrowserPreviewState>(ref reader, "browser preview state"),
            reader.Utf8UInt16(BrowserWireLimits.MaxDetailBytes, "browser state error detail"));
        Validate(message);
        return message;
    }

    private static void EncodePreview(WireCodec.PayloadWriter writer, BrowserPreviewMessage message)
    {
        writer.Int64(message.Epoch);
        writer.Int64(message.NavigationId);
        writer.Int64(message.FrameId);
        writer.UInt16(message.Width);
        writer.UInt16(message.Height);
        writer.Binary(message.Jpeg, BrowserWireLimits.MaxPreviewBytes, "browser preview JPEG");
    }

    private static BrowserPreviewMessage DecodePreview(ref WireCodec.PayloadReader reader)
    {
        var message = new BrowserPreviewMessage(
            reader.Int64("browser preview epoch"),
            reader.Int64("browser preview navigation ID"),
            reader.Int64("browser preview frame ID"),
            reader.UInt16("browser preview width"),
            reader.UInt16("browser preview height"),
            reader.Binary(BrowserWireLimits.MaxPreviewBytes, "browser preview JPEG"));
        Validate(message);
        return message;
    }

    private static void EncodeDialog(WireCodec.PayloadWriter writer, BrowserDialogMessage message)
    {
        writer.Int64(message.Epoch);
        writer.Int64(message.DialogId);
        writer.UInt8((int)message.Type);
        writer.Utf8UInt16(message.Origin, BrowserWireLimits.MaxOriginBytes, "browser dialog origin");
        writer.Utf8UInt16(message.Message, BrowserWireLimits.MaxDialogBytes, "browser dialog message");
        writer.Utf8UInt16(message.DefaultValue, BrowserWireLimits.MaxDialogBytes, "browser dialog default value");
        writer.Int32(message.TimeoutMilliseconds);
    }

    private static BrowserDialogMessage DecodeDialog(ref WireCodec.PayloadReader reader)
    {
        var message = new BrowserDialogMessage(
            reader.Int64("browser dialog epoch"),
            reader.Int64("browser dialog ID"),
            ReadEnum<BrowserDialogType>(ref reader, "browser dialog type"),
            reader.Utf8UInt16(BrowserWireLimits.MaxOriginBytes, "browser dialog origin"),
            reader.Utf8UInt16(BrowserWireLimits.MaxDialogBytes, "browser dialog message"),
            reader.Utf8UInt16(BrowserWireLimits.MaxDialogBytes, "browser dialog default value"),
            reader.Int32("browser dialog timeout"));
        Validate(message);
        return message;
    }

    private static void EncodeDialogReply(WireCodec.PayloadWriter writer, BrowserDialogReplyMessage message)
    {
        writer.Int64(message.Epoch);
        writer.Int64(message.DialogId);
        writer.UInt8(message.Accepted ? 1 : 0);
        writer.NullableUtf8UInt16(message.PromptText, BrowserWireLimits.MaxTextBytes, "browser dialog prompt text");
    }

    private static BrowserDialogReplyMessage DecodeDialogReply(ref WireCodec.PayloadReader reader)
    {
        var message = new BrowserDialogReplyMessage(
            reader.Int64("browser dialog reply epoch"),
            reader.Int64("browser dialog reply ID"),
            reader.Boolean("browser dialog accepted"),
            reader.NullableUtf8UInt16(BrowserWireLimits.MaxTextBytes, "browser dialog prompt text"));
        Validate(message);
        return message;
    }

    private static TEnum ReadEnum<TEnum>(ref WireCodec.PayloadReader reader, string field)
        where TEnum : struct, Enum
    {
        var value = reader.UInt8(field);
        if (!Enum.IsDefined(typeof(TEnum), value))
        {
            throw new WireFormatException($"Unknown {field}: {value}.");
        }

        return (TEnum)Enum.ToObject(typeof(TEnum), value);
    }

    private static void Validate(BrowserCapabilityMessage message)
    {
        RequireEnum(message.Status, "browser capability status");
        Require(message.ApiLevel is > 0 and <= ushort.MaxValue, "Browser API level is out of range.");
        RequireNotBlank(message.WebViewVersion, "Browser WebView version is blank.");
        RequireTextLength(message.WebViewVersion, BrowserWireLimits.MaxTitleBytes, "Browser WebView version");
        RequireTextLength(message.Detail, BrowserWireLimits.MaxDetailBytes, "Browser capability detail");
        Require(message.SecureEndpointPort is >= 0 and <= ushort.MaxValue, "Browser endpoint port is out of range.");
        if (message.Status == BrowserCapabilityStatus.Available)
        {
            Require(message.SecureEndpointPort > 0, "Available browser capability requires a secure endpoint.");
        }

        if (!message.PreviewSupported)
        {
            Require(
                message.PreviewMaxWidth == 0
                && message.PreviewMaxHeight == 0
                && message.InteractivePreviewFramesPerSecond == 0
                && message.IdlePreviewFramesPerSecond == 0
                && message.PreviewMaxBytes == 0,
                "Unavailable preview must advertise zero limits.");
            return;
        }

        Require(message.PreviewMaxWidth is > 0 and <= BrowserWireLimits.MaxPreviewWidth,
            "Browser preview width is out of range.");
        Require(message.PreviewMaxHeight is > 0 and <= BrowserWireLimits.MaxPreviewHeight,
            "Browser preview height is out of range.");
        Require(message.InteractivePreviewFramesPerSecond is > 0 and <= BrowserWireLimits.MaxInteractivePreviewFramesPerSecond,
            "Browser interactive preview rate is out of range.");
        Require(message.IdlePreviewFramesPerSecond is > 0 and <= BrowserWireLimits.MaxIdlePreviewFramesPerSecond,
            "Browser idle preview rate is out of range.");
        Require(message.PreviewMaxBytes is > 0 and <= BrowserWireLimits.MaxPreviewBytes,
            "Browser preview byte limit is out of range.");
    }

    private static void Validate(BrowserCommandMessage message)
    {
        RequirePositive(message.Epoch, "browser command epoch");
        RequirePositive(message.CommandId, "browser command ID");
        RequireEnum(message.Action, "browser command action");
        var requiresUrl = message.Action is BrowserCommandAction.Open or BrowserCommandAction.Navigate;
        var requiresPreview = message.Action == BrowserCommandAction.SetPreviewEnabled;
        Require(requiresUrl == (message.Url is not null), "Browser command URL has an invalid action combination.");
        Require(requiresPreview == message.PreviewEnabled.HasValue,
            "Browser preview setting has an invalid action combination.");
        if (requiresUrl)
        {
            RequireNotBlank(message.Url!, "Browser command URL is blank.");
            RequireTextLength(message.Url!, BrowserWireLimits.MaxUrlBytes, "Browser command URL");
        }
    }

    private static void Validate(BrowserInputMessage message)
    {
        RequirePositive(message.Epoch, "browser input epoch");
        RequirePositive(message.Sequence, "browser input sequence");
        ArgumentNullException.ThrowIfNull(message.Event);
        switch (message.Event)
        {
            case BrowserPointerInput pointer:
                RequireEnum(pointer.Action, "browser pointer action");
                ValidatePreviewReference(pointer.NavigationId, pointer.FrameId, pointer.X, pointer.Y);
                Require(pointer.Buttons is 0 or 1, "Browser pointer buttons must be the primary bit or zero.");
                Require(
                    pointer.Action switch
                    {
                        BrowserPointerAction.Down => pointer.Buttons == 1,
                        BrowserPointerAction.Up or BrowserPointerAction.Cancel => pointer.Buttons == 0,
                        BrowserPointerAction.Move => true,
                        _ => false,
                    },
                    "Browser pointer action has an invalid button transition.");
                break;

            case BrowserScrollInput scroll:
                ValidatePreviewReference(scroll.NavigationId, scroll.FrameId, scroll.X, scroll.Y);
                Require(scroll.DeltaX != 0 || scroll.DeltaY != 0, "Browser scroll must have a delta.");
                break;

            case BrowserSemanticKeyInput key:
                RequireEnum(key.Key, "browser semantic key");
                break;

            case BrowserTextInput text:
                RequireNotNull(text.Text, "Browser text input is null.");
                RequireTextLength(text.Text, BrowserWireLimits.MaxTextBytes, "Browser text input");
                break;

            default:
                throw new WireFormatException($"Unhandled browser input type: {message.Event.GetType().Name}.");
        }
    }

    private static void Validate(BrowserStateMessage message)
    {
        RequirePositive(message.Epoch, "browser state epoch");
        RequirePositive(message.Revision, "browser state revision");
        RequireNonNegative(message.NavigationId, "browser state navigation ID");
        RequireNonNegative(message.LastAcceptedCommandId, "browser state command acknowledgement");
        RequireNonNegative(message.LastAcceptedInputSequence, "browser state input acknowledgement");
        RequireEnum(message.LoadState, "browser load state");
        RequireNotNull(message.Url, "Browser state URL is null.");
        RequireNotNull(message.Title, "Browser state title is null.");
        RequireNotNull(message.ErrorDetail, "Browser state error detail is null.");
        RequireTextLength(message.Url, BrowserWireLimits.MaxUrlBytes, "Browser state URL");
        RequireTextLength(message.Title, BrowserWireLimits.MaxTitleBytes, "Browser state title");
        RequireTextLength(message.ErrorDetail, BrowserWireLimits.MaxDetailBytes, "Browser state error detail");
        Require(message.Progress is >= 0 and <= 100, "Browser state progress is out of range.");
        Require(message.ViewportWidth is >= 0 and <= MaxViewportDimension,
            "Browser viewport width is out of range.");
        Require(message.ViewportHeight is >= 0 and <= MaxViewportDimension,
            "Browser viewport height is out of range.");
        RequireEnum(message.PreviewState, "browser preview state");
        if (message.LoadState == BrowserLoadState.Failed)
        {
            RequireNotBlank(message.ErrorDetail, "Failed browser state requires an error detail.");
        }
        else
        {
            Require(message.ErrorDetail.Length == 0, "Only failed browser state carries an error detail.");
        }

        if (message.LoadState is BrowserLoadState.Loading or BrowserLoadState.Loaded or BrowserLoadState.Failed)
        {
            RequirePositive(message.NavigationId, "active browser state navigation ID");
            RequireNotBlank(message.Url, "Active browser state URL is blank.");
        }
    }

    private static void Validate(BrowserPreviewMessage message)
    {
        RequirePositive(message.Epoch, "browser preview epoch");
        RequirePositive(message.NavigationId, "browser preview navigation ID");
        RequirePositive(message.FrameId, "browser preview frame ID");
        Require(message.Width is > 0 and <= BrowserWireLimits.MaxPreviewWidth,
            "Browser preview width is out of range.");
        Require(message.Height is > 0 and <= BrowserWireLimits.MaxPreviewHeight,
            "Browser preview height is out of range.");
        Require(message.Jpeg.Length is > 0 and <= BrowserWireLimits.MaxPreviewBytes,
            "Browser preview JPEG length is out of range.");
    }

    private static void Validate(BrowserDialogMessage message)
    {
        RequirePositive(message.Epoch, "browser dialog epoch");
        RequirePositive(message.DialogId, "browser dialog ID");
        RequireEnum(message.Type, "browser dialog type");
        RequireNotBlank(message.Origin, "Browser dialog origin is blank.");
        RequireNotNull(message.Message, "Browser dialog message is null.");
        RequireNotNull(message.DefaultValue, "Browser dialog default value is null.");
        RequireTextLength(message.Origin, BrowserWireLimits.MaxOriginBytes, "Browser dialog origin");
        RequireTextLength(message.Message, BrowserWireLimits.MaxDialogBytes, "Browser dialog message");
        RequireTextLength(message.DefaultValue, BrowserWireLimits.MaxDialogBytes, "Browser dialog default value");
        Require(message.TimeoutMilliseconds is > 0 and <= BrowserWireLimits.MaxDialogTimeoutMilliseconds,
            "Browser dialog timeout is out of range.");
        if (message.Type != BrowserDialogType.Prompt)
        {
            Require(message.DefaultValue.Length == 0, "Only browser prompts carry a default value.");
        }
    }

    private static void Validate(BrowserDialogReplyMessage message)
    {
        RequirePositive(message.Epoch, "browser dialog reply epoch");
        RequirePositive(message.DialogId, "browser dialog reply ID");
        if (!message.Accepted)
        {
            Require(message.PromptText is null, "Cancelled browser dialog reply carries prompt text.");
            return;
        }

        if (message.PromptText is not null)
        {
            RequireTextLength(message.PromptText, BrowserWireLimits.MaxTextBytes, "Browser dialog prompt text");
        }
    }

    private static void ValidatePreviewReference(long navigationId, long frameId, int x, int y)
    {
        RequirePositive(navigationId, "browser input navigation ID");
        RequirePositive(frameId, "browser input frame ID");
        Require(x is >= 0 and <= ushort.MaxValue, "Browser input X is out of range.");
        Require(y is >= 0 and <= ushort.MaxValue, "Browser input Y is out of range.");
    }

    private static void RequireEnum<TEnum>(TEnum value, string field)
        where TEnum : struct, Enum => Require(Enum.IsDefined(value), $"Unknown {field}: {value}.");

    private static void RequirePositive(long value, string field) => Require(value > 0, $"{field} must be positive.");

    private static void RequireNonNegative(long value, string field) => Require(value >= 0, $"{field} is negative.");

    private static void RequireNotBlank(string value, string message)
    {
        RequireNotNull(value, message);
        Require(!string.IsNullOrWhiteSpace(value), message);
    }

    private static void RequireNotNull(string? value, string message) => Require(value is not null, message);

    private static void RequireTextLength(string value, int maximumBytes, string field)
    {
        RequireNotNull(value, $"{field} is null.");
        var length = System.Text.Encoding.UTF8.GetByteCount(value);
        Require(length <= maximumBytes, $"{field} is too long: {length} bytes.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new WireFormatException(message);
        }
    }

    private readonly record struct BrowserPreviewReference(long NavigationId, long FrameId, int X, int Y);
}
