package com.rextechnologies.flint.mobile.ui

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import com.rextechnologies.flint.castcore.copy.MobileTab
import com.rextechnologies.flint.design.FlintColors
import com.rextechnologies.flint.design.FlintShapes
import com.rextechnologies.flint.design.FlintSpace
import com.rextechnologies.flint.design.FlintText
import com.rextechnologies.flint.design.FlintType
import com.rextechnologies.flint.design.flintClickable
import com.rextechnologies.flint.mobile.MobileActivity
import com.rextechnologies.flint.mobile.MobileController
import com.rextechnologies.flint.mobile.MobileUiState
import java.util.Locale

/**
 * The app's one screen.
 *
 * The desktop's left rail becomes a bottom bar of four, and the page grid is carried over unchanged
 * — a heading, then cards twelve apart inside a twenty-eight margin. It goes full width instead of
 * stopping at 820, which is the only thing about the layout a phone actually changes.
 */
@Composable
fun MobileApp(
    controller: MobileController,
    activity: MobileActivity,
    onRequestMirror: () -> Unit,
    onPickVideo: () -> Unit,
) {
    val state by controller.state.collectAsState()

    // Null means the stored answer has not arrived yet. Showing either screen on a guess means one
    // of them is wrong, and the wrong one is the six-step introduction somebody has already read.
    when (state.introductionSeen) {
        null -> Box(Modifier.fillMaxSize().background(FlintColors.Ink))

        false -> OnboardingScreen(
            onFinish = {
                controller.markIntroductionSeen()
                controller.probe()
            },
            // The same probe as onFinish. Skipping the introduction is skipping the reading, not
            // asking to land on a page with nothing on it.
            onSkip = {
                controller.markIntroductionSeen()
                controller.probe()
            },
        )

        true -> MainScreen(state, controller, activity, onRequestMirror, onPickVideo)
    }
}

@Composable
private fun MainScreen(
    state: MobileUiState,
    controller: MobileController,
    activity: MobileActivity,
    onRequestMirror: () -> Unit,
    onPickVideo: () -> Unit,
) {
    // Back closes the sheet, then dismisses the notice, then returns to Cast, and only then leaves.
    // Without it back exited the app from any tab, which is not what a bottom bar teaches.
    BackHandler(enabled = state.pairingVisible || state.notice != null || state.tab != MobileTab.CAST) {
        controller.onBack()
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(FlintColors.Ink)
            .windowInsetsPadding(WindowInsets.safeDrawing),
    ) {
        Column(
            modifier = Modifier
                .weight(1f)
                .fillMaxWidth()
                .verticalScroll(rememberScrollState())
                .padding(horizontal = FlintSpace.PageMargin)
                .padding(top = FlintSpace.Large, bottom = FlintSpace.Large),
            verticalArrangement = Arrangement.spacedBy(FlintSpace.CardSpacing),
        ) {
            when (state.tab) {
                MobileTab.CAST -> CastScreen(state, controller)
                MobileTab.SCREEN -> ScreenTab(state, controller, activity, onRequestMirror)
                MobileTab.MEDIA -> MediaScreen(state, controller, onPickVideo)
                MobileTab.SETTINGS -> SettingsScreen(state, controller, activity)
            }
        }

        // Above the bar rather than at the end of the page. A notice appears in response to a press,
        // and at the bottom of a scrolling page it appeared below the fold — out of sight of the
        // button that raised it, which is the one place it needed to be.
        state.notice?.let { notice -> NoticeBanner(notice, controller::dismissNotice) }

        BottomBar(selected = state.tab, onSelect = controller::selectTab)
    }

    if (state.pairingVisible) {
        PairingSheet(
            error = state.pairingError,
            connecting = state.isConnecting,
            onSubmit = controller::submitPairingCode,
            onDismiss = { controller.showPairing(false) },
        )
    }
}

/**
 * A message that needs reading once.
 *
 * Marked polite rather than assertive: it appears in response to something the person just did, so a
 * screen reader should finish the sentence it is on before announcing it.
 */
@Composable
private fun NoticeBanner(notice: String, onDismiss: () -> Unit) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .background(FlintColors.Raised)
            .padding(horizontal = FlintSpace.PageMargin, vertical = FlintSpace.Compact)
            .semantics { liveRegion = LiveRegionMode.Polite },
        verticalAlignment = Alignment.Top,
        horizontalArrangement = Arrangement.spacedBy(FlintSpace.Compact),
    ) {
        FlintText(
            text = notice,
            style = FlintType.BodySmall.copy(color = FlintColors.Text),
            modifier = Modifier.weight(1f),
        )
        Box(
            modifier = Modifier.flintClickable(onClick = onDismiss, onClickLabel = "Dismiss"),
        ) {
            FlintText(
                text = "DISMISS",
                style = FlintType.LabelSmall.copy(color = FlintColors.Signal),
            )
        }
    }
}

@Composable
internal fun BottomBar(selected: MobileTab, onSelect: (MobileTab) -> Unit) {
    Column(modifier = Modifier.fillMaxWidth()) {
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .height(FlintSpace.Hairline)
                .background(FlintColors.Line),
        )
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .background(FlintColors.Ink)
                .padding(horizontal = FlintSpace.Compact, vertical = FlintSpace.Small),
            horizontalArrangement = Arrangement.SpaceEvenly,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            MobileTab.entries.forEach { tab ->
                TabButton(tab = tab, selected = tab == selected, onSelect = { onSelect(tab) })
            }
        }
    }
}

/**
 * One tab.
 *
 * `Role.Tab` and `selected` are what let a screen-reader user know which one is active — the colour
 * that says so visually is invisible to them. The glyph and the label are cleared and replaced by
 * the tab's own name, because unmerged they were announced as "Cast tab, C, CAST".
 *
 * The navigation-bar inset is not applied here. The outer column already consumes `safeDrawing`,
 * which includes it, and applying it again pushed the bar up by its own height above the gesture
 * area.
 */
@Composable
private fun TabButton(tab: MobileTab, selected: Boolean, onSelect: () -> Unit) {
    val tint = if (selected) FlintColors.Signal else FlintColors.Muted
    Column(
        modifier = Modifier
            // The role is given to the clickable itself rather than added beside it, so there is one
            // role on this node instead of a button's and a tab's disagreeing.
            .flintClickable(onClick = onSelect, onClickLabel = tab.title, role = Role.Tab)
            .semantics(mergeDescendants = true) {
                contentDescription = tab.title
                this.selected = selected
            }
            .padding(horizontal = FlintSpace.Compact, vertical = FlintSpace.Tiny),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(FlintSpace.Tiny),
    ) {
        Box(
            modifier = Modifier
                .size(FlintSpace.XLarge)
                .background(
                    if (selected) FlintColors.SignalWash else FlintColors.Ink,
                    FlintShapes.Small,
                )
                // The letterform is decoration standing in for an icon. Announcing it reads as a
                // stray letter between the tab's name and its label.
                .clearAndSetSemantics { },
            contentAlignment = Alignment.Center,
        ) {
            FlintText(text = tab.glyph, style = FlintType.TitleMedium.copy(color = tint))
        }
        FlintText(
            text = tab.title.uppercase(Locale.ROOT),
            style = FlintType.LabelSmall.copy(color = tint),
            // The upper-cased label is the tab's name a second time. The description on the node
            // is the copy that is read out.
            modifier = Modifier.clearAndSetSemantics { },
        )
    }
}
