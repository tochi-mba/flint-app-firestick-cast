package com.rextechnologies.flint.mobile.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBars
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
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.LiveRegionMode
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
import java.util.Locale

/**
 * The app's one screen.
 *
 * The desktop's left rail becomes a bottom bar of four, and the page grid is carried over unchanged
 * — a heading, then cards twelve apart inside a twenty-eight margin. It goes full width instead of
 * stopping at 820, which is the only thing about the layout a phone actually changes.
 */
@Composable
fun MobileApp(controller: MobileController, activity: MobileActivity) {
    val state by controller.state.collectAsState()

    if (!state.introductionSeen) {
        OnboardingScreen(
            onFinish = {
                controller.markIntroductionSeen()
                controller.probe()
            },
            onSkip = controller::markIntroductionSeen,
        )
        return
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
                MobileTab.SCREEN -> ScreenTab(state, controller)
                MobileTab.MEDIA -> MediaScreen(state)
                MobileTab.SETTINGS -> SettingsScreen(state, controller, activity)
            }

            state.notice?.let { notice ->
                Spacer(Modifier.height(FlintSpace.Tiny))
                NoticeCard(notice, controller::dismissNotice)
            }
        }

        BottomBar(selected = state.tab, onSelect = controller::selectTab)
    }

    if (state.pairingVisible) {
        PairingSheet(
            error = state.pairingError,
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
private fun NoticeCard(notice: String, onDismiss: () -> Unit) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .background(FlintColors.Raised, FlintShapes.Small)
            .padding(horizontal = FlintSpace.Compact, vertical = FlintSpace.Small)
            .semantics { liveRegion = LiveRegionMode.Polite },
        verticalArrangement = Arrangement.spacedBy(FlintSpace.Small),
    ) {
        FlintText(text = notice, style = FlintType.BodySmall.copy(color = FlintColors.Text))
        Box(modifier = Modifier.flintClickable(onClick = onDismiss)) {
            FlintText(
                text = "DISMISS",
                style = FlintType.LabelSmall.copy(color = FlintColors.Signal),
            )
        }
    }
}

@Composable
private fun BottomBar(selected: MobileTab, onSelect: (MobileTab) -> Unit) {
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
                .windowInsetsPadding(WindowInsets.navigationBars)
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

@Composable
private fun TabButton(tab: MobileTab, selected: Boolean, onSelect: () -> Unit) {
    val tint = if (selected) FlintColors.Signal else FlintColors.Muted
    Column(
        modifier = Modifier
            .flintClickable(onClick = onSelect, onClickLabel = tab.title)
            .semantics { contentDescription = "${tab.title} tab" }
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
                ),
            contentAlignment = Alignment.Center,
        ) {
            FlintText(text = tab.glyph, style = FlintType.TitleMedium.copy(color = tint))
        }
        FlintText(
            text = tab.title.uppercase(Locale.ROOT),
            style = FlintType.LabelSmall.copy(color = tint),
        )
    }
}
