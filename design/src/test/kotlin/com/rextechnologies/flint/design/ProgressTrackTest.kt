package com.rextechnologies.flint.design

import androidx.compose.foundation.layout.width
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.ProgressBarRangeInfo
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.test.SemanticsMatcher
import androidx.compose.ui.test.assert
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.performTouchInput
import androidx.compose.ui.unit.dp
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/** The track: what it tells a screen reader, and where a press sends the person. */
@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(qualifiers = "w411dp-h891dp-xxhdpi")
class ProgressTrackTest {
    @get:Rule
    val compose = createComposeRule()

    @Test
    fun `the track reports its progress as a range, and an unknown end as empty`() {
        compose.setContent {
            FlintTheme {
                ProgressTrack(fraction = 0.25f, modifier = Modifier.testTag("known"))
                ProgressTrack(fraction = null, modifier = Modifier.testTag("unknown"))
            }
        }
        compose.onNodeWithTag("known").assert(
            SemanticsMatcher.expectValue(SemanticsProperties.ProgressBarRangeInfo, ProgressBarRangeInfo(0.25f, 0f..1f)),
        )
        compose.onNodeWithTag("unknown").assert(
            SemanticsMatcher.expectValue(SemanticsProperties.ProgressBarRangeInfo, ProgressBarRangeInfo(0f, 0f..1f)),
        )
    }

    @Test
    fun `a tap seeks to the fraction of the width it landed on, once`() {
        val seeks = mutableListOf<Float>()
        compose.setContent {
            FlintTheme {
                ProgressTrack(
                    fraction = 0.1f,
                    modifier = Modifier.width(200.dp).testTag("track"),
                    onSeek = { seeks += it },
                )
            }
        }
        val node = compose.onNodeWithTag("track")
        node.performTouchInput { click(Offset(width * 0.75f, centerY)) }
        assertEquals(1, seeks.size)
        assertTrue(seeks.single() in 0.7f..0.8f, "${seeks.single()}")
    }
}
