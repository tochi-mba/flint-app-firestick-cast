package com.rextechnologies.flint.receiver.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.tv.material3.Text
import com.rextechnologies.flint.receiver.BrowserSurfaceUi

/** What the omnibar can do, so the surface decides the effect rather than the chrome. */
internal enum class OmnibarAction { BACK, FORWARD, RELOAD_OR_STOP, HOME, ADDRESS, TABS, WORKSPACE, MENU }

/**
 * The band of controls over the page: history, reload, the address, and the way into everything
 * else.
 *
 * Replaces a passive panel that showed a title and a URL and had no controls at all — the previous
 * design deliberately kept history and the address bar on the desktop, which left the television
 * unusable on its own.
 *
 * Two details matter more than the rest. Reload and Stop are one button that swaps glyph, so the row
 * never reflows mid-load and nothing moves under a viewer's selection. And the address shows the
 * registrable domain in full strength with the rest dimmed, so a look-alike path cannot pass itself
 * off as the site it is pretending to be.
 */
@Composable
internal fun ReceiverBrowserOmnibar(
    page: BrowserSurfaceUi,
    tabCount: Int,
    activeTab: Int,
    firstControl: FocusRequester,
    onAction: (OmnibarAction) -> Unit,
) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .background(ReceiverColors.Scrim, ReceiverShapes.Medium)
            .border(ReceiverSpace.Hairline, ReceiverColors.Line, ReceiverShapes.Medium)
            .padding(ReceiverSpace.Compact)
            .testTag(ReceiverTags.BROWSER_OMNIBAR),
        verticalArrangement = Arrangement.spacedBy(ReceiverSpace.Small),
    ) {
        Row(
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Small),
        ) {
            OmnibarButton(
                glyph = "◀",
                name = "Back",
                enabled = page.canGoBack,
                onClick = { onAction(OmnibarAction.BACK) },
            )
            OmnibarButton(
                glyph = "▶",
                name = "Forward",
                enabled = page.canGoForward,
                onClick = { onAction(OmnibarAction.FORWARD) },
            )
            OmnibarButton(
                // One button, two jobs. Two buttons where one appears only while loading makes the
                // whole row jump exactly when someone is reaching for it.
                glyph = if (page.isLoading) "✕" else "⟳",
                name = if (page.isLoading) "Stop loading" else "Reload",
                onClick = { onAction(OmnibarAction.RELOAD_OR_STOP) },
            )
            OmnibarButton(
                glyph = "⌂",
                name = "New tab page",
                onClick = { onAction(OmnibarAction.HOME) },
            )

            // Focus lands here, not on Back.
            //
            // Back is disabled whenever there is no history — which is the state a freshly opened
            // page is always in — and a disabled control cannot take focus. Requesting it there left
            // the chrome visible with nothing focused and the D-pad doing nothing at all, found on a
            // Fire TV Stick 4K. The address is also simply the right answer: it is always available
            // and it is what someone summoning the chrome most often wants.
            AddressChip(
                page = page,
                focusRequester = firstControl,
                modifier = Modifier.weight(1f),
                onClick = { onAction(OmnibarAction.ADDRESS) },
            )

            OmnibarButton(
                glyph = "▤",
                name = "Tabs, $activeTab of $tabCount",
                badge = tabCount.takeIf { it > 1 }?.toString(),
                onClick = { onAction(OmnibarAction.TABS) },
            )
            OmnibarButton(
                glyph = "▦",
                name = "Browser workspace — several web pages side by side",
                onClick = { onAction(OmnibarAction.WORKSPACE) },
            )
            OmnibarButton(
                glyph = "⋮",
                name = "Browser menu",
                onClick = { onAction(OmnibarAction.MENU) },
            )
        }

        OmnibarProgress(page = page)
    }
}

@Composable
private fun AddressChip(
    page: BrowserSurfaceUi,
    focusRequester: FocusRequester,
    modifier: Modifier = Modifier,
    onClick: () -> Unit,
) {
    val host = page.url.registrableHost()
    val remainder = page.url.afterHost()

    Row(
        modifier = modifier
            .defaultMinSize(minHeight = 52.dp)
            .background(ReceiverColors.Ink, ReceiverShapes.Pill)
            .focusRequester(focusRequester)
            .tvClickable(shape = ReceiverShapes.Pill, onClick = onClick)
            .padding(horizontal = ReceiverSpace.Medium, vertical = ReceiverSpace.Small)
            .testTag(ReceiverTags.BROWSER_SECURITY_CHIP)
            .semantics {
                contentDescription = if (page.url.isBlank()) {
                    "Search or enter address"
                } else {
                    "Address, ${page.url}. Select to change it."
                }
            },
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Small),
    ) {
        if (page.url.isNotBlank()) {
            // Every address the policy allows is https, so the lock is a statement of fact rather
            // than a per-site judgement.
            Text(text = "🔒", style = ReceiverType.Caption, color = ReceiverColors.Success)
        }

        if (page.url.isBlank()) {
            Text(
                text = "Search or enter address",
                style = ReceiverType.Body,
                color = ReceiverColors.Muted,
                maxLines = 1,
            )
        } else {
            Row(verticalAlignment = Alignment.CenterVertically) {
                // The domain is what tells someone where they are. Everything after it is dimmed so
                // a long deceptive path cannot out-shout the name it is hiding under.
                Text(
                    text = host,
                    style = ReceiverType.Body.copy(fontWeight = FontWeight.Bold),
                    color = ReceiverColors.Text,
                    maxLines = 1,
                )
                if (remainder.isNotEmpty()) {
                    Text(
                        text = remainder,
                        style = ReceiverType.Body,
                        color = ReceiverColors.Muted,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
            }
        }
    }
}

@Composable
private fun OmnibarButton(
    glyph: String,
    name: String,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    badge: String? = null,
    onClick: () -> Unit,
) {
    Box(
        modifier = modifier
            .defaultMinSize(minWidth = 52.dp, minHeight = 52.dp)
            .background(ReceiverColors.Panel, ReceiverShapes.Pill)
            .tvClickable(enabled = enabled, shape = ReceiverShapes.Pill, onClick = onClick)
            .semantics { contentDescription = name },
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = if (badge != null) "$glyph $badge" else glyph,
            style = ReceiverType.Body,
            // A disabled control stays visible and stays dim. Removing it would move everything
            // beside it, which is worse than a button that is plainly not available.
            color = if (enabled) ReceiverColors.Text else ReceiverColors.Line,
        )
    }
}

@Composable
private fun OmnibarProgress(page: BrowserSurfaceUi) {
    if (!page.isLoading) {
        return
    }
    val fraction = (page.progressPercent.coerceIn(0, 100)) / 100f
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .height(3.dp)
            .background(ReceiverColors.Line, ReceiverShapes.Pill)
            .testTag(ReceiverTags.BROWSER_PROGRESS),
    ) {
        Box(
            modifier = Modifier
                .fillMaxWidth(fraction)
                .height(3.dp)
                .background(ReceiverColors.Signal, ReceiverShapes.Pill),
        )
    }
}

/**
 * The part of a display URL that identifies the site.
 *
 * Deliberately simple: the display URL has already been through [BrowserUrlPolicy], which strips the
 * query and fragment and normalises the host, so this only has to find where the host ends.
 */
private fun String.registrableHost(): String {
    val withoutScheme = substringAfter("://", missingDelimiterValue = this)
    return withoutScheme.substringBefore('/')
}

private fun String.afterHost(): String {
    val withoutScheme = substringAfter("://", missingDelimiterValue = this)
    val slash = withoutScheme.indexOf('/')
    return if (slash < 0) "" else withoutScheme.substring(slash)
}
