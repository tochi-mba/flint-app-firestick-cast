package com.rextechnologies.flint.receiver.ui

import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.composed
import androidx.compose.ui.draw.scale
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.LineHeightStyle
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.foundation.shape.RoundedCornerShape

/**
 * The receiver's shared measurements.
 *
 * Colour already had a home in [ReceiverColors]; nothing else did. Every `Text` picked its own size,
 * radii were chosen per component, and the overscan fraction was written out separately in four
 * files. A browser adds an omnibar, a tab strip, a keyboard and a menu to that, which is more
 * surface than ad-hoc numbers survive.
 *
 * Sizes assume a viewer roughly ten feet from a 1080p panel. Amazon's floor for TV text is 14sp;
 * body text here is deliberately larger, because a browser asks people to read rather than scan.
 */
object ReceiverType {
    val Display = TextStyle(fontSize = 34.sp, lineHeight = 40.sp, fontWeight = FontWeight.Bold)
    val Title = TextStyle(fontSize = 22.sp, lineHeight = 28.sp, fontWeight = FontWeight.Bold)
    val Body = TextStyle(fontSize = 18.sp, lineHeight = 24.sp)
    val BodyStrong = TextStyle(fontSize = 18.sp, lineHeight = 24.sp, fontWeight = FontWeight.Bold)
    val Caption = TextStyle(fontSize = 15.sp, lineHeight = 20.sp)

    /** Small upper-case caption. Tracking is what keeps it readable at this size across a room. */
    val Label = TextStyle(
        fontSize = 13.sp,
        lineHeight = 16.sp,
        fontWeight = FontWeight.Bold,
        letterSpacing = 1.2.sp,
    )

    /** Trims the leading a font reserves above and below, so chrome rows measure predictably. */
    val TvLineHeight = LineHeightStyle(
        alignment = LineHeightStyle.Alignment.Center,
        trim = LineHeightStyle.Trim.Both,
    )
}

/** One spacing ramp, so chrome rows line up without each component inventing its own gaps. */
object ReceiverSpace {
    val Hairline: Dp = 1.dp
    val Tiny: Dp = 4.dp
    val Small: Dp = 8.dp
    val Compact: Dp = 12.dp
    val Medium: Dp = 16.dp
    val Large: Dp = 24.dp
    val XLarge: Dp = 32.dp
    val Huge: Dp = 48.dp
    val TouchTarget: Dp = 48.dp
}

object ReceiverShapes {
    val Small: Shape = RoundedCornerShape(8.dp)
    val Medium: Shape = RoundedCornerShape(14.dp)
    val Large: Shape = RoundedCornerShape(20.dp)
    val Pill: Shape = RoundedCornerShape(50)
}

/**
 * The safe area a television will actually display.
 *
 * Amazon asks for the outer 5% of every edge to be treated as unreliable. It was previously written
 * as a bare `0.05f` in each surface that remembered to, which is exactly the kind of number that
 * drifts.
 */
object ReceiverOverscan {
    const val SAFE_FRACTION: Float = 0.05f

    /** The fraction of each axis that is safe to draw in. */
    const val CONTENT_FRACTION: Float = 1f - SAFE_FRACTION * 2f
}

/**
 * Focus ring + lift for a control that is already focusable.
 *
 * Prefer [tvClickable] for buttons. This must sit *before* the focus target in the modifier
 * chain (e.g. `.tvFocus().focusable()`), otherwise the halo never lights up.
 */
fun Modifier.tvFocus(
    shape: Shape = ReceiverShapes.Pill,
    enabled: Boolean = true,
): Modifier = composed {
    var focused by remember { mutableStateOf(false) }
    val lift by animateFloatAsState(
        targetValue = if (focused && enabled) 1.06f else 1f,
        animationSpec = spring(dampingRatio = 0.7f, stiffness = 900f),
        label = "tvFocusLift",
    )
    this
        .onFocusChanged { focused = it.isFocused }
        .scale(lift)
        .border(
            width = if (focused && enabled) 3.dp else ReceiverSpace.Hairline,
            color = when {
                !enabled -> ReceiverColors.Line
                focused -> ReceiverColors.Signal
                else -> ReceiverColors.Line
            },
            shape = shape,
        )
}

/**
 * One focusable click target with TV halo + press squash.
 *
 * Uses a single [clickable] focus target (no nested [focusable]) and reads focus/press from that
 * same [MutableInteractionSource], so Select still fires while the ring and click animation work
 * on Fire TV where Material ripple indication is effectively invisible.
 */
fun Modifier.tvClickable(
    enabled: Boolean = true,
    shape: Shape = ReceiverShapes.Pill,
    role: Role? = Role.Button,
    onClick: () -> Unit,
): Modifier = composed {
    val interaction = remember { MutableInteractionSource() }
    val focused by interaction.collectIsFocusedAsState()
    val pressed by interaction.collectIsPressedAsState()
    val lift by animateFloatAsState(
        targetValue = when {
            !enabled -> 1f
            pressed -> 0.94f
            focused -> 1.06f
            else -> 1f
        },
        animationSpec = spring(dampingRatio = 0.7f, stiffness = 900f),
        label = "tvClickLift",
    )
    this
        .scale(lift)
        .border(
            width = if (focused && enabled) 3.dp else ReceiverSpace.Hairline,
            color = when {
                !enabled -> ReceiverColors.Line
                focused -> ReceiverColors.Signal
                else -> ReceiverColors.Line
            },
            shape = shape,
        )
        .clickable(
            interactionSource = interaction,
            indication = null,
            enabled = enabled,
            role = role,
            onClick = onClick,
        )
}

/** Focus state without the visual, for surfaces that draw their own selection. */
@Composable
fun rememberFocusFlag(): MutableInteractionSource = remember { MutableInteractionSource() }
