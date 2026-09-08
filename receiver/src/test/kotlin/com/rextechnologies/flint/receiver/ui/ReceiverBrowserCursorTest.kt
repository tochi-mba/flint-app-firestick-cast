package com.rextechnologies.flint.receiver.ui

import androidx.activity.ComponentActivity
import androidx.compose.foundation.layout.Box
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithContentDescription
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.tv.material3.Text
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.rextechnologies.flint.receiver.browser.CursorState
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.Config

@RunWith(AndroidJUnit4::class)
@Config(qualifiers = "w960dp-h540dp-television-xhdpi")
class ReceiverBrowserCursorTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    @Test
    fun `decorative cursor does not hide page accessibility semantics`() {
        compose.setContent {
            ReceiverTheme {
                Box {
                    Text(
                        text = "Page",
                        modifier = Modifier.semantics {
                            contentDescription = "Page content"
                        },
                    )
                    ReceiverBrowserCursor(
                        cursor = CursorState(x = 100, y = 100),
                        pressed = false,
                        visible = true,
                    )
                }
            }
        }

        compose.onNodeWithContentDescription("Page content").assertIsDisplayed()
        compose.onAllNodesWithContentDescription("Pointer").assertCountEquals(0)
    }
}
