package com.rextechnologies.flint.mobile.ui

import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.test.SemanticsMatcher
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertIsSelected
import androidx.compose.ui.test.isSelected
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.performClick
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.rextechnologies.flint.castcore.copy.MobileTab
import com.rextechnologies.flint.design.FlintTheme
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode
import kotlin.test.assertEquals

/**
 * What a screen reader is told about the bottom bar.
 *
 * The colour that says which tab is active is invisible to TalkBack. `Role.Tab` and `selected` are
 * the only things that carry it, and neither fails to compile when forgotten.
 */
@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(qualifiers = "w411dp-h891dp-xxhdpi")
class BottomBarTest {
    @get:Rule
    val compose = createComposeRule()

    private val tabs = SemanticsMatcher.expectValue(SemanticsProperties.Role, Role.Tab)

    @Test
    fun `every tab is a tab, exactly one is selected, and pressing one selects it`() {
        var selected = MobileTab.CAST
        compose.setContent {
            FlintTheme { BottomBar(selected = selected, onSelect = { selected = it }) }
        }

        compose.onAllNodes(tabs).assertCountEquals(MobileTab.entries.size)
        compose.onAllNodes(tabs.and(isSelected())).assertCountEquals(1)
        compose.onNodeWithContentDescription("Cast").assertIsSelected()

        compose.onNodeWithContentDescription("Settings").performClick()
        assertEquals(MobileTab.SETTINGS, selected)
    }
}
