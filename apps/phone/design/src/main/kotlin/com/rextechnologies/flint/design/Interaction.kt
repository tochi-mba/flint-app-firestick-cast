package com.rextechnologies.flint.design

import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.interaction.collectIsHoveredAsState
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.composed
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.semantics.Role

/**
 * The REX touch state.
 *
 * The receiver's `tvFocus` and `tvClickable` are D-pad affordances — a focus halo and a lift — and
 * neither means anything under a thumb. The nearest precedent that does is the desktop's, which drops
 * a control to 0.88 opacity on hover and 0.72 on press, so those are the numbers rather than a new
 * pair invented for this surface.
 *
 * Focus is still honoured, because a phone can have a keyboard attached and a switch-access user has
 * nothing else to go on.
 *
 * Opacity rather than scale on purpose: a scale animation is motion, and motion is the thing a
 * reduced-motion setting asks for less of. This reads identically whether or not animations are on.
 */
fun Modifier.flintClickable(
    enabled: Boolean = true,
    role: Role? = Role.Button,
    onClickLabel: String? = null,
    onClick: () -> Unit,
): Modifier = composed {
    val interaction = remember { MutableInteractionSource() }
    val pressed by interaction.collectIsPressedAsState()
    val hovered by interaction.collectIsHoveredAsState()
    val focused by interaction.collectIsFocusedAsState()

    this
        .alpha(
            when {
                !enabled -> DISABLED_ALPHA
                pressed -> PRESSED_ALPHA
                hovered || focused -> HOVERED_ALPHA
                else -> 1f
            },
        )
        .clickable(
            interactionSource = interaction,
            // Material's ripple is invisible against this palette and would be the app's only
            // Material dependency. The opacity above is the affordance.
            indication = null,
            enabled = enabled,
            role = role,
            onClickLabel = onClickLabel,
            onClick = onClick,
        )
}

/** What a disabled control looks like: present, legible, obviously not for pressing. */
const val DISABLED_ALPHA: Float = 0.45f

private const val PRESSED_ALPHA = 0.72f
private const val HOVERED_ALPHA = 0.88f
