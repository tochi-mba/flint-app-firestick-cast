namespace Flint.Protocol;

/// <summary>Strict payload codec for additive browser messages 21 through 31.</summary>
internal static partial class BrowserWireCodec
{
    private static void EncodeTabCommand(
        WireCodec.PayloadWriter writer,
        BrowserTabCommandMessage message)
    {
        writer.Int64(message.Epoch);
        writer.Int64(message.CommandId);
        writer.UInt8((int)message.Action);
        writer.Int64(message.TabId);
        writer.NullableUtf8UInt16(
            message.Url,
            BrowserWireLimits.MaxUrlBytes,
            "browser tab command URL");
    }

    private static BrowserTabCommandMessage DecodeTabCommand(ref WireCodec.PayloadReader reader)
    {
        var message = new BrowserTabCommandMessage(
            reader.Int64("browser tab command epoch"),
            reader.Int64("browser tab command ID"),
            ReadEnum<BrowserTabAction>(ref reader, "browser tab command action"),
            reader.Int64("browser tab command tab ID"),
            reader.NullableUtf8UInt16(BrowserWireLimits.MaxUrlBytes, "browser tab command URL"));
        Validate(message);
        return message;
    }

    private static void EncodeTabState(WireCodec.PayloadWriter writer, BrowserTabStateMessage message)
    {
        writer.Int64(message.Epoch);
        writer.Int64(message.Revision);
        writer.Int64(message.ActiveTabId);
        writer.UInt8(message.Tabs.Count);
        foreach (var tab in message.Tabs)
        {
            writer.Int64(tab.TabId);
            writer.UInt8((int)tab.LoadState);
            writer.UInt8(tab.Progress);
            writer.UInt8(tab.CanGoBack ? 1 : 0);
            writer.UInt8(tab.CanGoForward ? 1 : 0);
            writer.UInt8(tab.Frozen ? 1 : 0);
            writer.Int64(tab.FaviconId);
            writer.Utf8UInt16(tab.Url, BrowserWireLimits.MaxUrlBytes, "browser tab URL");
            writer.Utf8UInt16(tab.Title, BrowserWireLimits.MaxTitleBytes, "browser tab title");
        }
    }

    private static BrowserTabStateMessage DecodeTabState(ref WireCodec.PayloadReader reader)
    {
        var epoch = reader.Int64("browser tab state epoch");
        var revision = reader.Int64("browser tab state revision");
        var activeTabId = reader.Int64("browser active tab ID");
        var count = reader.UInt8("browser tab count");
        Require(count <= BrowserWireLimits.MaxTabs, $"Too many browser tabs: {count}.");

        var tabs = new List<BrowserTabStateEntry>(count);
        for (var index = 0; index < count; index++)
        {
            tabs.Add(new BrowserTabStateEntry(
                reader.Int64("browser tab ID"),
                ReadEnum<BrowserLoadState>(ref reader, "browser tab load state"),
                reader.UInt8("browser tab progress"),
                reader.Boolean("browser tab can-go-back flag"),
                reader.Boolean("browser tab can-go-forward flag"),
                reader.Boolean("browser tab frozen flag"),
                reader.Int64("browser tab favicon ID"),
                reader.Utf8UInt16(BrowserWireLimits.MaxUrlBytes, "browser tab URL"),
                reader.Utf8UInt16(BrowserWireLimits.MaxTitleBytes, "browser tab title")));
        }

        var message = new BrowserTabStateMessage(epoch, revision, activeTabId, ValueList<BrowserTabStateEntry>.From(tabs));
        Validate(message);
        return message;
    }

    private static void EncodeViewCommand(
        WireCodec.PayloadWriter writer,
        BrowserViewCommandMessage message)
    {
        writer.Int64(message.Epoch);
        writer.Int64(message.CommandId);
        writer.UInt8((int)message.Action);
        writer.Int32(message.Value);
        writer.Utf8UInt16(message.Text, ViewTextLimit(message.Action), "browser view command text");
    }

    private static BrowserViewCommandMessage DecodeViewCommand(ref WireCodec.PayloadReader reader)
    {
        var epoch = reader.Int64("browser view command epoch");
        var commandId = reader.Int64("browser view command ID");
        var action = ReadEnum<BrowserViewAction>(ref reader, "browser view command action");
        var message = new BrowserViewCommandMessage(
            epoch,
            commandId,
            action,
            reader.Int32("browser view command value"),
            reader.Utf8UInt16(ViewTextLimit(action), "browser view command text"));
        Validate(message);
        return message;
    }

    private static void EncodeViewState(WireCodec.PayloadWriter writer, BrowserViewStateMessage message)
    {
        writer.Int64(message.Epoch);
        writer.Int64(message.Revision);
        writer.Int32(message.ZoomPercent);
        writer.UInt8((int)message.UaMode);
        writer.UInt8((int)message.DarkMode);
        writer.UInt8((int)message.InputMode);
        writer.UInt8(message.Fullscreen ? 1 : 0);
        writer.UInt8(message.MediaPlaying ? 1 : 0);
        writer.UInt8(message.EditingFocused ? 1 : 0);
        writer.UInt8(message.FindActive ? 1 : 0);
        writer.Int32(message.FindCurrent);
        writer.Int32(message.FindTotal);
        writer.UInt8((int)message.SearchEngine);
    }

    private static BrowserViewStateMessage DecodeViewState(ref WireCodec.PayloadReader reader)
    {
        var message = new BrowserViewStateMessage(
            reader.Int64("browser view state epoch"),
            reader.Int64("browser view state revision"),
            reader.Int32("browser zoom percent"),
            ReadEnum<BrowserUserAgentMode>(ref reader, "browser user-agent mode"),
            ReadEnum<BrowserDarkMode>(ref reader, "browser dark mode"),
            ReadEnum<BrowserInteractionMode>(ref reader, "browser input mode"),
            reader.Boolean("browser fullscreen flag"),
            reader.Boolean("browser media-playing flag"),
            reader.Boolean("browser editing-focused flag"),
            reader.Boolean("browser find-active flag"),
            reader.Int32("browser find current"),
            reader.Int32("browser find total"),
            ReadEnum<BrowserSearchEngine>(ref reader, "browser search engine"));
        Validate(message);
        return message;
    }

    private static void EncodeFavicon(WireCodec.PayloadWriter writer, BrowserFaviconMessage message)
    {
        writer.Int64(message.Epoch);
        writer.Int64(message.FaviconId);
        writer.UInt16(message.Width);
        writer.UInt16(message.Height);
        writer.Binary(message.Png, BrowserWireLimits.MaxFaviconBytes, "browser favicon PNG");
    }

    private static BrowserFaviconMessage DecodeFavicon(ref WireCodec.PayloadReader reader)
    {
        var message = new BrowserFaviconMessage(
            reader.Int64("browser favicon epoch"),
            reader.Int64("browser favicon ID"),
            reader.UInt16("browser favicon width"),
            reader.UInt16("browser favicon height"),
            reader.Binary(BrowserWireLimits.MaxFaviconBytes, "browser favicon PNG"));
        Validate(message);
        return message;
    }

    private static void EncodeLibraryCommand(
        WireCodec.PayloadWriter writer,
        BrowserLibraryCommandMessage message)
    {
        writer.Int64(message.Epoch);
        writer.Int64(message.CommandId);
        writer.UInt8((int)message.Action);
        writer.Utf8UInt16(message.Url, BrowserWireLimits.MaxUrlBytes, "browser library command URL");
        writer.Utf8UInt16(message.Title, BrowserWireLimits.MaxTitleBytes, "browser library command title");
    }

    private static BrowserLibraryCommandMessage DecodeLibraryCommand(ref WireCodec.PayloadReader reader)
    {
        var message = new BrowserLibraryCommandMessage(
            reader.Int64("browser library command epoch"),
            reader.Int64("browser library command ID"),
            ReadEnum<BrowserLibraryAction>(ref reader, "browser library command action"),
            reader.Utf8UInt16(BrowserWireLimits.MaxUrlBytes, "browser library command URL"),
            reader.Utf8UInt16(BrowserWireLimits.MaxTitleBytes, "browser library command title"));
        Validate(message);
        return message;
    }

    private static void EncodeLibraryState(WireCodec.PayloadWriter writer, BrowserLibraryStateMessage message)
    {
        writer.Int64(message.Epoch);
        writer.Int64(message.Revision);
        writer.UInt8(message.Bookmarks.Count);
        writer.UInt8(message.History.Count);
        foreach (var entry in message.Bookmarks)
        {
            EncodeLibraryEntry(writer, entry);
        }

        foreach (var entry in message.History)
        {
            EncodeLibraryEntry(writer, entry);
        }
    }

    private static BrowserLibraryStateMessage DecodeLibraryState(ref WireCodec.PayloadReader reader)
    {
        var epoch = reader.Int64("browser library state epoch");
        var revision = reader.Int64("browser library state revision");
        var bookmarkCount = reader.UInt8("browser bookmark count");
        var historyCount = reader.UInt8("browser history count");
        var bookmarks = new List<BrowserLibraryEntry>(bookmarkCount);
        var history = new List<BrowserLibraryEntry>(historyCount);
        for (var index = 0; index < bookmarkCount; index++)
        {
            bookmarks.Add(DecodeLibraryEntry(ref reader));
        }

        for (var index = 0; index < historyCount; index++)
        {
            history.Add(DecodeLibraryEntry(ref reader));
        }

        var message = new BrowserLibraryStateMessage(
            epoch,
            revision,
            ValueList<BrowserLibraryEntry>.From(bookmarks),
            ValueList<BrowserLibraryEntry>.From(history));
        Validate(message);
        return message;
    }

    private static void EncodeLibraryEntry(WireCodec.PayloadWriter writer, BrowserLibraryEntry entry)
    {
        writer.UInt8((int)entry.Kind);
        writer.Int64(entry.FaviconId);
        writer.Int64(entry.LastVisitedMilliseconds);
        writer.Utf8UInt16(entry.Url, BrowserWireLimits.MaxUrlBytes, "browser library URL");
        writer.Utf8UInt16(entry.Title, BrowserWireLimits.MaxTitleBytes, "browser library title");
    }

    private static BrowserLibraryEntry DecodeLibraryEntry(ref WireCodec.PayloadReader reader) => new(
        ReadEnum<BrowserLibraryEntryKind>(ref reader, "browser library entry kind"),
        reader.Int64("browser library favicon ID"),
        reader.Int64("browser library last-visited time"),
        reader.Utf8UInt16(BrowserWireLimits.MaxUrlBytes, "browser library URL"),
        reader.Utf8UInt16(BrowserWireLimits.MaxTitleBytes, "browser library title"));

    private static void EncodeProfileCommand(
        WireCodec.PayloadWriter writer,
        BrowserProfileCommandMessage message)
    {
        writer.Int64(message.Epoch);
        writer.Int64(message.CommandId);
        writer.UInt8((int)message.Action);
        writer.Utf8UInt16(
            message.ProfileId,
            BrowserWireLimits.MaxProfileIdBytes,
            "browser profile command profile ID");
        writer.Utf8UInt16(
            message.Name,
            BrowserWireLimits.MaxProfileNameBytes,
            "browser profile command name");
    }

    private static BrowserProfileCommandMessage DecodeProfileCommand(ref WireCodec.PayloadReader reader)
    {
        var message = new BrowserProfileCommandMessage(
            reader.Int64("browser profile command epoch"),
            reader.Int64("browser profile command ID"),
            ReadEnum<BrowserProfileAction>(ref reader, "browser profile command action"),
            reader.Utf8UInt16(BrowserWireLimits.MaxProfileIdBytes, "browser profile command profile ID"),
            reader.Utf8UInt16(BrowserWireLimits.MaxProfileNameBytes, "browser profile command name"));
        Validate(message);
        return message;
    }

    private static void EncodeProfileState(
        WireCodec.PayloadWriter writer,
        BrowserProfileStateMessage message)
    {
        writer.Int64(message.Epoch);
        writer.Int64(message.Revision);
        writer.UInt8((int)message.ActiveSource);
        writer.Utf8UInt16(
            message.ActiveProfileId,
            BrowserWireLimits.MaxProfileIdBytes,
            "browser active profile ID");
        writer.Utf8UInt16(
            message.DeviceName,
            BrowserWireLimits.MaxDeviceProfileNameBytes,
            "browser profile device name");
        writer.UInt8(message.Profiles.Count);
        foreach (var profile in message.Profiles)
        {
            writer.Utf8UInt16(
                profile.ProfileId,
                BrowserWireLimits.MaxProfileIdBytes,
                "browser profile ID");
            writer.Utf8UInt16(
                profile.Name,
                BrowserWireLimits.MaxProfileNameBytes,
                "browser profile name");
        }
    }

    private static BrowserProfileStateMessage DecodeProfileState(ref WireCodec.PayloadReader reader)
    {
        var epoch = reader.Int64("browser profile state epoch");
        var revision = reader.Int64("browser profile state revision");
        var activeSource = ReadEnum<BrowserProfileSource>(ref reader, "browser active profile source");
        var activeProfileId = reader.Utf8UInt16(
            BrowserWireLimits.MaxProfileIdBytes,
            "browser active profile ID");
        var deviceName = reader.Utf8UInt16(
            BrowserWireLimits.MaxDeviceProfileNameBytes,
            "browser profile device name");
        var count = reader.UInt8("browser profile count");
        Require(count <= BrowserWireLimits.MaxTvProfiles, $"Too many browser profiles: {count}.");
        var profiles = new List<BrowserProfileEntry>(count);
        for (var index = 0; index < count; index++)
        {
            profiles.Add(new BrowserProfileEntry(
                reader.Utf8UInt16(BrowserWireLimits.MaxProfileIdBytes, "browser profile ID"),
                reader.Utf8UInt16(BrowserWireLimits.MaxProfileNameBytes, "browser profile name")));
        }

        var message = new BrowserProfileStateMessage(
            epoch,
            revision,
            activeSource,
            activeProfileId,
            deviceName,
            ValueList<BrowserProfileEntry>.From(profiles));
        Validate(message);
        return message;
    }

    private static void Validate(BrowserTabCommandMessage message)
    {
        RequirePositive(message.Epoch, "browser tab command epoch");
        RequirePositive(message.CommandId, "browser tab command ID");
        RequireEnum(message.Action, "browser tab command action");
        if (message.Action == BrowserTabAction.New)
        {
            Require(message.TabId == 0, "New browser tab command must use tab ID zero.");
        }
        else
        {
            RequirePositive(message.TabId, "browser tab command tab ID");
        }

        Require(message.Action == BrowserTabAction.New || message.Url is null,
            "Only new browser tab command carries a URL.");
        if (message.Url is not null)
        {
            RequireNotBlank(message.Url, "Browser tab command URL is blank.");
            RequireTextLength(message.Url, BrowserWireLimits.MaxUrlBytes, "Browser tab command URL");
        }
    }

    private static void Validate(BrowserTabStateMessage message)
    {
        RequirePositive(message.Epoch, "browser tab state epoch");
        RequirePositive(message.Revision, "browser tab state revision");
        Require(message.Tabs.Count <= BrowserWireLimits.MaxTabs, "Browser tab count is out of range.");
        Require(
            (message.Tabs.IsEmpty && message.ActiveTabId == 0)
            || (!message.Tabs.IsEmpty && message.ActiveTabId > 0),
            "Browser active tab ID does not match tab presence.");

        var tabIds = new HashSet<long>();
        foreach (var tab in message.Tabs)
        {
            if (tab is null)
            {
                throw new WireFormatException("Browser tab entry is null.");
            }

            ValidateTab(tab);
            Require(tabIds.Add(tab.TabId), "Browser tab IDs must be unique.");
        }

        if (!message.Tabs.IsEmpty)
        {
            Require(tabIds.Contains(message.ActiveTabId), "Browser active tab ID is not present.");
        }
    }

    private static void ValidateTab(BrowserTabStateEntry tab)
    {
        RequirePositive(tab.TabId, "browser tab ID");
        RequireEnum(tab.LoadState, "browser tab load state");
        Require(tab.Progress is >= 0 and <= 100, "Browser tab progress is out of range.");
        RequireNonNegative(tab.FaviconId, "browser tab favicon ID");
        RequireNotNull(tab.Url, "Browser tab URL is null.");
        RequireNotNull(tab.Title, "Browser tab title is null.");
        RequireTextLength(tab.Url, BrowserWireLimits.MaxUrlBytes, "Browser tab URL");
        RequireTextLength(tab.Title, BrowserWireLimits.MaxTitleBytes, "Browser tab title");
        if (tab.LoadState is BrowserLoadState.Loading or BrowserLoadState.Loaded or BrowserLoadState.Failed)
        {
            RequireNotBlank(tab.Url, "Active browser tab URL is blank.");
        }
    }

    private static void Validate(BrowserViewCommandMessage message)
    {
        RequirePositive(message.Epoch, "browser view command epoch");
        RequirePositive(message.CommandId, "browser view command ID");
        RequireEnum(message.Action, "browser view command action");
        RequireNotNull(message.Text, "Browser view command text is null.");
        RequireTextLength(message.Text, ViewTextLimit(message.Action), "Browser view command text");

        switch (message.Action)
        {
            case BrowserViewAction.SetZoom:
                Require(message.Value is >= BrowserWireLimits.MinZoomPercent and <= BrowserWireLimits.MaxZoomPercent,
                    "Browser zoom percent is out of range.");
                Require(message.Text.Length == 0, "Set-zoom command carries text.");
                break;

            case BrowserViewAction.SetUa:
                RequireIntEnum<BrowserUserAgentMode>(message.Value, "browser user-agent mode");
                Require(message.Text.Length == 0, "Set-UA command carries text.");
                break;

            case BrowserViewAction.SetDark:
                RequireIntEnum<BrowserDarkMode>(message.Value, "browser dark mode");
                Require(message.Text.Length == 0, "Set-dark command carries text.");
                break;

            case BrowserViewAction.SetInputMode:
                RequireIntEnum<BrowserInteractionMode>(message.Value, "browser input mode");
                Require(message.Text.Length == 0, "Set-input-mode command carries text.");
                break;

            case BrowserViewAction.SetFullscreen:
                Require(message.Value is 0 or 1, "Browser fullscreen value is not boolean.");
                Require(message.Text.Length == 0, "Set-fullscreen command carries text.");
                break;

            case BrowserViewAction.FindStart:
                Require(message.Value == 0, "Find-start command value must be zero.");
                RequireNotBlank(message.Text, "Browser find text is blank.");
                break;

            case BrowserViewAction.FindNext:
            case BrowserViewAction.FindPrev:
            case BrowserViewAction.FindClear:
                Require(message.Value == 0, "Browser find command value must be zero.");
                Require(message.Text.Length == 0, "Browser find command carries text.");
                break;

            case BrowserViewAction.SetSearchEngine:
                RequireIntEnum<BrowserSearchEngine>(message.Value, "browser search engine");
                if ((BrowserSearchEngine)message.Value == BrowserSearchEngine.Custom)
                {
                    RequireNotBlank(message.Text, "Custom browser search template is blank.");
                }
                else
                {
                    Require(message.Text.Length == 0, "Preset browser search-engine command carries text.");
                }

                break;
        }
    }

    private static void Validate(BrowserViewStateMessage message)
    {
        RequirePositive(message.Epoch, "browser view state epoch");
        RequirePositive(message.Revision, "browser view state revision");
        Require(message.ZoomPercent is >= BrowserWireLimits.MinZoomPercent and <= BrowserWireLimits.MaxZoomPercent,
            "Browser zoom percent is out of range.");
        RequireEnum(message.UaMode, "browser user-agent mode");
        RequireEnum(message.DarkMode, "browser dark mode");
        RequireEnum(message.InputMode, "browser input mode");
        RequireEnum(message.SearchEngine, "browser search engine");
        Require(message.FindCurrent >= 0 && message.FindTotal >= 0, "Browser find counters are negative.");
        if (message.FindActive)
        {
            Require(
                (message.FindTotal == 0 && message.FindCurrent == 0)
                || (message.FindTotal > 0 && message.FindCurrent is >= 1 && message.FindCurrent <= message.FindTotal),
                "Browser find counters are inconsistent.");
        }
        else
        {
            Require(message.FindCurrent == 0 && message.FindTotal == 0,
                "Inactive browser find carries counters.");
        }
    }

    private static void Validate(BrowserFaviconMessage message)
    {
        RequirePositive(message.Epoch, "browser favicon epoch");
        RequirePositive(message.FaviconId, "browser favicon ID");
        Require(message.Width is > 0 and <= BrowserWireLimits.MaxFaviconDimension,
            "Browser favicon width is out of range.");
        Require(message.Height is > 0 and <= BrowserWireLimits.MaxFaviconDimension,
            "Browser favicon height is out of range.");
        Require(message.Png.Length is > 0 and <= BrowserWireLimits.MaxFaviconBytes,
            "Browser favicon PNG length is out of range.");
    }

    private static void Validate(BrowserLibraryCommandMessage message)
    {
        RequirePositive(message.Epoch, "browser library command epoch");
        RequirePositive(message.CommandId, "browser library command ID");
        RequireEnum(message.Action, "browser library command action");
        RequireNotNull(message.Url, "Browser library command URL is null.");
        RequireNotNull(message.Title, "Browser library command title is null.");
        RequireTextLength(message.Url, BrowserWireLimits.MaxUrlBytes, "Browser library command URL");
        RequireTextLength(message.Title, BrowserWireLimits.MaxTitleBytes, "Browser library command title");
        switch (message.Action)
        {
            case BrowserLibraryAction.AddBookmark:
                RequireNotBlank(message.Url, "Add-bookmark URL is blank.");
                break;

            case BrowserLibraryAction.RemoveBookmark:
                RequireNotBlank(message.Url, "Remove-bookmark URL is blank.");
                Require(message.Title.Length == 0, "Remove-bookmark command carries a title.");
                break;

            case BrowserLibraryAction.ClearHistory:
            case BrowserLibraryAction.ClearBookmarks:
            case BrowserLibraryAction.RequestSnapshot:
                Require(message.Url.Length == 0 && message.Title.Length == 0,
                    "Parameterless browser library command carries text.");
                break;
        }
    }

    private static void Validate(BrowserLibraryStateMessage message)
    {
        RequirePositive(message.Epoch, "browser library state epoch");
        RequirePositive(message.Revision, "browser library state revision");
        Require(message.Bookmarks.Count <= BrowserWireLimits.MaxBookmarks,
            "Browser bookmark count is out of range.");
        Require(message.History.Count <= BrowserWireLimits.MaxHistoryEntries,
            "Browser history count is out of range.");
        foreach (var entry in message.Bookmarks)
        {
            ValidateLibraryEntry(entry, BrowserLibraryEntryKind.Bookmark);
        }

        foreach (var entry in message.History)
        {
            ValidateLibraryEntry(entry, BrowserLibraryEntryKind.History);
        }
    }

    private static void ValidateLibraryEntry(
        BrowserLibraryEntry entry,
        BrowserLibraryEntryKind expectedKind)
    {
        if (entry is null)
        {
            throw new WireFormatException("Browser library entry is null.");
        }

        RequireEnum(entry.Kind, "browser library entry kind");
        Require(entry.Kind == expectedKind, "Browser library entry is in the wrong collection.");
        RequireNonNegative(entry.FaviconId, "browser library favicon ID");
        RequireNonNegative(entry.LastVisitedMilliseconds, "browser library last-visited time");
        RequireNotBlank(entry.Url, "Browser library URL is blank.");
        RequireNotNull(entry.Title, "Browser library title is null.");
        RequireTextLength(entry.Url, BrowserWireLimits.MaxUrlBytes, "Browser library URL");
        RequireTextLength(entry.Title, BrowserWireLimits.MaxTitleBytes, "Browser library title");
    }

    private static void Validate(BrowserProfileCommandMessage message)
    {
        RequirePositive(message.Epoch, "browser profile command epoch");
        RequirePositive(message.CommandId, "browser profile command ID");
        RequireEnum(message.Action, "browser profile command action");
        RequireNotNull(message.ProfileId, "Browser profile command profile ID is null.");
        RequireNotNull(message.Name, "Browser profile command name is null.");
        RequireTextLength(
            message.ProfileId,
            BrowserWireLimits.MaxProfileIdBytes,
            "Browser profile command profile ID");
        RequireTextLength(
            message.Name,
            BrowserWireLimits.MaxProfileNameBytes,
            "Browser profile command name");

        switch (message.Action)
        {
            case BrowserProfileAction.SelectTvProfile:
            case BrowserProfileAction.DeleteTvProfile:
                ValidateProfileId(message.ProfileId);
                Require(message.Name.Length == 0, "Browser profile command carries a name.");
                break;

            case BrowserProfileAction.CreateTvProfile:
                Require(message.ProfileId.Length == 0, "Create-profile command carries a profile ID.");
                ValidateProfileName(message.Name);
                break;

            case BrowserProfileAction.RenameTvProfile:
                ValidateProfileId(message.ProfileId);
                ValidateProfileName(message.Name);
                break;

            case BrowserProfileAction.SelectDevice:
            case BrowserProfileAction.RequestSnapshot:
                Require(
                    message.ProfileId.Length == 0 && message.Name.Length == 0,
                    "Parameterless browser profile command carries text.");
                break;
        }
    }

    private static void Validate(BrowserProfileStateMessage message)
    {
        RequirePositive(message.Epoch, "browser profile state epoch");
        RequirePositive(message.Revision, "browser profile state revision");
        RequireEnum(message.ActiveSource, "browser active profile source");
        RequireNotNull(message.ActiveProfileId, "Browser active profile ID is null.");
        RequireNotNull(message.DeviceName, "Browser profile device name is null.");
        RequireTextLength(
            message.ActiveProfileId,
            BrowserWireLimits.MaxProfileIdBytes,
            "Browser active profile ID");
        ValidatePrintable(
            message.DeviceName,
            BrowserWireLimits.MaxDeviceProfileNameBytes,
            "Browser profile device name");
        Require(message.Profiles.Count <= BrowserWireLimits.MaxTvProfiles,
            "Browser profile count is out of range.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in message.Profiles)
        {
            if (profile is null)
            {
                throw new WireFormatException("Browser profile entry is null.");
            }

            ValidateProfileId(profile.ProfileId);
            ValidateProfileName(profile.Name);
            Require(ids.Add(profile.ProfileId), "Browser profile IDs must be unique.");
            Require(names.Add(profile.Name), "Browser profile names must be unique.");
        }

        if (message.ActiveSource == BrowserProfileSource.Tv)
        {
            ValidateProfileId(message.ActiveProfileId);
            Require(ids.Contains(message.ActiveProfileId), "Active TV browser profile is not present.");
        }
        else
        {
            Require(message.ActiveProfileId.Length == 0, "Device profile source carries a TV profile ID.");
        }
    }

    private static void EncodeNetworkCommand(
        WireCodec.PayloadWriter writer,
        BrowserNetworkCommandMessage message)
    {
        writer.Int64(message.Epoch);
        writer.Int64(message.CommandId);
        writer.UInt8((int)message.Action);
        writer.Utf8UInt16(
            message.ProfileId,
            BrowserWireLimits.MaxProfileIdBytes,
            "browser network command profile ID");
        writer.UInt8(message.VpnEnabled ? 1 : 0);
        writer.UInt8((int)message.Provider);
        writer.UInt8(message.AutoConnectOnBrowserStart ? 1 : 0);
        writer.UInt8(message.RequireVpnBeforeBrowse ? 1 : 0);
        writer.Utf8UInt32(
            message.ConfigText,
            BrowserWireLimits.MaxConfigUtf8Bytes,
            "browser network command config");
    }

    private static BrowserNetworkCommandMessage DecodeNetworkCommand(ref WireCodec.PayloadReader reader)
    {
        var message = new BrowserNetworkCommandMessage(
            reader.Int64("browser network command epoch"),
            reader.Int64("browser network command ID"),
            ReadEnum<BrowserNetworkAction>(ref reader, "browser network command action"),
            reader.Utf8UInt16(BrowserWireLimits.MaxProfileIdBytes, "browser network command profile ID"),
            reader.Boolean("browser network command vpn enabled"),
            ReadEnum<BrowserVpnProvider>(ref reader, "browser network command provider"),
            reader.Boolean("browser network command auto-connect"),
            reader.Boolean("browser network command require VPN before browse"),
            reader.Utf8UInt32(BrowserWireLimits.MaxConfigUtf8Bytes, "browser network command config"));
        Validate(message);
        return message;
    }

    private static void EncodeNetworkState(
        WireCodec.PayloadWriter writer,
        BrowserNetworkStateMessage message)
    {
        writer.Int64(message.Epoch);
        writer.Int64(message.Revision);
        writer.Utf8UInt16(
            message.ProfileId,
            BrowserWireLimits.MaxProfileIdBytes,
            "browser network state profile ID");
        writer.UInt8(message.VpnEnabled ? 1 : 0);
        writer.UInt8((int)message.Provider);
        writer.UInt8(message.AutoConnectOnBrowserStart ? 1 : 0);
        writer.UInt8(message.RequireVpnBeforeBrowse ? 1 : 0);
        writer.UInt8(message.ConfigPresent ? 1 : 0);
        writer.UInt8(message.CapabilityPreparable ? 1 : 0);
        writer.Utf8UInt16(
            message.CapabilityReason,
            BrowserWireLimits.MaxDetailBytes,
            "browser network capability reason");
        writer.UInt8((int)message.SessionState);
        writer.Utf8UInt16(
            message.SessionDetail,
            BrowserWireLimits.MaxDetailBytes,
            "browser network session detail");
    }

    private static BrowserNetworkStateMessage DecodeNetworkState(ref WireCodec.PayloadReader reader)
    {
        var message = new BrowserNetworkStateMessage(
            reader.Int64("browser network state epoch"),
            reader.Int64("browser network state revision"),
            reader.Utf8UInt16(BrowserWireLimits.MaxProfileIdBytes, "browser network state profile ID"),
            reader.Boolean("browser network state vpn enabled"),
            ReadEnum<BrowserVpnProvider>(ref reader, "browser network state provider"),
            reader.Boolean("browser network state auto-connect"),
            reader.Boolean("browser network state require VPN before browse"),
            reader.Boolean("browser network state config present"),
            reader.Boolean("browser network state capability preparable"),
            reader.Utf8UInt16(BrowserWireLimits.MaxDetailBytes, "browser network capability reason"),
            ReadEnum<BrowserVpnSessionState>(ref reader, "browser network session state"),
            reader.Utf8UInt16(BrowserWireLimits.MaxDetailBytes, "browser network session detail"));
        Validate(message);
        return message;
    }

    private static void Validate(BrowserNetworkCommandMessage message)
    {
        RequirePositive(message.Epoch, "browser network command epoch");
        RequirePositive(message.CommandId, "browser network command ID");
        RequireEnum(message.Action, "browser network command action");
        RequireNotNull(message.ProfileId, "Browser network command profile ID is null.");
        RequireNotNull(message.ConfigText, "Browser network command config is null.");
        RequireTextLength(
            message.ProfileId,
            BrowserWireLimits.MaxProfileIdBytes,
            "Browser network command profile ID");
        RequireTextLength(
            message.ConfigText,
            BrowserWireLimits.MaxConfigUtf8Bytes,
            "Browser network command config");
        RequireEnum(message.Provider, "browser network command provider");

        switch (message.Action)
        {
            case BrowserNetworkAction.Set:
                ValidateProfileId(message.ProfileId);
                if (message.VpnEnabled)
                {
                    Require(
                        message.Provider == BrowserVpnProvider.WireGuard,
                        "Enabled VPN requires the WireGuard provider.");
                }

                if (message.ConfigText.Length > 0)
                {
                    Require(
                        WireGuardConfigValidator.IsValid(message.ConfigText),
                        "WireGuard config is incomplete.");
                }

                break;

            case BrowserNetworkAction.Clear:
                ValidateProfileId(message.ProfileId);
                RequireNetworkCommandFieldsEmpty(message);
                break;

            case BrowserNetworkAction.RequestSnapshot:
                if (message.ProfileId.Length > 0)
                {
                    ValidateProfileId(message.ProfileId);
                }

                RequireNetworkCommandFieldsEmpty(message);
                break;
        }
    }

    private static void RequireNetworkCommandFieldsEmpty(BrowserNetworkCommandMessage message)
    {
        Require(!message.VpnEnabled, "Browser network command carries vpnEnabled.");
        Require(
            message.Provider == BrowserVpnProvider.None,
            "Browser network command carries a provider.");
        Require(!message.AutoConnectOnBrowserStart, "Browser network command carries auto-connect.");
        Require(!message.RequireVpnBeforeBrowse, "Browser network command carries require VPN before browse.");
        Require(message.ConfigText.Length == 0, "Browser network command carries config text.");
    }

    private static void Validate(BrowserNetworkStateMessage message)
    {
        RequirePositive(message.Epoch, "browser network state epoch");
        RequirePositive(message.Revision, "browser network state revision");
        RequireNotNull(message.ProfileId, "Browser network state profile ID is null.");
        RequireNotNull(message.CapabilityReason, "Browser network capability reason is null.");
        RequireNotNull(message.SessionDetail, "Browser network session detail is null.");
        if (message.ProfileId.Length > 0)
        {
            ValidateProfileId(message.ProfileId);
        }

        RequireEnum(message.Provider, "browser network state provider");
        RequireEnum(message.SessionState, "browser network session state");
        RequireTextLength(
            message.CapabilityReason,
            BrowserWireLimits.MaxDetailBytes,
            "Browser network capability reason");
        RequireTextLength(
            message.SessionDetail,
            BrowserWireLimits.MaxDetailBytes,
            "Browser network session detail");
    }

    private static void ValidateProfileId(string profileId)
    {
        RequireNotNull(profileId, "Browser profile ID is null.");
        Require(profileId.Length > 0, "Browser profile ID is empty.");
        RequireTextLength(profileId, BrowserWireLimits.MaxProfileIdBytes, "Browser profile ID");
        Require(
            profileId.All(character =>
                character is >= 'A' and <= 'Z'
                    or >= 'a' and <= 'z'
                    or >= '0' and <= '9'
                    or '_' or '-'),
            "Browser profile ID contains unsafe characters.");
    }

    private static void ValidateProfileName(string name)
    {
        RequireNotNull(name, "Browser profile name is null.");
        Require(name.Length > 0 && !string.IsNullOrWhiteSpace(name), "Browser profile name is blank.");
        Require(name == name.Trim(), "Browser profile name is not trimmed.");
        ValidatePrintable(name, BrowserWireLimits.MaxProfileNameBytes, "Browser profile name");
    }

    private static void ValidatePrintable(string value, int maximumBytes, string field)
    {
        RequireNotNull(value, $"{field} is null.");
        RequireTextLength(value, maximumBytes, field);
        Require(!value.Any(char.IsControl), $"{field} contains control characters.");
    }

    private static int ViewTextLimit(BrowserViewAction action) =>
        action == BrowserViewAction.FindStart
            ? BrowserWireLimits.MaxFindTextBytes
            : BrowserWireLimits.MaxUrlBytes;

    private static void RequireIntEnum<TEnum>(int value, string field)
        where TEnum : struct, Enum
    {
        Require(value is >= byte.MinValue and <= byte.MaxValue && Enum.IsDefined(typeof(TEnum), value),
            $"Unknown {field}: {value}.");
    }
}
