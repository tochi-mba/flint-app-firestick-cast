package com.rextechnologies.flint.receiver.ui

import androidx.activity.ComponentActivity
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsFocused
import androidx.compose.ui.test.assertTextEquals
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performKeyInput
import androidx.compose.ui.test.pressKey
import androidx.compose.ui.test.requestFocus
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

        // The details sheet offers browsing but a fresh code only for a TV nobody holds, and closing
        // it hands focus back to its own entry: Disconnect is one press away, not the default.
        openDetails()
        compose.onNodeWithTag(ReceiverTags.BROWSER_ENTRY).assertIsDisplayed()
        compose.onNodeWithText("New code").assertDoesNotExist()
        closeDetails()
        compose.onNodeWithTag("receiver-connection-details").assertIsFocused()
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

    @Test
    fun `a starting receiver says only that it is getting ready`() {
        compose.setContent { ReceiverTheme { ReceiverSurface(state = ReceiverUiState()) } }

        compose.onNodeWithText("Getting ready").assertIsDisplayed()
        compose.onNodeWithText("Your connection code will appear in a moment.").assertIsDisplayed()
        compose.onNodeWithTag(ReceiverTags.PAIRING_PANEL).assertDoesNotExist()
        compose.onNodeWithTag(ReceiverTags.PRIMARY_ACTION).assertDoesNotExist()
    }

    @Test
    fun `a receiver with no network asks for one, and its details have nothing to offer`() {
        compose.setContent {
            ReceiverTheme { ReceiverSurface(state = ReceiverUiState(networkState = ReceiverNetworkState.WAITING)) }
        }

        compose.onNodeWithText("Connect to your network").assertIsDisplayed()
        compose.onNodeWithTag(ReceiverTags.PAIRING_PANEL).assertDoesNotExist()
        openDetails()
        compose.onNodeWithTag(ReceiverTags.ENDPOINT)
            .assertIsDisplayed()
            .assertTextEquals("TV address: Not connected to a network")
        compose.onNodeWithTag(ReceiverTags.BROWSER_ENTRY).assertDoesNotExist()
        compose.onNodeWithText("New code").assertDoesNotExist()

        closeDetails()
        compose.onNodeWithTag(ReceiverTags.ENDPOINT).assertDoesNotExist()
        compose.onNodeWithTag("receiver-connection-details").assertIsFocused()
    }

    @Test
    fun `a receiver in trouble says what happened and offers one retry, focused`() {
        var retries = 0
        compose.setContent {
            ReceiverTheme {
                ReceiverSurface(
                    state = readyState().copy(error = "The port is taken."),
                    onRetry = { retries += 1 },
                )
            }
        }

        compose.waitForIdle()
        compose.onNodeWithText("Let's reconnect").assertIsDisplayed()
        compose.onNodeWithText("The port is taken.").assertIsDisplayed()
        compose.onNodeWithTag(ReceiverTags.PAIRING_PANEL).assertDoesNotExist()
        compose.onNodeWithText("Try again")
            .assertIsFocused()
            .performKeyInput { pressKey(Key.DirectionCenter) }
        compose.runOnIdle { assertEquals(1, retries) }

        // Not ready, so the details sheet has neither browsing nor a fresh code, and closing it
        // hands focus back to its own entry rather than to the retry.
        openDetails()
        compose.onNodeWithTag(ReceiverTags.ENDPOINT).assertTextEquals("TV address: 10.0.0.8:47855")
        compose.onNodeWithTag(ReceiverTags.BROWSER_ENTRY).assertDoesNotExist()
        compose.onNodeWithText("New code").assertDoesNotExist()
        closeDetails()
        compose.onNodeWithTag("receiver-connection-details").assertIsFocused()
    }

    /** What a remote does: move focus onto the details entry and press OK. The sheet takes focus. */
    private fun openDetails() {
        compose.onNodeWithTag("receiver-connection-details")
            .requestFocus()
            .performKeyInput { pressKey(Key.DirectionCenter) }
        compose.onNodeWithText("Close").assertIsDisplayed().assertIsFocused()
    }

    private fun closeDetails() {
        compose.onNodeWithText("Close").assertIsFocused().performKeyInput { pressKey(Key.DirectionCenter) }
        compose.onNodeWithText("Close").assertDoesNotExist()
    }

    private fun readyState() = ReceiverUiState(
        networkState = ReceiverNetworkState.LISTENING,
        pairingCode = "123456",
        address = "10.0.0.8",
        port = 47_855,
    )
}
