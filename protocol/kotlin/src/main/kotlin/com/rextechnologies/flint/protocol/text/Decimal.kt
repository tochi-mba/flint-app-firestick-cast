package com.rextechnologies.flint.protocol.text

import kotlin.math.abs
import kotlin.math.roundToLong

/**
 * Numbers formatted for people, without a locale.
 *
 * `String.format` puts a comma in for half the world, and then the number in a sentence does not
 * match the number in the row above it. This existed three times over — once for throughput, once
 * for a package size, once for a bitrate — and every copy carried the same two defects: a large
 * value saturated `Long` and printed something like `922337203685477580.7`, and a negative one
 * printed `-1.-2` because the sign came out of the remainder as well as the quotient.
 */
object Decimal {
    /**
     * The largest magnitude worth printing.
     *
     * Well above any real throughput, bitrate or file size, and far enough below the point at which
     * `round(value * 10)` stops being exact that the output is always the number that went in.
     */
    const val MAXIMUM_MAGNITUDE: Double = 1_000_000_000.0

    /** One decimal place. */
    fun oneDecimal(value: Double): String {
        val scaled = roundedMagnitude(value, "decimal", scale = 10.0)
        val sign = if (value < 0 && scaled != 0L) "-" else ""
        return "$sign${scaled / 10}.${scaled % 10}"
    }

    /** No decimal places, for a threshold quoted in a sentence beside a measurement. */
    fun whole(value: Double): String {
        val magnitude = roundedMagnitude(value, "whole", scale = 1.0)
        val sign = if (value < 0 && magnitude != 0L) "-" else ""
        return "$sign$magnitude"
    }

    /**
     * The magnitude, scaled and rounded half away from zero.
     *
     * Rounding is done on the magnitude and the sign put back afterwards because that is the rule a
     * person expects from a printed measurement: 0.5 shows as 1 and -0.5 as -1. Kotlin's `round` is
     * `Math.rint`, which rounds a tie to the nearest even integer instead -- so 0.5 would show as 0,
     * 1.5 as 2 and 2.5 as 2 again, and three rows of a diagnostics panel would disagree about what
     * rounding means. Sign is taken from the input rather than the result so that a value which
     * rounds down to zero prints "0" and never "-0".
     */
    private fun roundedMagnitude(value: Double, form: String, scale: Double): Long {
        require(value.isFinite()) { "A value that is not a number has no $form form" }
        val clamped = abs(value).coerceAtMost(MAXIMUM_MAGNITUDE)
        return (clamped * scale).roundToLong()
    }
}
