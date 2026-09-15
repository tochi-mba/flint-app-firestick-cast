package com.rextechnologies.flint.receiver.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.tv.material3.Text
import com.rextechnologies.flint.receiver.BrowserSurfaceUi

/** A page-level failure with an action; a tiny toast cannot rescue an otherwise blank screen. */
@Composable
internal fun ReceiverBrowserErrorPage(
    page: BrowserSurfaceUi,
    onRetry: () -> Unit,
    onEditAddress: () -> Unit,
) {
    val retryFocus = remember { FocusRequester() }
    LaunchedEffect(page.failure) { retryFocus.requestFocus() }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(ReceiverColors.Ink)
            .testTag(ReceiverTags.BROWSER_ERROR_PAGE),
        contentAlignment = Alignment.Center,
    ) {
        Column(
            modifier = Modifier
                .fillMaxSize(ReceiverOverscan.CONTENT_FRACTION)
                .padding(ReceiverSpace.XLarge),
            verticalArrangement = Arrangement.Center,
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Text(text = "PAGE NOT AVAILABLE", style = ReceiverType.Label, color = ReceiverColors.Live)
            Text(
                text = page.title.ifBlank { "We couldn't open this page" },
                style = ReceiverType.Title,
                color = ReceiverColors.Text,
                modifier = Modifier.padding(top = ReceiverSpace.Small),
            )
            Text(
                text = page.failure.orEmpty(),
                style = ReceiverType.Body,
                color = ReceiverColors.Muted,
                modifier = Modifier
                    .padding(top = ReceiverSpace.Compact, bottom = ReceiverSpace.Large)
                    .semantics { contentDescription = "Browser error: ${page.failure.orEmpty()}" },
            )
            Row(horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Compact)) {
                TvActionButton(
                    label = "Try again",
                    enabled = true,
                    onClick = onRetry,
                    modifier = Modifier.focusRequester(retryFocus),
                )
                TvActionButton(label = "Edit address", enabled = true, onClick = onEditAddress)
            }
        }
    }
}
