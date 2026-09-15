package com.rextechnologies.flint.mobile.ui

import com.rextechnologies.flint.castcore.capability.ToneIntent
import com.rextechnologies.flint.design.Tone

/**
 * The one place a semantic tone becomes a design-system tone.
 *
 * Two enums rather than one, because `:castcore` is Android-free and `:design` is a Compose library:
 * neither can depend on the other without dragging something unwanted into it. The `when` below is
 * exhaustive, so adding a member to either enum breaks the build here rather than silently painting
 * a new status the wrong colour.
 */
fun ToneIntent.toDesignTone(): Tone = when (this) {
    ToneIntent.NEUTRAL -> Tone.Neutral
    ToneIntent.SIGNAL -> Tone.Signal
    ToneIntent.LIVE -> Tone.Live
    ToneIntent.LINE -> Tone.Line
}
