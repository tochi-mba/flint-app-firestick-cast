package com.rextechnologies.flint.design

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.test.SemanticsMatcher
import androidx.compose.ui.test.assert
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.performClick
import androidx.compose.ui.unit.dp
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode
import kotlin.test.assertEquals

/** The one touch affordance in the system, and what it tells a screen reader. */
@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(qualifiers = "w411dp-h891dp-xxhdpi")
class InteractionTest {
    @get:Rule
    val compose = createComposeRule()

    @Test
    fun `a disabled control is announced as disabled and does not fire`() {
        var presses = 0
        compose.setContent {
            FlintTheme {
                Box(Modifier.size(48.dp).testTag("control").flintClickable(enabled = false) { presses++ })
            }
        }
        compose.onNodeWithTag("control").assertIsNotEnabled().performClick()
        assertEquals(0, presses)
    }

    @Test
    fun `an enabled control is a button unless told otherwise, and fires once per press`() {
        var presses = 0
        compose.setContent {
            FlintTheme {
                Box(Modifier.size(48.dp).testTag("control").flintClickable(onClickLabel = "Probe") { presses++ })
            }
        }
        compose.onNodeWithTag("control")
            .assertIsEnabled()
            .assert(SemanticsMatcher.expectValue(SemanticsProperties.Role, Role.Button))
            .performClick()
        assertEquals(1, presses)
    }

    @Test
    fun `the role can be a tab, and then it is not also a button`() {
        compose.setContent {
            FlintTheme {
                Box(Modifier.size(48.dp).testTag("tab").flintClickable(role = Role.Tab) { })
            }
        }
        compose.onNodeWithTag("tab").assert(SemanticsMatcher.expectValue(SemanticsProperties.Role, Role.Tab))
    }
}
