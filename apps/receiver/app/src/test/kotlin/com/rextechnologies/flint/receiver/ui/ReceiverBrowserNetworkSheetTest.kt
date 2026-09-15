package com.rextechnologies.flint.receiver.ui

import androidx.activity.ComponentActivity
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.rextechnologies.flint.receiver.browser.BrowserVpnState
import com.rextechnologies.flint.receiver.browser.ProfileNetworkSettings
import com.rextechnologies.flint.receiver.browser.VpnCapability
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.Config

@RunWith(AndroidJUnit4::class)
@Config(qualifiers = "w960dp-h540dp-television-xhdpi")
class ReceiverBrowserNetworkSheetTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    @Test
    fun `unavailable vpn stays honest and offers back`() {
        compose.setContent {
            ReceiverTheme {
                ReceiverBrowserNetworkSheet(
                    settings = ProfileNetworkSettings(),
                    capability = VpnCapability(preparable = false, reason = "VpnService probe failed"),
                    vpnState = BrowserVpnState.Unavailable,
                    onToggleEnabled = {},
                    onToggleAutoConnect = {},
                    onToggleRequireVpn = {},
                    onConnect = {},
                    onClear = {},
                    onClose = {},
                )
            }
        }
        compose.waitForIdle()
        compose.onNodeWithTag(ReceiverTags.BROWSER_NETWORK).assertIsDisplayed()
        compose.onNodeWithText("NETWORK", substring = true).assertIsDisplayed()
        compose.onNodeWithTag(ReceiverTags.BROWSER_VPN_BANNER).assertIsDisplayed()
        compose.onNodeWithText("VPN off", substring = true).assertDoesNotExist()
    }

    @Test
    fun `failed connect shows soft-fail banner`() {
        compose.setContent {
            ReceiverTheme {
                ReceiverBrowserNetworkSheet(
                    settings = ProfileNetworkSettings(vpnEnabled = true),
                    capability = VpnCapability(preparable = true, reason = "ok"),
                    vpnState = BrowserVpnState.Failed("VPN permission not granted"),
                    onToggleEnabled = {},
                    onToggleAutoConnect = {},
                    onToggleRequireVpn = {},
                    onConnect = {},
                    onClear = {},
                    onClose = {},
                )
            }
        }
        compose.waitForIdle()
        compose.onNodeWithTag(ReceiverTags.BROWSER_VPN_BANNER).assertIsDisplayed()
        compose.onNodeWithText("browsing uses the normal network", substring = true).assertIsDisplayed()
    }
}
