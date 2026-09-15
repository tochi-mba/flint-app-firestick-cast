package com.rextechnologies.flint.receiver.ui

import com.rextechnologies.flint.receiver.ReceiverNetworkState
import com.rextechnologies.flint.receiver.ReceiverUiState
import java.util.Locale

internal enum class IdleExperience {
    STARTING,
    NO_NETWORK,
    READY,
    CONNECTED,
    ATTENTION,
}

internal data class PairingCodeGroups(
    val first: String,
    val second: String,
    val spoken: String,
)

internal fun ReceiverUiState.idleExperience(): IdleExperience = when {
    error != null -> IdleExperience.ATTENTION
    connected -> IdleExperience.CONNECTED
    ready -> IdleExperience.READY
    networkState == ReceiverNetworkState.STARTING -> IdleExperience.STARTING
    else -> IdleExperience.NO_NETWORK
}

/** Returns two atomic groups so a six-digit code can never wrap midway on a TV. */
internal fun pairingCodeGroups(value: String): PairingCodeGroups? {
    if (value.length != 6 || value.any { it !in '0'..'9' }) return null
    return PairingCodeGroups(
        first = value.substring(0, 3),
        second = value.substring(3, 6),
        spoken = value.toCharArray().joinToString(" "),
    )
}

internal fun endpointLabel(state: ReceiverUiState): String? =
    state.address?.let { "$it:${state.port}" }

/** Turns a sender filename into calm, television-friendly display copy. */
internal fun displayTitle(value: String, fallback: String = "Your media"): String {
    val source = value.trim()
    if (source.isEmpty()) return fallback
    val lastDot = source.lastIndexOf('.')
    val withoutExtension = if (lastDot > 0 && source.length - lastDot in 2..6) {
        source.substring(0, lastDot)
    } else {
        source
    }
    val readable = withoutExtension
        .replace(Regex("[_-]+"), " ")
        .replace(Regex("\\s+"), " ")
        .trim()
    return readable.replaceFirstChar { character ->
        if (character.isLowerCase()) character.titlecase(Locale.ROOT) else character.toString()
    }.ifBlank { fallback }
}

internal fun progressFraction(positionMs: Long, durationMs: Long): Float {
    if (positionMs <= 0 || durationMs <= 0) return 0f
    return (positionMs.toDouble() / durationMs.toDouble()).coerceIn(0.0, 1.0).toFloat()
}

internal fun elapsedLabel(milliseconds: Long): String {
    val totalSeconds = milliseconds.coerceAtLeast(0) / 1_000
    val hours = totalSeconds / 3_600
    val minutes = (totalSeconds % 3_600) / 60
    val seconds = totalSeconds % 60
    return if (hours > 0) {
        String.format(Locale.ROOT, "%d:%02d:%02d", hours, minutes, seconds)
    } else {
        String.format(Locale.ROOT, "%d:%02d", minutes, seconds)
    }
}

internal object ReceiverTags {
    const val ROOT = "receiver-root"
    const val HEADER = "receiver-header"
    const val STATUS = "receiver-status"
    const val IDLE_HERO = "receiver-idle-hero"
    const val PAIRING_PANEL = "receiver-pairing-panel"
    const val PAIRING_CODE = "receiver-pairing-code"
    const val ENDPOINT = "receiver-endpoint"
    const val BROWSER_PORT = "receiver-browser-port"
    const val PRIMARY_ACTION = "receiver-primary-action"
    const val BROWSER_ENTRY = "receiver-browser-entry"
    const val PLAYBACK_HUD = "receiver-playback-hud"
    const val PLAYBACK_ERROR = "receiver-playback-error"
    const val PLAYBACK_PLAY_PAUSE = "receiver-playback-play-pause"
    const val PLAYBACK_CLOSE = "receiver-playback-close"
    const val MIRROR_HUD = "receiver-mirror-hud"
    const val MIRROR_ERROR = "receiver-mirror-error"
    const val MIRROR_CONTROLS = "receiver-mirror-controls"
    const val MIRROR_CONTROLS_FIT = "receiver-mirror-controls-fit"
    const val MIRROR_CONTROLS_STATS = "receiver-mirror-controls-stats"
    const val MIRROR_CONTROLS_STOP = "receiver-mirror-controls-stop"
    const val MIRROR_STATS_PANEL = "receiver-mirror-stats-panel"
    const val MIRROR_HINT = "receiver-mirror-hint"
    const val BROWSER_SURFACE = "receiver-browser-surface"
    const val BROWSER_HOST_CURSOR = "receiver-browser-host-cursor"
    const val BROWSER_CHROME = "receiver-browser-chrome"
    const val BROWSER_STATUS = "receiver-browser-status"
    const val BROWSER_TITLE = "receiver-browser-title"
    const val BROWSER_URL = "receiver-browser-url"
    const val BROWSER_PROGRESS = "receiver-browser-progress"
    const val BROWSER_ERROR = "receiver-browser-error"
    const val BROWSER_DIALOG = "receiver-browser-dialog"
    const val BROWSER_NOTICE = "receiver-browser-notice"
    const val BROWSER_KEYBOARD = "receiver-browser-keyboard"
    const val BROWSER_OMNIBOX = "receiver-browser-omnibox"
    const val BROWSER_OMNIBOX_FIELD = "receiver-browser-omnibox-field"
    const val BROWSER_OMNIBOX_DESKTOP_HINT = "receiver-browser-omnibox-desktop-hint"
    const val BROWSER_OMNIBAR = "receiver-browser-omnibar"
    const val BROWSER_SECURITY_CHIP = "receiver-browser-security-chip"
    const val BROWSER_MODE_BADGE = "receiver-browser-mode-badge"
    const val BROWSER_CURSOR = "receiver-browser-cursor"
    const val BROWSER_TAB_STRIP = "receiver-browser-tab-strip"
    const val BROWSER_TAB_SWITCHER = "receiver-browser-tab-switcher"
    const val BROWSER_NEW_TAB = "receiver-browser-new-tab"
    const val BROWSER_ERROR_PAGE = "receiver-browser-error-page"
    const val BROWSER_MENU = "receiver-browser-menu"
    const val BROWSER_LIBRARY = "receiver-browser-library"
    const val BROWSER_CLEAR_DATA = "receiver-browser-clear-data"
    const val BROWSER_PROFILES = "receiver-browser-profiles"
    const val BROWSER_PROFILE_NAME = "receiver-browser-profile-name"
    const val BROWSER_PROFILE_DELETE = "receiver-browser-profile-delete"
    const val BROWSER_PROFILE_DEVICE = "receiver-browser-profile-device"
    const val BROWSER_FIND_BAR = "receiver-browser-find-bar"
    const val BROWSER_LEAVE_PROMPT = "receiver-browser-leave-prompt"
    const val BROWSER_WORKSPACE = "receiver-browser-workspace"
    const val BROWSER_NETWORK = "receiver-browser-network"
    const val BROWSER_VPN_BANNER = "receiver-browser-vpn-banner"
}
