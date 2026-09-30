package com.rextechnologies.flint.mobile.ui

import android.app.Application
import android.content.Context
import android.provider.Settings
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.test.SemanticsMatcher
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.junit4.v2.createComposeRule
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.rextechnologies.flint.castcore.capability.CapabilityReport
import com.rextechnologies.flint.castcore.capability.CastMode
import com.rextechnologies.flint.castcore.capability.LocalNetwork
import com.rextechnologies.flint.castcore.capability.ModeStatus
import com.rextechnologies.flint.castcore.capability.ModeVerdict
import com.rextechnologies.flint.castcore.capability.PhoneCapabilities
import com.rextechnologies.flint.castcore.capability.ReceiverDevice
import com.rextechnologies.flint.castcore.copy.CastCopy
import com.rextechnologies.flint.castcore.copy.MediaCopy
import com.rextechnologies.flint.castcore.copy.MobileTab
import com.rextechnologies.flint.castcore.copy.ScreenCopy
import com.rextechnologies.flint.castcore.copy.SettingsCopy
import com.rextechnologies.flint.castcore.discovery.DiscoveryRung
import com.rextechnologies.flint.castcore.media.SessionDiagnostics
import com.rextechnologies.flint.castcore.media.ThermalLevel
import com.rextechnologies.flint.castcore.screen.SecondScreenScene
import com.rextechnologies.flint.design.FlintText
import com.rextechnologies.flint.design.FlintTheme
import com.rextechnologies.flint.design.FlintType
import com.rextechnologies.flint.mobile.LiveOutput
import com.rextechnologies.flint.mobile.MobileActivity
import com.rextechnologies.flint.mobile.MobileController
import com.rextechnologies.flint.mobile.MobileUiState
import com.rextechnologies.flint.mobile.OutputMode
import com.rextechnologies.flint.mobile.state.LinkState
import com.rextechnologies.flint.mobile.state.LookupState
import com.rextechnologies.flint.protocol.media.LinkHealth
import com.rextechnologies.flint.protocol.session.CastSessionParameters
import com.rextechnologies.flint.protocol.wire.CodecId
import com.rextechnologies.flint.protocol.wire.HelloMessage
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import org.junit.After
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.Robolectric
import org.robolectric.Shadows.shadowOf
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode
import java.net.Inet4Address
import java.net.InetAddress
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/** The four tabs and the disclosure they share, rendered from a state rather than from a network. */
@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(qualifiers = "w411dp-h891dp-xxhdpi")
class ScreensTest {
    @get:Rule
    val compose = createComposeRule()

    private val context: Context = ApplicationProvider.getApplicationContext()
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private val controller = MobileController(context, "Pixel", 1080, 2400, 420, scope)
    private val activity: MobileActivity = Robolectric.buildActivity(MobileActivity::class.java).get()

    @After
    fun close() {
        controller.close()
        scope.cancel()
    }

    @Test
    fun `a detail section opens and closes on its own title`() {
        compose.setContent { Page { DetailSection("Connection details") { Text("The inside") } } }

        compose.onNodeWithText("The inside").assertDoesNotExist()
        compose.onNode(stateIs("Collapsed")).assertExists()

        compose.onNodeWithText("Connection details", ignoreCase = true).performClick()
        compose.onNodeWithText("The inside").assertIsDisplayed()
        compose.onNode(stateIs("Expanded")).assertExists()

        compose.onNodeWithText("Hide Connection details", ignoreCase = true).performClick()
        compose.onNodeWithText("The inside").assertDoesNotExist()
    }

    @Test
    fun `with no local network the cast tab offers only the hotspot settings`() {
        compose.setContent { Page { CastScreen(MobileUiState(), controller) } }

        compose.onNodeWithText("Connect to your TV").assertIsDisplayed()
        compose.onNodeWithText(CastCopy.NO_NETWORK_TITLE).assertIsDisplayed()
        compose.onNodeWithText("Find TVs", ignoreCase = true).assertDoesNotExist()
        compose.onNodeWithText(CastCopy.HOTSPOT_ACTION, ignoreCase = true).performClick()

        // No app may switch a hotspot on, so the button opens the settings page that can.
        val opened = shadowOf(ApplicationProvider.getApplicationContext<Application>()).nextStartedActivity
        assertEquals(Settings.ACTION_WIRELESS_SETTINGS, opened?.action)
    }

    @Test
    fun `before connecting the cast tab finds, lists and offers to connect the chosen tv`() {
        var state by mutableStateOf(
            MobileUiState(network = wifi, receivers = listOf(lounge, bedroom), selected = lounge),
        )
        compose.setContent { Page { CastScreen(state, controller) } }

        compose.onNodeWithText("Nearby TVs", ignoreCase = true).assertExists()
        compose.onNodeWithText("Selected", ignoreCase = true).assertExists()
        compose.onNodeWithText("Find TVs", ignoreCase = true).assertIsEnabled()
        compose.onNodeWithText("Connect to Lounge", ignoreCase = true).performScrollTo().performClick()
        compose.waitUntil(5_000) { controller.state.value.pairingVisible }

        compose.onNodeWithText("Bedroom").performScrollTo().performClick()
        compose.waitUntil(5_000) { controller.state.value.selected?.address == bedroom.address }

        state = state.copy(lookup = LookupState.Running, link = LinkState.Connecting("Lounge"))
        compose.onNodeWithText("Finding TVs…", ignoreCase = true).assertIsNotEnabled()
        compose.onNodeWithText("Connecting…", ignoreCase = true).assertIsNotEnabled()

        compose.onNodeWithText("Connection details", ignoreCase = true).performScrollTo().performClick()
        compose.onNodeWithText("${lounge.address}:${lounge.port}").assertExists()
        compose.onNodeWithText("Set up or manage Flint on this TV", ignoreCase = true).assertExists()
    }

    @Test
    fun `a probe that found nothing says so, and no tv means no setup section`() {
        val state = MobileUiState(
            network = wifi,
            lookup = LookupState.FoundNothing(listOf(DiscoveryRung.MULTICAST_DNS)),
            rungsAttempted = listOf(DiscoveryRung.MULTICAST_DNS),
            report = report(),
        )
        compose.setContent { Page { CastScreen(state, controller) } }

        compose.onNodeWithText(CastCopy.PROBE_FAILED_TITLE).assertExists()
        compose.onNodeWithText("Nearby TVs", ignoreCase = true).assertDoesNotExist()
        compose.onNodeWithText("Set up or manage Flint on this TV", ignoreCase = true).assertDoesNotExist()

        compose.onNodeWithText("Connection details", ignoreCase = true).performScrollTo().performClick()
        compose.onNodeWithText("Media reason.").assertExists()
    }

    @Test
    fun `once connected the cast tab is three choices about that tv`() {
        compose.setContent { Page { CastScreen(connected(), controller) } }

        compose.onNodeWithText("Your TV").assertIsDisplayed()
        compose.onNodeWithText("Lounge").assertIsDisplayed()
        compose.onNodeWithText("Find TVs", ignoreCase = true).assertDoesNotExist()

        compose.onNodeWithText("Share screen", ignoreCase = true).performClick()
        compose.waitUntil(5_000) { controller.state.value.tab == MobileTab.SCREEN }
        compose.onNodeWithText("Play media", ignoreCase = true).performClick()
        compose.waitUntil(5_000) { controller.state.value.tab == MobileTab.MEDIA }
        compose.onNodeWithText("Disconnect", ignoreCase = true).performClick()
    }

    @Test
    fun `a connected tv with nothing selected is still named`() {
        compose.setContent { Page { CastScreen(connected().copy(selected = null), controller) } }

        compose.onNodeWithText("Connected TV").assertIsDisplayed()
        compose.onNodeWithText("Set up or manage Flint on this TV", ignoreCase = true).assertDoesNotExist()
    }

    @Test
    fun `the live strip keeps its numbers behind session details`() {
        val output = LiveOutput(
            mode = OutputMode.SECOND_SCREEN,
            deviceName = "Lounge",
            width = 1280,
            height = 720,
            codec = CodecId.H264,
            elapsedSeconds = 65,
            health = LinkHealth.Stable,
            bitrateBitsPerSecond = 2_500_000,
            degradedReason = "The link is struggling.",
            scene = SecondScreenScene.Dashboard("Lounge", "Pixel", "Steady"),
            keyFrameFallback = true,
            diagnostics = diagnostics,
        )
        compose.setContent {
            Page { ScreenTab(connected().copy(output = output), controller, activity, onRequestMirror = {}) }
        }

        compose.onNodeWithText("The link is struggling.").assertExists()
        compose.onNodeWithText(ScreenCopy.STOP_SECOND_SCREEN, ignoreCase = true).assertExists()
        compose.onNodeWithText("1280×720").assertDoesNotExist()

        compose.onNodeWithText("Session details", ignoreCase = true).performScrollTo().performClick()

        compose.onNodeWithText("1280×720").assertExists()
        compose.onNodeWithText("1:05", substring = true).assertExists()
        compose.onNodeWithText("Every few seconds").assertExists()
        compose.onNodeWithText("2.5 Mbit/s").assertExists()
        compose.onNodeWithText(SecondScreenScene.Dashboard("Lounge", "Pixel", "Steady").title).assertExists()
    }

    @Test
    fun `a plain mirror strip leaves out the rows it has nothing for`() {
        val output = LiveOutput(
            mode = OutputMode.MIRROR,
            deviceName = "Lounge",
            width = 1920,
            height = 1080,
            codec = CodecId.H264,
            elapsedSeconds = 3,
            health = LinkHealth.Headroom,
            bitrateBitsPerSecond = 8_000_000,
            diagnostics = diagnostics,
        )
        compose.setContent {
            Page { ScreenTab(connected().copy(output = output), controller, activity, onRequestMirror = {}) }
        }

        compose.onNodeWithText("Session details", ignoreCase = true).performScrollTo().performClick()

        compose.onNodeWithText("1920×1080").assertExists()
        compose.onNodeWithText("Every few seconds").assertDoesNotExist()
        compose.onNodeWithText("Connection quality", ignoreCase = true).assertDoesNotExist()
        compose.onNodeWithText(ScreenCopy.STOP_MIRROR, ignoreCase = true).assertExists()
    }

    @Test
    fun `settings keeps setup and diagnostics folded until asked`() {
        var state by mutableStateOf(connected())
        compose.setContent { Page { SettingsScreen(state, controller, activity) } }

        compose.onNodeWithText(SettingsCopy.RUN_ENCODER_PROBE, ignoreCase = true).assertDoesNotExist()
        compose.onNodeWithText(SettingsCopy.FORGET_PAIRINGS, ignoreCase = true).performScrollTo().performClick()
        compose.waitUntil(5_000) { controller.state.value.notice == SettingsCopy.FORGOTTEN }

        compose.onNodeWithText("Manage Lounge", ignoreCase = true).performScrollTo().performClick()
        compose.onNodeWithText("Hide Manage Lounge", ignoreCase = true).assertExists()

        compose.onNodeWithText("Diagnostics and troubleshooting", ignoreCase = true).performScrollTo().performClick()
        compose.onNodeWithText(SettingsCopy.RUN_SECOND_SCREEN_PROBE, ignoreCase = true).assertExists()
        compose.onNodeWithText(SettingsCopy.RUN_ENCODER_PROBE, ignoreCase = true).assertIsEnabled()

        state = state.copy(encoderProbeRunning = true, secondScreenProbeRunning = true)
        compose.waitForIdle()
        compose.onNodeWithText(SettingsCopy.RUN_ENCODER_PROBE, ignoreCase = true).assertDoesNotExist()
        compose.onNodeWithText(SettingsCopy.RUN_SECOND_SCREEN_PROBE, ignoreCase = true).assertDoesNotExist()
        assertEquals(
            2,
            compose.onAllNodes(hasTextIgnoringCase(SettingsCopy.PROBE_RUNNING)).fetchSemanticsNodes().size,
        )
    }

    @Test
    fun `settings without a tv has nothing to manage`() {
        compose.setContent { Page { SettingsScreen(MobileUiState(network = wifi), controller, activity) } }

        compose.onNodeWithText("Manage", substring = true, ignoreCase = true).assertDoesNotExist()
        compose.onNodeWithText("Diagnostics and troubleshooting", ignoreCase = true).assertExists()
    }

    @Test
    fun `a connected phone with nothing playing can choose a video`() {
        var picked = false
        compose.setContent { Page { MediaScreen(connected(), controller, onPickVideo = { picked = true }) } }

        compose.onNodeWithText(MediaCopy.PICK_ACTION, ignoreCase = true).performScrollTo().performClick()

        compose.runOnIdle { assertTrue(picked) }
    }

    private fun connected(): MobileUiState = MobileUiState(
        network = wifi,
        receivers = listOf(lounge),
        selected = lounge,
        report = report(),
        link = LinkState.Connected(lounge, parameters),
    )

    private fun report(): CapabilityReport = CapabilityReport(
        network = wifi,
        phone = PhoneCapabilities(34, "Pixel", 1080, 2400, 420),
        device = lounge,
        path = null,
        verdicts = CastMode.entries.map {
            val reason = if (it == CastMode.MEDIA_HANDOFF) "Media reason." else "A reason."
            ModeVerdict(it, ModeStatus.AVAILABLE, reason)
        },
    )

    private fun stateIs(description: String) =
        SemanticsMatcher.expectValue(SemanticsProperties.StateDescription, description)

    private fun hasTextIgnoringCase(text: String) = SemanticsMatcher("text is $text") { node ->
        node.config.getOrElse(SemanticsProperties.Text) { emptyList() }.any { it.text.equals(text, ignoreCase = true) }
    }

    private companion object {
        val wifi = LocalNetwork.PhoneIsClient("wlan0", 3, address("192.168.1.44"), 24)
        val lounge = ReceiverDevice(address = "192.168.1.60", friendlyName = "Lounge", receiverAnswered = true)
        val bedroom = ReceiverDevice(address = "192.168.1.61", friendlyName = "Bedroom", receiverAnswered = true)
        val diagnostics = SessionDiagnostics(
            targetBitrate = 2_500_000,
            bitrateCeiling = 4_000_000,
            sessionMaximumBitrate = 8_000_000,
            receiverQueueDepth = 1,
            pendingSendBytes = 0,
            droppedFramesDelta = 0,
            lastDecision = "Held",
            thermalLevel = ThermalLevel.NONE,
        )
        val parameters = CastSessionParameters(
            protocolVersion = 1,
            peer = HelloMessage(
                minimumVersion = 1,
                maximumVersion = 1,
                deviceName = "Lounge",
                codecCapabilities = setOf(CodecId.H264),
                screenWidth = 1920,
                screenHeight = 1080,
                densityDpi = 320,
            ),
            videoCodec = CodecId.H264,
        )

        fun address(value: String): Inet4Address = InetAddress.getByName(value) as Inet4Address
    }
}

@Composable
private fun Page(content: @Composable () -> Unit) {
    FlintTheme { Column(Modifier.verticalScroll(rememberScrollState())) { content() } }
}

@Composable
private fun Text(text: String) = FlintText(text = text, style = FlintType.BodyMedium)
