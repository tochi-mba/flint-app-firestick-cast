package com.rextechnologies.flint.receiver.ui

import androidx.activity.ComponentActivity
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsFocused
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performKeyInput
import androidx.compose.ui.test.pressKey
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.rextechnologies.flint.receiver.ReceiverNetworkState
import com.rextechnologies.flint.receiver.ReceiverUiState
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.Config
import kotlin.test.assertEquals

@RunWith(AndroidJUnit4::class)
@Config(qualifiers = "w960dp-h540dp-television-xhdpi")
class ReceiverIdleBrowserEntryTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    @Test
    fun `standalone browsing remains available in connection details`() {
        var opens = 0
        compose.setContent {
            ReceiverTheme {
                ReceiverSurface(
                    state = readyState(),
                    onBrowse = { opens += 1 },
                )
            }
        }

        compose.waitForIdle()
        compose.onNodeWithTag("receiver-connection-details")
            .assertIsDisplayed()
            .assertIsFocused()
            .performKeyInput { pressKey(Key.DirectionCenter) }
        compose.onNodeWithTag(ReceiverTags.BROWSER_ENTRY)
            .assertIsDisplayed()
        compose.onNodeWithText("Close").performKeyInput {
            pressKey(Key.DirectionRight)
            pressKey(Key.DirectionCenter)
        }

        compose.runOnIdle { assertEquals(1, opens) }
    }

    @Test
    fun `the waiting screen hides standalone browsing until details are opened`() {
        compose.setContent {
            ReceiverTheme {
                ReceiverSurface(state = readyState().copy(peerName = "Living room laptop"))
            }
        }

        compose.waitForIdle()

        compose.onNodeWithTag(ReceiverTags.BROWSER_ENTRY).assertDoesNotExist()
        compose.onNodeWithTag("receiver-connection-details").assertIsDisplayed()
        compose.onNodeWithTag(ReceiverTags.PRIMARY_ACTION).assertIsDisplayed().assertIsFocused()
    }

    @Test
    fun `a connected TV can disconnect its controller and request a new code`() {
        var refreshes = 0
        compose.setContent {
            ReceiverTheme {
                ReceiverSurface(
                    state = readyState().copy(peerName = "Living room laptop"),
                    onRefreshCode = { refreshes += 1 },
                )
            }
        }

        compose.waitForIdle()
        // Disconnecting is on the screen itself, focused, one press away: it is the one thing a
        // person needs while another device holds the TV.
        compose.onNodeWithText("Disconnect")
            .assertIsDisplayed()
            .assertIsFocused()
            .performKeyInput { pressKey(Key.DirectionCenter) }
        compose.runOnIdle { assertEquals(1, refreshes) }

        // The details sheet offers a fresh code only for a TV nobody holds.
        compose.onNodeWithTag("receiver-connection-details").performClick()
        compose.onNodeWithText("New code").assertDoesNotExist()
    }

    @Test
    fun `connection details are optional and closing returns focus to their entry`() {
        compose.setContent {
            ReceiverTheme {
                ReceiverSurface(state = readyState().copy(browserPort = 47856, browserFingerprint = "ABCD 1234"))
            }
        }
        compose.onNodeWithTag(ReceiverTags.PAIRING_CODE).assertIsDisplayed()
        compose.onNodeWithTag(ReceiverTags.ENDPOINT).assertDoesNotExist()
        compose.onNodeWithTag("receiver-security-code").assertDoesNotExist()
        compose.onNodeWithTag("receiver-connection-details")
            .assertIsFocused()
            .performKeyInput { pressKey(Key.DirectionCenter) }
        compose.onNodeWithTag(ReceiverTags.ENDPOINT).assertIsDisplayed()
        compose.onNodeWithTag("receiver-security-code").assertIsDisplayed()
        compose.onNodeWithText("Close").assertIsFocused()
            .performKeyInput { pressKey(Key.DirectionCenter) }
        compose.onNodeWithTag(ReceiverTags.ENDPOINT).assertDoesNotExist()
        compose.onNodeWithTag("receiver-connection-details").assertIsFocused()
    }

    private fun readyState() = ReceiverUiState(
        networkState = ReceiverNetworkState.LISTENING,
        pairingCode = "123456",
        address = "10.0.0.8",
        port = 47_855,
    )
}
