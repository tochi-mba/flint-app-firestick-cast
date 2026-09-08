package com.rextechnologies.flint.receiver.ui

import androidx.activity.ComponentActivity
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsFocused
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.rextechnologies.flint.receiver.browser.BrowserLibraryProfile
import com.rextechnologies.flint.receiver.browser.BrowserProfileSource
import com.rextechnologies.flint.receiver.browser.BrowserProfilesUiState
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.Config
import kotlin.test.assertEquals

@RunWith(AndroidJUnit4::class)
@Config(qualifiers = "w960dp-h540dp-television-xhdpi")
class ReceiverBrowserProfilesSheetTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    @Test
    fun `the selected TV profile receives initial dpad focus`() {
        var selected: String? = null
        compose.setContent {
            ReceiverTheme {
                ReceiverBrowserProfilesSheet(
                    profiles = profiles(),
                    onSelectTv = { selected = it.id },
                    onSelectDevice = {},
                    onCreate = {},
                    onRename = {},
                    onDelete = {},
                )
            }
        }

        compose.waitForIdle()
        compose.onNodeWithTag("browser-profile-family")
            .assertIsDisplayed()
            .assertIsFocused()
            .performClick()

        assertEquals("family", selected)
    }

    @Test
    fun `a connected device is clearly temporary and dpad selectable`() {
        var selected = false
        compose.setContent {
            ReceiverTheme {
                ReceiverBrowserProfilesSheet(
                    profiles = profiles().copy(
                        activeSource = BrowserProfileSource.CONNECTED_DEVICE,
                        deviceAvailable = true,
                        deviceName = "Living room laptop",
                    ),
                    onSelectTv = {},
                    onSelectDevice = { selected = true },
                    onCreate = {},
                    onRename = {},
                    onDelete = {},
                )
            }
        }

        compose.waitForIdle()
        compose.onNodeWithTag(ReceiverTags.BROWSER_PROFILE_DEVICE)
            .assertIsDisplayed()
            .assertIsFocused()
            .performClick()
        compose.onNodeWithText("CURRENT · DATA STAYS ON THIS DEVICE").assertIsDisplayed()

        assertEquals(true, selected)
    }

    @Test
    fun `the last durable TV profile cannot expose an enabled delete action`() {
        compose.setContent {
            ReceiverTheme {
                ReceiverBrowserProfilesSheet(
                    profiles = BrowserProfilesUiState(
                        tvProfiles = listOf(BrowserLibraryProfile("default", "Default")),
                    ),
                    onSelectTv = {},
                    onSelectDevice = {},
                    onCreate = {},
                    onRename = {},
                    onDelete = {},
                )
            }
        }

        compose.waitForIdle()

        compose.onNodeWithText("DELETE").assertIsNotEnabled()
        compose.onNodeWithText("NO VERIFIED DEVICE CONNECTED").assertIsDisplayed()
    }

    private fun profiles() = BrowserProfilesUiState(
        tvProfiles = listOf(
            BrowserLibraryProfile("default", "Default"),
            BrowserLibraryProfile("family", "Family"),
        ),
        activeTvProfileId = "family",
        activeSource = BrowserProfileSource.TV,
        deviceAvailable = true,
        deviceName = "Living room laptop",
    )
}
