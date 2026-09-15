package com.rextechnologies.flint.mobile.ui

import android.content.Context
import androidx.compose.foundation.layout.Column
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.v2.createComposeRule
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.rextechnologies.flint.castcore.copy.FailureCopy
import com.rextechnologies.flint.design.FlintTheme
import com.rextechnologies.flint.mobile.platform.CrashLog
import org.junit.After
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode
import kotlin.test.assertNull

@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(qualifiers = "w411dp-h891dp-xxhdpi")
class LastFailureCardTest {
    @get:Rule
    val compose = createComposeRule()

    private val context: Context = ApplicationProvider.getApplicationContext()

    @Before
    @After
    fun clearReport() {
        CrashLog.clear(context)
    }

    @Test
    fun `a failure recorded while settings is open appears and can be cleared`() {
        compose.setContent {
            FlintTheme { Column { LastFailureCard(context) } }
        }
        compose.onNodeWithText(FailureCopy.TITLE).assertDoesNotExist()

        compose.runOnIdle {
            CrashLog.record(context, IllegalStateException().apply { stackTrace = emptyArray() }, "a Flint action")
        }

        compose.onNodeWithText(FailureCopy.TITLE).assertIsDisplayed()
        compose.onNodeWithText("java.lang.IllegalStateException", substring = true).assertIsDisplayed()
        compose.onNodeWithText(FailureCopy.COPY_ACTION, ignoreCase = true).performClick()
        compose.onNodeWithText(FailureCopy.COPIED, ignoreCase = true).assertExists()

        compose.runOnIdle {
            CrashLog.record(context, IllegalArgumentException().apply { stackTrace = emptyArray() }, "a Flint action")
        }
        compose.onNodeWithText("java.lang.IllegalArgumentException", substring = true).assertIsDisplayed()
        compose.onNodeWithText(FailureCopy.COPY_ACTION, ignoreCase = true).assertExists()
        compose.onNodeWithText(FailureCopy.CLEAR_ACTION, ignoreCase = true).performClick()
        compose.onNodeWithText(FailureCopy.TITLE).assertDoesNotExist()
        compose.runOnIdle { assertNull(CrashLog.last(context)) }
    }
}
