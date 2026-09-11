package com.rextechnologies.flint.mobile.ui

import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.height
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import com.rextechnologies.flint.castcore.capability.CastMode
import com.rextechnologies.flint.castcore.copy.MediaCopy
import com.rextechnologies.flint.castcore.copy.MobileTab
import com.rextechnologies.flint.design.AdvisoryBlock
import com.rextechnologies.flint.design.EmptyState
import com.rextechnologies.flint.design.FlintSpace
import com.rextechnologies.flint.design.InfoCard
import com.rextechnologies.flint.design.PageHeading
import com.rextechnologies.flint.design.SectionLabel
import com.rextechnologies.flint.design.Tone
import com.rextechnologies.flint.mobile.MobileUiState

/**
 * The Media tab.
 *
 * The picker and the push path are the next slice's work, so this page shows the verdict and the
 * empty state and offers no control that would fail when pressed.
 */
@Composable
fun MediaScreen(state: MobileUiState) {
    val tab = MobileTab.MEDIA
    PageHeading(eyebrow = tab.eyebrow, headline = tab.title)

    Spacer(Modifier.height(FlintSpace.Small))
    state.report?.let { report ->
        SectionLabel("What this pair can do")
        ModeCard(report[CastMode.MEDIA_HANDOFF])
    }

    Spacer(Modifier.height(FlintSpace.Small))
    EmptyState(
        glyph = MediaCopy.empty.glyph,
        title = MediaCopy.empty.title,
        body = MediaCopy.empty.body,
    )

    Spacer(Modifier.height(FlintSpace.Small))
    InfoCard(borderTone = Tone.Line) {
        SectionLabel("How it will work")
        AdvisoryBlock(heading = "WHY IT IS SENT RATHER THAN FETCHED", body = MediaCopy.WHY_PUSHED)
    }
}
