package com.rextechnologies.flint.receiver.ui

import androidx.activity.ComponentActivity
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsFocused
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithTag
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
    fun `a ready TV focuses and opens standalone browsing without a host`() {
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
        compose.onNodeWithTag(ReceiverTags.BROWSER_ENTRY)
            .assertIsDisplayed()
            .assertIsFocused()
            .performKeyInput { pressKey(Key.DirectionCenter) }

        compose.runOnIdle { assertEquals(1, opens) }
    }

    @Test
    fun `standalone browsing remains available while a PC is connected`() {
        compose.setContent {
            ReceiverTheme {
                ReceiverSurface(state = readyState().copy(peerName = "Living room laptop"))
            }
        }

        compose.waitForIdle()

        compose.onNodeWithTag(ReceiverTags.BROWSER_ENTRY).assertIsDisplayed().assertIsFocused()
    }

    private fun readyState() = ReceiverUiState(
        networkState = ReceiverNetworkState.LISTENING,
        pairingCode = "123456",
        address = "10.0.0.8",
        port = 47_855,
    )
}
