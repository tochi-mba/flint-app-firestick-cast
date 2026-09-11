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
import com.rextechnologies.flint.design.FlintColors
import com.rextechnologies.flint.design.FlintSpace
import com.rextechnologies.flint.design.FlintText
import com.rextechnologies.flint.design.FlintType
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
    val report = state.report
    if (report == null) {
        // One state, not two. Rendering the empty state under a verdict card said both "nothing is
        // known about this pair" and "here is what this pair can do" on the same screen.
        EmptyState(
            glyph = MediaCopy.empty.glyph,
            title = MediaCopy.empty.title,
            body = MediaCopy.empty.body,
        )
        return
    }

    SectionLabel("What this pair can do")
    ModeCard(report[CastMode.MEDIA_HANDOFF])

    Spacer(Modifier.height(FlintSpace.Small))
    InfoCard(borderTone = Tone.Line) {
        SectionLabel("How it will work")
        AdvisoryBlock(heading = "WHY IT IS SENT RATHER THAN FETCHED", body = MediaCopy.WHY_PUSHED)
        // No picker and no transport control. Pushing a file is slice 09's work, and a control
        // wired to nothing is the defect the verdict above exists to prevent.
        FlintText(
            text = "Choosing a file is not in this build yet. The verdict above is what this phone " +
                "and this television can do once it is.",
            style = FlintType.BodySmall.copy(color = FlintColors.Muted),
        )
    }
}
