package com.rextechnologies.flint.mobile.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import com.rextechnologies.flint.castcore.capability.MobileCapabilityAssessor
import com.rextechnologies.flint.design.FlintColors
import com.rextechnologies.flint.design.FlintSpace
import com.rextechnologies.flint.design.FlintText
import com.rextechnologies.flint.design.FlintType

/**
 * What the television shows while the phone is the cockpit.
 *
 * Flint's own screen, drawn into a display only Flint can see — which is the whole of what a second
 * screen is on Android, and the reason every card that offers the mode says so. It is not the
 * phone's home screen, it cannot host another app's window, and there is no version of Android in
 * which it could.
 *
 * Deliberately quiet. It is on a television across a room, so it carries the two facts a person
 * glancing at it needs — that Flint has the screen, and which phone is driving — at a size that
 * reads from a sofa, and nothing else that would make it a second place to maintain the product's
 * copy.
 */
@Composable
fun SecondScreenContent(deviceName: String) {
    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(FlintColors.Ink)
            .padding(FlintSpace.PageMargin),
        verticalArrangement = Arrangement.spacedBy(FlintSpace.Medium, Alignment.CenterVertically),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        FlintText(
            text = "SECOND SCREEN",
            style = FlintType.LabelSmall.copy(color = FlintColors.Signal),
        )
        FlintText(text = "Flint", style = FlintType.HeadlineLarge)
        FlintText(
            text = deviceName.ifBlank { "This television" },
            style = FlintType.TitleMedium.copy(color = FlintColors.Muted),
        )
        FlintText(
            text = MobileCapabilityAssessor.SECOND_SCREEN_BOUNDARY,
            style = FlintType.BodyMedium.copy(color = FlintColors.Muted),
        )
    }
}
