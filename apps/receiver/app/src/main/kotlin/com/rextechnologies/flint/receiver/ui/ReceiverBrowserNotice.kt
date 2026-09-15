package com.rextechnologies.flint.receiver.ui

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutVertically
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.tv.material3.Text
import com.rextechnologies.flint.receiver.BrowserNoticeUi
import com.rextechnologies.flint.receiver.browser.BrowserNoticeReducer
import kotlinx.coroutines.delay

/**
 * A short line explaining something the browser would not do.
 *
 * Placed low and centred, inside the overscan-safe area, so it never covers the omnibar and never
 * lands where a television's edges are unreliable. It takes no focus: the viewer should keep
 * browsing, and reading it is optional.
 *
 * The timer lives here rather than in the reducer because expiry is a frame concern; the reducer
 * only decides whether a notice is still current for a given clock reading.
 */
@Composable
internal fun ReceiverBrowserNotice(
    notice: BrowserNoticeUi,
    onExpired: () -> Unit,
    visibleMillis: Long? = BrowserNoticeReducer.VISIBLE_MILLIS,
) {
    // Keyed on the id so a replacement restarts the countdown while a repeat does not.
    LaunchedEffect(notice.id, visibleMillis) {
        if (visibleMillis != null) {
            delay(visibleMillis)
            onExpired()
        }
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .padding(bottom = ReceiverSpace.Huge),
        contentAlignment = Alignment.BottomCenter,
    ) {
        AnimatedVisibility(
            visible = true,
            enter = fadeIn() + slideInVertically { it / 2 },
            exit = fadeOut() + slideOutVertically { it / 2 },
        ) {
            Box(
                modifier = Modifier
                    .fillMaxWidth(ReceiverOverscan.CONTENT_FRACTION)
                    .background(ReceiverColors.Scrim, ReceiverShapes.Medium)
                    .border(ReceiverSpace.Hairline, ReceiverColors.Line, ReceiverShapes.Medium)
                    .padding(horizontal = ReceiverSpace.Large, vertical = ReceiverSpace.Compact)
                    .testTag(ReceiverTags.BROWSER_NOTICE)
                    .semantics {
                        liveRegion = LiveRegionMode.Polite
                        contentDescription = notice.message
                    },
                contentAlignment = Alignment.Center,
            ) {
                Text(
                    text = notice.message,
                    style = ReceiverType.Body,
                    color = ReceiverColors.Text,
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }
    }
}
