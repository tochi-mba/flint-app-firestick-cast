package com.rextechnologies.flint.receiver.ui

import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.tv.material3.Text
import com.rextechnologies.flint.receiver.BrowserSurfaceUi
import kotlinx.coroutines.delay

/**
 * How long a settled page keeps its chrome on screen.
 *
 * Long enough to read an address from a sofa, short enough to be gone before anyone starts reading
 * the page itself.
 */
internal const val CHROME_AUTO_HIDE_MILLIS = 5_000L

/** How long the chrome takes to fade in or out. */
internal const val CHROME_FADE_MILLIS = 200

/** How much of the screen width the chrome may take before its text starts eliding. */
private const val CHROME_WIDTH_FRACTION = 0.6f

/** The overscan-safe inset every receiver surface uses. */
private const val SAFE_AREA_FRACTION = 0.05f

/**
 * The band of information drawn over the browsing page.
 *
 * # Why there is chrome at all
 *
 * The page fills the screen and belongs to whoever wrote it, so anything Flint draws over it is an
 * interruption and has to earn its place. Three things do:
 *
 * * **Which page this is.** A television across the room shows a page the viewer did not type; the
 *   address is the only way to tell a redirect or a mistyped domain from what was intended.
 * * **That something is happening.** A slow page on a television is indistinguishable from a broken
 *   one without a progress indication, and there is no tab spinner here to glance at.
 * * **What went wrong.** A failure that only reaches the desktop leaves whoever is watching the
 *   television staring at a blank page.
 *
 * Everything else — history, reload, the address bar — lives on the desktop, which has a keyboard
 * and a pointer. Putting controls here would mean building a browser UI operable by five buttons on
 * a remote, which is the thing this feature exists to avoid.
 *
 * # Why it hides itself
 *
 * Chrome that never leaves is a permanent bite out of the page. It shows while a page is loading —
 * exactly when a viewer needs to know something is happening — and withdraws once the page is
 * readable, matching how the mirror surface treats its controls. A failure is the exception and
 * stays up, because it is the one state with nothing underneath worth looking at.
 *
 * # Legibility over an unknown background
 *
 * The page underneath can be any colour, so the chrome carries its own dark ground and border
 * rather than relying on contrast that may not exist. Muted grey text laid directly over a white
 * page — which is what this replaced — is unreadable.
 *
 * @param page what the receiver knows about the page being shown.
 * @param autoHideMillis how long after a page settles the chrome withdraws, or `null` to hold it
 *   open. Tests pin this to `null` so a capture cannot race a fade.
 */
@Composable
internal fun ReceiverBrowserChrome(
    page: BrowserSurfaceUi,
    modifier: Modifier = Modifier,
    autoHideMillis: Long? = CHROME_AUTO_HIDE_MILLIS,
) {
    // A failure holds the chrome open on its own: withdrawing it would take away the only
    // explanation the viewer has.
    val pinned = page.isLoading || page.failure != null || autoHideMillis == null
    var visible by remember { mutableStateOf(true) }

    // Keyed on the page's identity as well as the pinning, so a fresh navigation brings the chrome
    // back rather than leaving it hidden by the previous page's timeout.
    LaunchedEffect(pinned, page.url, page.title) {
        visible = true
        // `pinned` is true whenever the delay is absent, so reaching here means there is one.
        if (!pinned && autoHideMillis != null) {
            delay(autoHideMillis)
            visible = false
        }
    }

    // A fade rather than a pop, drawn through alpha on an ordinary child rather than
    // `AnimatedVisibility`: the panel keeps a normal place in the layout while it is on screen and
    // leaves the tree entirely once it has finished fading.
    val opacity by animateFloatAsState(
        targetValue = if (visible) 1f else 0f,
        animationSpec = tween(durationMillis = CHROME_FADE_MILLIS),
        label = "browser-chrome-opacity",
    )

    // The chrome covers the whole panel, so the screen's own width is the width to size against.
    // Deliberately not `BoxWithConstraints`: it subcomposes during the measure pass, so content
    // that appears in response to a later state change — a second navigation bringing the chrome
    // back — composes but is never placed.
    val panelWidth = LocalConfiguration.current.screenWidthDp.dp * CHROME_WIDTH_FRACTION

    Box(modifier = modifier.fillMaxSize()) {
        if (opacity > 0f) {
            // Televisions crop the edges of the signal by an amount nobody can predict, and chrome
            // under the bezel is chrome that does not exist. A centred box at 90% of each dimension
            // is exactly the 5% inset every other receiver surface keeps.
            Box(
                modifier = Modifier
                    .fillMaxSize(1f - SAFE_AREA_FRACTION * 2f)
                    .align(Alignment.Center),
            ) {
                ChromePanel(
                    page = page,
                    maximumWidth = panelWidth,
                    modifier = Modifier.align(Alignment.TopStart).alpha(opacity),
                )
            }
        }
    }
}

/** The card itself: status, identity, progress, and a failure when there is one. */
@Composable
private fun ChromePanel(page: BrowserSurfaceUi, maximumWidth: Dp, modifier: Modifier = Modifier) {
    val shape = RoundedCornerShape(14.dp)
    Column(
        modifier = modifier
            .widthIn(max = maximumWidth)
            .background(ReceiverColors.Ink.copy(alpha = 0.92f), shape)
            .border(1.dp, ReceiverColors.Line, shape)
            .padding(horizontal = 18.dp, vertical = 12.dp)
            .testTag(ReceiverTags.BROWSER_CHROME),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            BrowserStatusPill(page)
            Spacer(Modifier.width(14.dp))
            Column {
                Text(
                    text = page.title.ifBlank { "Loading" },
                    color = ReceiverColors.Text,
                    fontSize = 18.sp,
                    lineHeight = 22.sp,
                    fontWeight = FontWeight.Bold,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.testTag(ReceiverTags.BROWSER_TITLE),
                )
                if (page.url.isNotBlank()) {
                    Text(
                        text = page.url,
                        color = ReceiverColors.Muted,
                        fontSize = 13.sp,
                        lineHeight = 17.sp,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                        modifier = Modifier.testTag(ReceiverTags.BROWSER_URL),
                    )
                }
            }
        }

        if (page.isLoading) {
            BrowserProgressBar(page.progressPercent)
        }

        page.failure?.let { failure ->
            Text(
                text = failure,
                color = ReceiverColors.Live,
                fontSize = 14.sp,
                lineHeight = 19.sp,
                modifier = Modifier.testTag(ReceiverTags.BROWSER_ERROR),
            )
        }
    }
}

/**
 * Whether the page is loading, as a state word rather than a spinner.
 *
 * A spinner on a television reads as "stuck" the moment it runs longer than a second. A word plus a
 * progress bar says the same thing and stays honest when the page is merely large.
 */
@Composable
private fun BrowserStatusPill(page: BrowserSurfaceUi) {
    val tone = when {
        page.failure != null -> ReceiverColors.Live
        page.isLoading -> ReceiverColors.Warning
        else -> ReceiverColors.Signal
    }
    val label = when {
        page.failure != null -> "BLOCKED"
        page.isLoading -> "LOADING"
        else -> "WEB"
    }

    Row(
        modifier = Modifier
            .background(tone.copy(alpha = 0.12f), RoundedCornerShape(50))
            .padding(horizontal = 10.dp, vertical = 6.dp)
            .testTag(ReceiverTags.BROWSER_STATUS),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(Modifier.size(6.dp).clip(CircleShape).background(tone))
        Spacer(Modifier.width(7.dp))
        Text(
            text = label,
            color = tone,
            fontSize = 10.sp,
            lineHeight = 12.sp,
            fontWeight = FontWeight.Bold,
            letterSpacing = 1.sp,
        )
    }
}

/**
 * A determinate progress bar.
 *
 * The fraction is clamped rather than trusted: the value originates in a page's own progress
 * reporting, and a width multiplier outside zero to one lays out a bar wider than its parent.
 */
@Composable
private fun BrowserProgressBar(progressPercent: Int) {
    val fraction = progressPercent.coerceIn(0, 100) / 100f
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .height(3.dp)
            .clip(RoundedCornerShape(50))
            .background(ReceiverColors.Line)
            .testTag(ReceiverTags.BROWSER_PROGRESS),
    ) {
        Box(
            Modifier
                .fillMaxWidth(fraction)
                .height(3.dp)
                .clip(RoundedCornerShape(50))
                .background(ReceiverColors.Signal),
        )
    }
}
