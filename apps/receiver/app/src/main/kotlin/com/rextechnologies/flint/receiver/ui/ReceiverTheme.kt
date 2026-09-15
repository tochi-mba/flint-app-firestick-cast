package com.rextechnologies.flint.receiver.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.PlatformTextStyle
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.style.LineHeightStyle
import androidx.tv.material3.LocalTextStyle
import androidx.tv.material3.MaterialTheme
import androidx.tv.material3.darkColorScheme

object ReceiverColors {
    val Ink = Color(0xFF080A09)
    val Panel = Color(0xFF111512)
    val Raised = Color(0xFF181E19)
    val Line = Color(0xFF29302A)
    val Text = Color(0xFFF2F5EE)
    val Muted = Color(0xFFA3ABA0)
    val Signal = Color(0xFFD7FF3F)
    val Live = Color(0xFFFF774D)
    val Success = Color(0xFF73E9A4)
    val Warning = Color(0xFFFFC857)
    val Scrim = Color(0xE6080A09)
}

private val ReceiverScheme = darkColorScheme(
    primary = ReceiverColors.Signal,
    onPrimary = ReceiverColors.Ink,
    secondary = ReceiverColors.Live,
    background = ReceiverColors.Ink,
    onBackground = ReceiverColors.Text,
    surface = ReceiverColors.Panel,
    onSurface = ReceiverColors.Text,
)

/**
 * Legacy Android font metrics reserve extra space above a glyph's ascent — a leftover from
 * pre-Lollipop text rendering that Compose still defaults to for backward compatibility. Every
 * button and label in this app sets its own explicit `fontSize`/`lineHeight` rather than drawing
 * from a theme scale, and each one inherited that lopsided padding: short, bold, all-caps labels
 * in small pill buttons made it obvious, reading as text sitting noticeably above the visual
 * center of its container rather than centered in it. Centering the line box around the glyphs
 * and dropping the legacy padding here, once, fixes every `Text()` call in the app — none of them
 * override `style` wholesale, so all of them inherit this through `LocalTextStyle`.
 */
private val CenteredTextStyle = TextStyle(
    platformStyle = PlatformTextStyle(includeFontPadding = false),
    lineHeightStyle = LineHeightStyle(
        alignment = LineHeightStyle.Alignment.Center,
        trim = LineHeightStyle.Trim.Both,
    ),
)

@Composable
fun ReceiverTheme(content: @Composable () -> Unit) {
    MaterialTheme(colorScheme = ReceiverScheme) {
        CompositionLocalProvider(
            LocalTextStyle provides LocalTextStyle.current.merge(CenteredTextStyle),
            content = content,
        )
    }
}
