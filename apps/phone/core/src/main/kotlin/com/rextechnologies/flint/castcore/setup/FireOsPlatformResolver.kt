package com.rextechnologies.flint.castcore.setup

import com.rextechnologies.flint.castcore.capability.ReceiverPlatform

/**
 * Turns what an ADB probe proved into a [ReceiverPlatform].
 *
 * A port of the Windows host's `FireTvPlatformResolver`, rule for rule, so the two products cannot
 * come to different conclusions about the same television. Pure and deliberately conservative:
 * every path that lacks proof answers [ReceiverPlatform.UNKNOWN] rather than the likelier answer,
 * because the whole value of the prober is that it does not guess.
 */
object FireOsPlatformResolver {
    /**
     * Build models Amazon documents as shipping with Vega OS rather than Android.
     *
     * Exact identifiers only. A product nickname containing a word such as "Select" is not strong
     * enough evidence for a verdict that offers no remedy.
     */
    val KNOWN_VEGA_BUILD_MODELS: Set<String> = setOf(
        // Fire TV Stick 4K Select (2025)
        "AFTCA002",
        // Fire TV Stick HD, second generation (2026)
        "AFTCL001",
    )

    /**
     * @param provesAndroid whether the probe reached an Android shell at all.
     * @param apiLevel the API level read from `ro.build.version.sdk`, or `null` when it could not be.
     * @param buildModel `ro.product.model`, or `null`.
     */
    fun resolve(provesAndroid: Boolean, apiLevel: Int?, buildModel: String?): ReceiverPlatform {
        if (buildModel != null && KNOWN_VEGA_BUILD_MODELS.any { it.equals(buildModel.trim(), ignoreCase = true) }) {
            return ReceiverPlatform.VEGA
        }
        // Silence is ambiguous: it could be Vega, or Fire OS with debugging off. Say so.
        if (!provesAndroid) return ReceiverPlatform.UNKNOWN

        // Amazon publishes these API-level ranges for Fire OS. A level outside the documented set
        // stays unknown rather than being rounded into the nearest generation.
        return when (apiLevel) {
            22 -> ReceiverPlatform.FIRE_OS_5
            25 -> ReceiverPlatform.FIRE_OS_6
            28 -> ReceiverPlatform.FIRE_OS_7
            29, 30 -> ReceiverPlatform.FIRE_OS_8
            in 31..34 -> ReceiverPlatform.FIRE_OS_14
            35, 36 -> ReceiverPlatform.FIRE_OS_16
            else -> ReceiverPlatform.UNKNOWN
        }
    }
}
