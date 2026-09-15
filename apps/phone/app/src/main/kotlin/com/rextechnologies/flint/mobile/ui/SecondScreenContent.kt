package com.rextechnologies.flint.mobile.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import com.rextechnologies.flint.castcore.screen.SceneCopy
import com.rextechnologies.flint.castcore.screen.SecondScreenScene
import com.rextechnologies.flint.design.FlintColors
import com.rextechnologies.flint.design.FlintShapes
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
 * One composable per scene, and nothing here decides which scene is showing: that is the
 * coordinator's, so the cockpit on the phone and the picture on the television are two views of one
 * value rather than two things kept in step by hand.
 */
@Composable
fun SecondScreenContent(scene: SecondScreenScene) {
    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(FlintColors.Ink)
            .padding(FlintSpace.Huge),
        verticalArrangement = Arrangement.spacedBy(FlintSpace.Medium, Alignment.CenterVertically),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        FlintText(text = SceneCopy.EYEBROW, style = FlintType.LabelSmall.copy(color = FlintColors.Signal))
        when (scene) {
            is SecondScreenScene.Dashboard -> Dashboard(scene)
            is SecondScreenScene.NowPlaying -> NowPlaying(scene)
        }
    }
}

@Composable
private fun Dashboard(scene: SecondScreenScene.Dashboard) {
    FlintText(text = "Flint", style = FlintType.HeadlineLarge)
    FlintText(text = scene.televisionLabel, style = FlintType.TitleMedium.copy(color = FlintColors.Muted))
    Spacer(Modifier.height(FlintSpace.Small))
    Row(horizontalArrangement = Arrangement.spacedBy(FlintSpace.XLarge)) {
        Fact(label = SceneCopy.DRIVEN_BY, value = scene.phoneLabel)
        Fact(label = SceneCopy.LINK, value = scene.linkWord)
    }
    Spacer(Modifier.height(FlintSpace.Small))
    FlintText(text = SceneCopy.BOUNDARY, style = FlintType.BodyMedium.copy(color = FlintColors.Muted))
}

@Composable
private fun NowPlaying(scene: SecondScreenScene.NowPlaying) {
    FlintText(text = scene.mediaLabel, style = FlintType.HeadlineLarge)
    FlintText(text = scene.stateWord, style = FlintType.TitleMedium.copy(color = FlintColors.Muted))
    Spacer(Modifier.height(FlintSpace.Small))
    // A bar only once the receiver has said how long the file is; a bar with no end is a guess.
    scene.progress?.let { fraction ->
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .height(FlintSpace.Small)
                .background(FlintColors.Raised, FlintShapes.Small),
        ) {
            Box(
                modifier = Modifier
                    .fillMaxWidth(fraction.toFloat())
                    .height(FlintSpace.Small)
                    .background(FlintColors.Signal, FlintShapes.Small),
            )
        }
    }
    FlintText(text = scene.clock, style = FlintType.Readout)
}

@Composable
private fun Fact(label: String, value: String) {
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        FlintText(text = label.uppercase(), style = FlintType.LabelSmall.copy(color = FlintColors.Muted))
        FlintText(text = value, style = FlintType.TitleMedium)
    }
}
