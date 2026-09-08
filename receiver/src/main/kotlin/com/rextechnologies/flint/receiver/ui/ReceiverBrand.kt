package com.rextechnologies.flint.receiver.ui

import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.platform.testTag
import androidx.tv.material3.Button
import androidx.tv.material3.Text
import com.rextechnologies.flint.receiver.ReceiverUiState

@Composable
internal fun ReceiverBackdrop(modifier: Modifier = Modifier) {
    Canvas(modifier) {
        val signal = ReceiverColors.Signal
        val line = ReceiverColors.Line
        drawCircle(
            color = signal.copy(alpha = 0.025f),
            radius = size.minDimension * 0.72f,
            center = Offset(size.width * 0.88f, size.height * 0.56f),
        )
        drawCircle(
            color = ReceiverColors.Ink,
            radius = size.minDimension * 0.56f,
            center = Offset(size.width * 0.88f, size.height * 0.56f),
        )
        repeat(7) { index ->
            val x = size.width * (0.055f + index * 0.014f)
            drawCircle(line.copy(alpha = 0.42f), radius = 1.2f, center = Offset(x, size.height * 0.90f))
        }
        drawLine(
            color = signal.copy(alpha = 0.08f),
            start = Offset(size.width * 0.66f, size.height * 0.08f),
            end = Offset(size.width * 0.96f, size.height * 0.08f),
            strokeWidth = 2f,
        )
    }
}

@Composable
internal fun ReceiverHeader(state: ReceiverUiState, modifier: Modifier = Modifier) {
    Row(
        modifier = modifier.testTag(ReceiverTags.HEADER),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        RexCodeMark()
        Spacer(Modifier.width(14.dp))
        Column(verticalArrangement = Arrangement.spacedBy(1.dp)) {
            Text(
                text = "REX TECHNOLOGIES",
                color = ReceiverColors.Signal,
                fontSize = 11.sp,
                lineHeight = 14.sp,
                fontWeight = FontWeight.Bold,
                letterSpacing = 1.8.sp,
            )
            Text(
                text = "FLINT  /  RECEIVER",
                color = ReceiverColors.Text,
                fontSize = 20.sp,
                lineHeight = 24.sp,
                fontWeight = FontWeight.Bold,
                letterSpacing = 0.2.sp,
            )
        }
        Spacer(Modifier.weight(1f))
        ReceiverStatusBadge(state)
    }
}

@Composable
private fun RexCodeMark() {
    val shape = RoundedCornerShape(11.dp)
    Box(
        modifier = Modifier
            .size(52.dp)
            .background(ReceiverColors.Panel, shape)
            .border(1.dp, ReceiverColors.Line, shape)
            .clearAndSetSemantics { },
        contentAlignment = Alignment.Center,
    ) {
        Canvas(Modifier.size(40.dp)) {
            val dot = ReceiverColors.Signal.copy(alpha = 0.16f)
            repeat(5) { row ->
                repeat(5) { column ->
                    drawCircle(
                        color = dot,
                        radius = 0.8.dp.toPx(),
                        center = Offset(
                            x = 4.dp.toPx() + column * 8.dp.toPx(),
                            y = 4.dp.toPx() + row * 8.dp.toPx(),
                        ),
                    )
                }
            }
        }
        Text(
            text = "REX",
            color = ReceiverColors.Signal,
            fontSize = 11.sp,
            lineHeight = 13.sp,
            fontWeight = FontWeight.Black,
            letterSpacing = 0.5.sp,
        )
    }
}

@Composable
private fun ReceiverStatusBadge(state: ReceiverUiState) {
    val experience = state.idleExperience()
    val (label, color) = when (experience) {
        IdleExperience.CONNECTED -> "PC CONNECTED" to ReceiverColors.Success
        IdleExperience.READY -> "READY TO PAIR" to ReceiverColors.Signal
        IdleExperience.ATTENTION -> "NEEDS ATTENTION" to ReceiverColors.Live
        IdleExperience.NO_NETWORK -> "WAITING FOR NETWORK" to ReceiverColors.Warning
        IdleExperience.STARTING -> "STARTING" to ReceiverColors.Muted
    }
    Row(
        modifier = Modifier
            .testTag(ReceiverTags.STATUS)
            .background(color.copy(alpha = 0.07f), RoundedCornerShape(50))
            .border(1.dp, color.copy(alpha = 0.42f), RoundedCornerShape(50))
            .clearAndSetSemantics {
                contentDescription = "Receiver status: $label"
                liveRegion = LiveRegionMode.Polite
            }
            .padding(horizontal = 15.dp, vertical = 9.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(Modifier.size(8.dp).background(color, CircleShape))
        Spacer(Modifier.width(9.dp))
        Text(
            text = label,
            color = color,
            fontSize = 12.sp,
            lineHeight = 15.sp,
            fontWeight = FontWeight.Bold,
            letterSpacing = 1.2.sp,
            maxLines = 1,
        )
    }
}

@Composable
internal fun TvActionButton(
    label: String,
    enabled: Boolean,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
) {
    var focused by remember { mutableStateOf(false) }
    val shape = RoundedCornerShape(50)
    Button(
        onClick = onClick,
        enabled = enabled,
        // Symmetric padding defines the pill's height rather than a minimum-height modifier. An
        // earlier version set heightIn(min = 54.dp): that grows the button's box, but the content
        // inside is not re-centered within the extra space, so all of it collected below the label
        // and the text read as sitting high in the pill. Padding alone keeps the label centered by
        // construction, and still clears the 48dp minimum focus target a remote needs.
        contentPadding = PaddingValues(horizontal = 24.dp, vertical = 17.dp),
        modifier = modifier
            .onFocusChanged { focused = it.isFocused }
            .border(
                width = if (focused) 3.dp else 1.dp,
                color = when {
                    !enabled -> ReceiverColors.Line
                    focused -> ReceiverColors.Signal
                    else -> Color.Transparent
                },
                shape = shape,
            ),
    ) {
        Text(
            text = label,
            fontSize = 12.sp,
            lineHeight = 15.sp,
            fontWeight = FontWeight.Bold,
            letterSpacing = 1.1.sp,
            maxLines = 1,
            overflow = TextOverflow.Clip,
        )
    }
}

@Composable
internal fun ReceiverSpinner(modifier: Modifier = Modifier, color: Color = ReceiverColors.Signal) {
    val transition = rememberInfiniteTransition(label = "receiver-spinner")
    val rotation by transition.animateFloat(
        initialValue = 0f,
        targetValue = 360f,
        animationSpec = infiniteRepeatable(tween(durationMillis = 950, easing = LinearEasing)),
        label = "receiver-spinner-rotation",
    )
    Canvas(modifier.size(42.dp)) {
        drawCircle(color = ReceiverColors.Line, style = Stroke(width = 3.dp.toPx()))
        drawArc(
            color = color,
            startAngle = rotation,
            sweepAngle = 96f,
            useCenter = false,
            style = Stroke(width = 3.dp.toPx(), cap = StrokeCap.Round),
        )
    }
}

@Composable
internal fun SignalIcon(
    modifier: Modifier = Modifier,
    color: Color = ReceiverColors.Signal,
    success: Boolean = false,
) {
    Canvas(modifier.size(48.dp)) {
        drawRoundRect(
            color = color.copy(alpha = 0.10f),
            cornerRadius = CornerRadius(14.dp.toPx()),
        )
        if (success) {
            val path = androidx.compose.ui.graphics.Path().apply {
                moveTo(size.width * 0.24f, size.height * 0.52f)
                lineTo(size.width * 0.43f, size.height * 0.70f)
                lineTo(size.width * 0.76f, size.height * 0.31f)
            }
            drawPath(
                path = path,
                color = color,
                style = Stroke(width = 4.dp.toPx(), cap = StrokeCap.Round),
            )
        } else {
            drawCircle(
                color = color,
                radius = 3.dp.toPx(),
                center = Offset(size.width / 2f, size.height * 0.72f),
            )
            drawArc(
                color = color,
                startAngle = 220f,
                sweepAngle = 100f,
                useCenter = false,
                topLeft = Offset(size.width * 0.31f, size.height * 0.31f),
                size = androidx.compose.ui.geometry.Size(size.width * 0.38f, size.height * 0.38f),
                style = Stroke(width = 3.dp.toPx(), cap = StrokeCap.Round),
            )
        }
    }
}
