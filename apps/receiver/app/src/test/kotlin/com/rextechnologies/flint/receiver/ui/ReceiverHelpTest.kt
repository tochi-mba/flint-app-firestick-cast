package com.rextechnologies.flint.receiver.ui

import androidx.activity.ComponentActivity
import androidx.compose.foundation.layout.Column
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.tv.material3.Button
import androidx.tv.material3.Text
import com.rextechnologies.flint.receiver.browser.BrowserHelpTopics
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.junit.Before
import org.robolectric.annotation.Config
import org.robolectric.shadows.ShadowDialog
import android.view.KeyEvent

@RunWith(AndroidJUnit4::class)
@Config(qualifiers = "w960dp-h540dp-television-xhdpi")
class ReceiverHelpTest {
    @get:Rule val compose = createAndroidComposeRule<ComponentActivity>()

    @Before fun clearHelpPreferences() {
        compose.activity.getSharedPreferences("flint-help", 0).edit().clear().commit()
    }

    private fun show() {
        compose.setContent {
            ReceiverTheme {
                val existingFocus = remember { FocusRequester() }
                LaunchedEffect(Unit) { existingFocus.requestFocus() }
                Column {
                    Button(onClick = {}, modifier = Modifier.focusRequester(existingFocus)) { Text("Existing control") }
                    ReceiverHelp()
                }
            }
        }
        compose.waitForIdle()
    }

    /**
     * Presses Next until the last topic is showing.
     *
     * Driven by the topic list rather than a hard-coded count: adding a help topic used to break
     * this test silently, because the final button stays "Next" and "Got it" simply never appears.
     */
    private fun advanceToLastTopic() {
        repeat(BrowserHelpTopics.entries.lastIndex) { activate("Next") }
    }

    private fun activate(text: String) {
        compose.onNodeWithText(text).performSemanticsAction(SemanticsActions.RequestFocus)
            .performKeyInput { pressKey(Key.DirectionCenter) }
        compose.waitForIdle()
    }

    @Test fun `initial help invitation does not steal focus or open a modal`() {
        show()
        compose.onNodeWithText("Existing control").assertIsFocused()
        compose.onNodeWithText("Start here").assertDoesNotExist()
        compose.onNodeWithText("New here? Help").assertIsDisplayed()
    }

    @Test fun `remote opens help with close focused and can return to the page`() {
        show()
        activate("New here? Help")
        compose.onNodeWithText("Start here").assertIsDisplayed()
        compose.onNodeWithText("Close").assertIsFocused()
        activate("Close")
        compose.onNodeWithText("Start here").assertDoesNotExist()
        compose.onNodeWithText("Existing control").assertIsDisplayed()
        compose.onNodeWithText("New here? Help").assertIsFocused()
    }

    @Test fun `completion survives help surface recreation without reopening`() {
        val visible = mutableStateOf(true)
        compose.setContent { ReceiverTheme { if (visible.value) ReceiverHelp() } }
        activate("New here? Help")
        advanceToLastTopic()
        activate("Got it")
        compose.runOnIdle { visible.value = false }
        compose.waitForIdle()
        compose.runOnIdle { visible.value = true }
        compose.waitForIdle()
        compose.onNodeWithText("Help").assertIsDisplayed()
        compose.onNodeWithText("Start here").assertDoesNotExist()
        activate("Help")
        compose.onNodeWithText("Start here").assertIsDisplayed()
    }

    @Test fun `remote back closes without marking tips dismissed`() {
        show()
        activate("New here? Help")
        // Back belongs to the Android dialog window, not Compose's focused-node key pipeline.
        compose.runOnIdle {
            val dialog = checkNotNull(ShadowDialog.getLatestDialog())
            dialog.dispatchKeyEvent(KeyEvent(KeyEvent.ACTION_DOWN, KeyEvent.KEYCODE_BACK))
            dialog.dispatchKeyEvent(KeyEvent(KeyEvent.ACTION_UP, KeyEvent.KEYCODE_BACK))
        }
        compose.waitForIdle()
        compose.onNodeWithText("Start here").assertDoesNotExist()
        compose.onNodeWithText("New here? Help").assertIsFocused()
    }

    @Test fun `dismissed tips stay replayable from the remote`() {
        show()
        activate("New here? Help")
        activate("Dismiss tips")
        compose.onNodeWithText("Start here").assertDoesNotExist()
        activate("Help")
        compose.onNodeWithText("Start here").assertIsDisplayed()
    }
}
