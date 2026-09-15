package com.rextechnologies.flint.receiver

import com.rextechnologies.flint.receiver.browser.BrowserRefusal
import com.rextechnologies.flint.receiver.browser.PendingJsDialog

/*
 * The service's browser switchboard.
 *
 * Every function here forwards one call to ReceiverBrowserController. They are extensions rather
 * than members because they carry no logic and no state: keeping them in ReceiverService buried
 * the parts of it that do - the media session, the mirror pipeline, the notification - under a
 * hundred lines of one-line forwarding.
 *
 * The names stay as they were, because the Activity and the Compose surfaces call them.
 */

fun ReceiverService.zoomBrowserIn() = browserController.zoomBrowserIn()

fun ReceiverService.zoomBrowserOut() = browserController.zoomBrowserOut()

fun ReceiverService.resetBrowserZoom() = browserController.resetBrowserZoom()

fun ReceiverService.cycleBrowserUserAgent() = browserController.cycleBrowserUserAgent()

fun ReceiverService.toggleBrowserDarkMode() = browserController.toggleBrowserDarkMode()

fun ReceiverService.setBrowserInputMode(mode: com.rextechnologies.flint.receiver.browser.BrowserInteractionMode) =
    browserController.setBrowserInputMode(mode)

fun ReceiverService.cycleBrowserSearchEngine() = browserController.cycleBrowserSearchEngine()

fun ReceiverService.startBrowserFind(query: String) = browserController.startFind(query)

fun ReceiverService.nextBrowserFind() = browserController.nextFind()

fun ReceiverService.previousBrowserFind() = browserController.previousFind()

fun ReceiverService.clearBrowserFind() = browserController.clearFind()

fun ReceiverService.toggleBrowserBookmark() = browserController.toggleBookmark()

fun ReceiverService.removeBrowserBookmark(url: String) = browserController.removeBookmark(url)

fun ReceiverService.selectBrowserTvProfile(profileId: String) = browserController.selectTvProfile(profileId)

fun ReceiverService.selectBrowserConnectedDeviceProfile() = browserController.selectConnectedDeviceProfile()

fun ReceiverService.createBrowserTvProfile(name: String) = browserController.createTvProfile(name)

fun ReceiverService.renameBrowserTvProfile(profileId: String, name: String) =
    browserController.renameTvProfile(profileId, name)

fun ReceiverService.deleteBrowserTvProfile(profileId: String) = browserController.deleteTvProfile(profileId)

fun ReceiverService.requestBrowserDataClear() = browserController.requestClearData()

fun ReceiverService.attachVpnConsentHost(host: com.rextechnologies.flint.receiver.browser.VpnConsentHost?) =
    browserController.attachVpnConsentHost(host)

fun ReceiverService.browserNetworkSettings(): com.rextechnologies.flint.receiver.browser.ProfileNetworkSettings =
    browserController.networkSettingsForActiveProfile()

fun ReceiverService.browserVpnCapability(): com.rextechnologies.flint.receiver.browser.VpnCapability =
    browserController.vpnCapability()

fun ReceiverService.browserVpnState(): kotlinx.coroutines.flow.StateFlow<com.rextechnologies.flint.receiver.browser.BrowserVpnState> =
    browserController.vpnState

fun ReceiverService.toggleBrowserVpnEnabled() = browserController.toggleVpnEnabledForActiveProfile()

fun ReceiverService.toggleBrowserVpnAutoConnect() = browserController.toggleVpnAutoConnectForActiveProfile()

fun ReceiverService.toggleBrowserRequireVpn() = browserController.toggleRequireVpnBeforeBrowseForActiveProfile()

fun ReceiverService.allowBrowserUnderVpnPolicy() = browserController.allowBrowsingUnderVpnPolicy()

fun ReceiverService.clearBrowserVpn() = browserController.clearVpnForActiveProfile()

fun ReceiverService.connectBrowserVpn() = browserController.connectVpnForActiveProfile()

fun ReceiverService.ensureBrowserVpn() = browserController.ensureVpnForActiveProfile()

fun ReceiverService.onBrowserPageDialog(pending: PendingJsDialog) = browserController.onPageDialog(pending)

fun ReceiverService.replyBrowserDialogLocal(accepted: Boolean, promptText: String? = null) =
    browserController.replyDialogLocal(accepted, promptText)

fun ReceiverService.showBrowserRefusal(refusal: BrowserRefusal) = browserController.showRefusal(refusal)

fun ReceiverService.dismissBrowserNotice() = browserController.dismissNotice()

fun ReceiverService.setBrowserFullscreen(active: Boolean) = browserController.setFullscreen(active)

fun ReceiverService.setBrowserEditing(active: Boolean) = browserController.setEditing(active)

fun ReceiverService.navigateBrowserFromTv(url: String) = browserController.navigateFromTv(url)

fun ReceiverService.closeBrowserFromTv() = browserController.closeFromTv()

fun ReceiverService.showBrowserNotice(message: String) = browserController.showNotice(message)

/** The television asking to leave a page's fullscreen; the page is told, as if it had asked. */
