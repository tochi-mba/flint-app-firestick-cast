package com.rextechnologies.flint.receiver.ui

import androidx.activity.ComponentActivity
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsFocused
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performKeyInput
import androidx.compose.ui.test.performSemanticsAction
import androidx.compose.ui.test.pressKey
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.rextechnologies.flint.receiver.browser.BrowserDialogAnswer
import com.rextechnologies.flint.receiver.browser.BrowserDialogKind
import com.rextechnologies.flint.receiver.browser.PendingJsDialog
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceDialogs
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.Config
import kotlin.test.assertEquals

@RunWith(AndroidJUnit4::class)
@Config(qualifiers = "w960dp-h540dp-television-xhdpi")
class ReceiverWorkspaceDialogTest {
    @get:Rule val compose = createAndroidComposeRule<ComponentActivity>()
    private val answers = mutableListOf<BrowserDialogAnswer>()

    private fun show(kind: BrowserDialogKind, defaultValue: String? = null) {
        val request = BrowserWorkspaceDialogs.Request(
            1,
            2,
            7,
            PendingJsDialog(kind, "https://example.com/private/path", "Continue?", defaultValue, {}),
        )
        compose.setContent {
            ReceiverTheme { ReceiverWorkspaceDialog(request, answers::add) }
        }
        compose.waitForIdle()
    }

    @Test fun `confirmation starts on cancel and shows origin without private path`() {
        show(BrowserDialogKind.CONFIRM)
        compose.onNodeWithText("Page 2 · example.com").assertIsDisplayed()
        compose.onNodeWithText("Cancel").assertIsFocused().performKeyInput { pressKey(Key.DirectionCenter) }
        compose.runOnIdle { assertEquals(listOf<BrowserDialogAnswer>(BrowserDialogAnswer.Cancel), answers) }
    }

    @Test fun `prompt can confirm its default value`() {
        show(BrowserDialogKind.PROMPT, "Family")
        compose.onNodeWithText("Family").assertIsDisplayed()
        compose.onNodeWithText("Edit response").assertIsDisplayed()
        compose.onNodeWithText("OK").performSemanticsAction(SemanticsActions.RequestFocus)
            .performKeyInput { pressKey(Key.DirectionCenter) }
        compose.runOnIdle { assertEquals(listOf<BrowserDialogAnswer>(BrowserDialogAnswer.Prompt("Family")), answers) }
    }

    @Test fun `before unload requires explicit leave action`() {
        show(BrowserDialogKind.BEFORE_UNLOAD)
        compose.onNodeWithText("Cancel").assertIsFocused()
        compose.onNodeWithText("Leave page").performSemanticsAction(SemanticsActions.RequestFocus)
            .performKeyInput { pressKey(Key.DirectionCenter) }
        compose.runOnIdle { assertEquals(listOf<BrowserDialogAnswer>(BrowserDialogAnswer.Confirm), answers) }
    }
}
