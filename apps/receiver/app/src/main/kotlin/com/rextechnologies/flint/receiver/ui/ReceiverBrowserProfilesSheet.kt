package com.rextechnologies.flint.receiver.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.runtime.withFrameNanos
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextOverflow
import androidx.tv.material3.Text
import com.rextechnologies.flint.receiver.browser.BrowserLibraryProfile
import com.rextechnologies.flint.receiver.browser.BrowserLibraryStore
import com.rextechnologies.flint.receiver.browser.BrowserProfileSource
import com.rextechnologies.flint.receiver.browser.BrowserProfilesUiState

/**
 * Chooses who owns this browsing session without blurring the privacy boundary.
 *
 * TV rows are durable and say so. The authenticated device row is visually separate and says its
 * data is not copied here. Every operation is a normal focus target; no long press or touch gesture
 * is required to discover or manage a profile.
 */
@Composable
internal fun ReceiverBrowserProfilesSheet(
    profiles: BrowserProfilesUiState,
    onSelectTv: (BrowserLibraryProfile) -> Unit,
    onSelectDevice: () -> Unit,
    onCreate: () -> Unit,
    onRename: (BrowserLibraryProfile) -> Unit,
    onDelete: (BrowserLibraryProfile) -> Unit,
) {
    val selectedFocus = remember { FocusRequester() }
    val inputMode = androidx.compose.ui.platform.LocalInputModeManager.current
    LaunchedEffect(profiles.activeSource, profiles.activeTvProfileId, profiles.deviceAvailable) {
        inputMode.requestInputMode(androidx.compose.ui.input.InputMode.Keyboard)
        withFrameNanos { }
        runCatching { selectedFocus.requestFocus() }
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(ReceiverColors.Scrim)
            .testTag(ReceiverTags.BROWSER_PROFILES),
        contentAlignment = Alignment.Center,
    ) {
        Column(
            modifier = Modifier
                .fillMaxSize(ReceiverOverscan.CONTENT_FRACTION)
                .background(ReceiverColors.Panel, ReceiverShapes.Large)
                .border(ReceiverSpace.Hairline, ReceiverColors.Line, ReceiverShapes.Large)
                .padding(ReceiverSpace.Large),
            verticalArrangement = Arrangement.spacedBy(ReceiverSpace.Medium),
        ) {
            Text(text = "WHO'S BROWSING?", style = ReceiverType.Label, color = ReceiverColors.Signal)
            Text(
                text = "TV profiles keep bookmarks and history separate on this television; TV site sign-ins " +
                    "remain shared. A connected-device profile stays on that device and is forgotten here when " +
                    "it disconnects.",
                style = ReceiverType.Caption,
                color = ReceiverColors.Muted,
            )

            LazyColumn(
                modifier = Modifier.weight(1f),
                verticalArrangement = Arrangement.spacedBy(ReceiverSpace.Small),
            ) {
                item {
                    Text(text = "SAVED ON THIS TV", style = ReceiverType.Label, color = ReceiverColors.Muted)
                }
                items(profiles.tvProfiles, key = BrowserLibraryProfile::id) { profile ->
                    val selected = profiles.activeSource == BrowserProfileSource.TV &&
                        profiles.activeTvProfileId == profile.id
                    TvProfileRow(
                        profile = profile,
                        selected = selected,
                        canDelete = profiles.tvProfiles.size > 1,
                        onSelect = { onSelectTv(profile) },
                        onRename = { onRename(profile) },
                        onDelete = { onDelete(profile) },
                        modifier = if (selected) Modifier.focusRequester(selectedFocus) else Modifier,
                    )
                }

                item {
                    Text(
                        text = "USE A CONNECTED DEVICE",
                        style = ReceiverType.Label,
                        color = ReceiverColors.Muted,
                        modifier = Modifier.padding(top = ReceiverSpace.Small),
                    )
                }
                item {
                    val deviceSelected = profiles.activeSource == BrowserProfileSource.CONNECTED_DEVICE
                    if (profiles.deviceAvailable) {
                        ConnectedDeviceProfileRow(
                            name = profiles.deviceName ?: "Connected device",
                            selected = deviceSelected,
                            switching = profiles.switchingToDevice,
                            onSelect = onSelectDevice,
                            modifier = if (deviceSelected) {
                                Modifier.focusRequester(selectedFocus)
                            } else {
                                Modifier
                            },
                        )
                    } else {
                        UnavailableDeviceProfileRow()
                    }
                }
            }

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.End,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    text = "${profiles.tvProfiles.size} of ${BrowserLibraryStore.MAX_PROFILES} TV profiles",
                    style = ReceiverType.Caption,
                    color = ReceiverColors.Muted,
                )
                Spacer(Modifier.weight(1f))
                TvActionButton(
                    label = "NEW TV PROFILE",
                    enabled = profiles.tvProfiles.size < BrowserLibraryStore.MAX_PROFILES,
                    onClick = onCreate,
                )
            }
        }
    }
}

@Composable
private fun TvProfileRow(
    profile: BrowserLibraryProfile,
    selected: Boolean,
    canDelete: Boolean,
    onSelect: () -> Unit,
    onRename: () -> Unit,
    onDelete: () -> Unit,
    modifier: Modifier,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .background(ReceiverColors.Raised, ReceiverShapes.Medium)
            .padding(ReceiverSpace.Small),
        horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Small),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(
            modifier = modifier
                .weight(1f)
                .background(
                    if (selected) ReceiverColors.Signal.copy(alpha = 0.12f) else ReceiverColors.Raised,
                    ReceiverShapes.Small,
                )
                .tvClickable(shape = ReceiverShapes.Small, onClick = onSelect)
                .testTag("browser-profile-${profile.id}")
                .semantics {
                    this.selected = selected
                    contentDescription = buildString {
                        append("Use TV profile ${profile.name}. ")
                        if (selected) append("Selected. ")
                        append("Bookmarks and history are saved on this TV.")
                    }
                }
                .padding(horizontal = ReceiverSpace.Medium, vertical = ReceiverSpace.Compact),
        ) {
            Text(
                text = profile.name,
                style = ReceiverType.BodyStrong,
                color = ReceiverColors.Text,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = if (selected) "CURRENT · SAVED ON THIS TV" else "Saved on this TV",
                style = ReceiverType.Caption,
                color = if (selected) ReceiverColors.Signal else ReceiverColors.Muted,
            )
        }
        TvActionButton(label = "RENAME", enabled = true, onClick = onRename)
        TvActionButton(label = "DELETE", enabled = canDelete, onClick = onDelete)
    }
}

@Composable
private fun ConnectedDeviceProfileRow(
    name: String,
    selected: Boolean,
    switching: Boolean,
    onSelect: () -> Unit,
    modifier: Modifier,
) {
    Column(
        modifier = modifier
            .fillMaxWidth()
            .background(ReceiverColors.Raised, ReceiverShapes.Medium)
            .tvClickable(enabled = !switching, shape = ReceiverShapes.Medium, onClick = onSelect)
            .testTag(ReceiverTags.BROWSER_PROFILE_DEVICE)
            .semantics {
                this.selected = selected
                contentDescription = buildString {
                    append("Use profile from $name. ")
                    if (selected) append("Selected. ")
                    append("Its bookmarks and history are not copied to this TV.")
                }
            }
            .padding(horizontal = ReceiverSpace.Medium, vertical = ReceiverSpace.Compact),
    ) {
        Text(
            text = if (switching) "CONNECTING TO $name…" else name,
            style = ReceiverType.BodyStrong,
            color = ReceiverColors.Text,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        Text(
            text = if (selected) {
                "CURRENT · DATA STAYS ON THIS DEVICE"
            } else {
                "Temporary on this TV · data stays on this device"
            },
            style = ReceiverType.Caption,
            color = if (selected) ReceiverColors.Signal else ReceiverColors.Muted,
        )
    }
}

@Composable
private fun UnavailableDeviceProfileRow() {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .background(ReceiverColors.Ink.copy(alpha = 0.45f), ReceiverShapes.Medium)
            .border(ReceiverSpace.Hairline, ReceiverColors.Line, ReceiverShapes.Medium)
            .padding(horizontal = ReceiverSpace.Medium, vertical = ReceiverSpace.Compact)
            .semantics {
                contentDescription = "No verified device profile is connected"
            },
    ) {
        Text(text = "NO VERIFIED DEVICE CONNECTED", style = ReceiverType.BodyStrong, color = ReceiverColors.Muted)
        Text(
            text = "Connect Flint securely to use a device profile without saving it on this TV.",
            style = ReceiverType.Caption,
            color = ReceiverColors.Muted,
        )
    }
}

@Composable
internal fun ReceiverBrowserProfileDeletePrompt(
    profile: BrowserLibraryProfile,
    onCancel: () -> Unit,
    onDelete: () -> Unit,
) {
    val cancelFocus = remember { FocusRequester() }
    LaunchedEffect(profile.id) {
        withFrameNanos { }
        runCatching { cancelFocus.requestFocus() }
    }
    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(ReceiverColors.Scrim)
            .testTag(ReceiverTags.BROWSER_PROFILE_DELETE),
        contentAlignment = Alignment.Center,
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth(0.62f)
                .background(ReceiverColors.Raised, ReceiverShapes.Large)
                .padding(ReceiverSpace.XLarge),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(ReceiverSpace.Medium),
        ) {
            Text(text = "Delete ${profile.name} from this TV?", style = ReceiverType.Title, color = ReceiverColors.Text)
            Text(
                text = "This permanently removes only this profile's TV bookmarks and history. " +
                    "Other TV profiles and connected devices are not changed.",
                style = ReceiverType.Body,
                color = ReceiverColors.Muted,
            )
            Row(horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Compact)) {
                TvActionButton("Keep profile", true, onCancel, Modifier.focusRequester(cancelFocus))
                TvActionButton("Delete profile", true, onDelete)
            }
        }
    }
}
