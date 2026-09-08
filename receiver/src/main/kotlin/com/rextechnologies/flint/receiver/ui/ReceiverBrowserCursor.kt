package com.rextechnologies.flint.receiver.ui

import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.dp
import com.rextechnologies.flint.receiver.browser.CursorState

/**
 * The pointer the D-pad steers.
 *
 * Drawn rather than composed: it moves every frame while a direction is held, and a Canvas redraw
 * costs far less than recomposing a positioned layout node sixty times a second on a stick.
 *
 * The shape is a ring with a gap in the middle rather than a filled arrow, because the thing under
 * the cursor is the thing being aimed at and an opaque pointer hides it. It grows and fills on press
 * so a click is visible from across a room, where a two-pixel state change is not.
 */
@Composable
internal fun ReceiverBrowserCursor(
    cursor: CursorState,
    pressed: Boolean,
    visible: Boolean,
) {
    if (!visible) {
        return
    }

    val density = LocalDensity.current
    val radius by animateFloatAsState(
        targetValue = with(density) { (if (pressed) 13.dp else 17.dp).toPx() },
        animationSpec = spring(dampingRatio = 0.6f, stiffness = 1400f),
        label = "cursorRadius",
    )
    val fill by animateFloatAsState(
        targetValue = if (pressed) 0.34f else 0.12f,
        animationSpec = spring(stiffness = 1400f),
        label = "cursorFill",
    )

    // The full-screen Canvas is decorative. Giving it semantics makes Compose treat it as an
    // occluding accessibility node and hides the webpage/chrome underneath from TalkBack.
    Canvas(modifier = Modifier.fillMaxSize()) {
        val centre = Offset(cursor.x.toFloat(), cursor.y.toFloat())
        val stroke = with(density) { 2.5.dp.toPx() }

        // A dark halo first, so the ring stays visible over a white page as well as a dark one. A
        // single-colour cursor disappears on roughly half the web.
        drawCircle(
            color = ReceiverColors.Ink.copy(alpha = 0.45f),
            radius = radius + stroke,
            center = centre,
            style = Stroke(width = stroke * 1.6f),
        )
        drawCircle(color = ReceiverColors.Signal.copy(alpha = fill), radius = radius, center = centre)
        drawCircle(
            color = ReceiverColors.Signal,
            radius = radius,
            center = centre,
            style = Stroke(width = stroke),
        )
        // The centre dot is what someone actually aims with; the ring only makes it findable.
        drawCircle(color = ReceiverColors.Signal, radius = stroke, center = centre)
    }
}
