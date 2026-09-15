package com.rextechnologies.flint.castcore.copy

import com.rextechnologies.flint.castcore.capability.ToneIntent

/** One bullet in an onboarding step, numbered when the order it is done in matters. */
data class OnboardingPoint(val text: String, val number: Int? = null) {
    init {
        require(text.isNotBlank())
        require(number == null || number > 0)
    }

    /** What goes in the gutter: a number when the order matters, the letterform bullet when it does not. */
    val marker: String
        get() = number?.toString() ?: "—"
}

/** One screen of the introduction. */
data class OnboardingStep(
    val eyebrow: String,
    val title: String,
    val body: String,
    val points: List<String> = emptyList(),
    val tone: ToneIntent = ToneIntent.SIGNAL,
    val ordered: Boolean = false,
) {
    init {
        require(eyebrow.isNotBlank() && title.isNotBlank() && body.isNotBlank())
    }

    val displayPoints: List<OnboardingPoint>
        get() = points.mapIndexed { index, text ->
            OnboardingPoint(text, if (ordered) index + 1 else null)
        }
}

/**
 * The introduction, which is skippable and reachable again from Settings.
 *
 * Step two is the important one and it is where it is on purpose. It carries the failure tone and it
 * states the one fact that decides whether the app can work at all — before asking anybody to turn on
 * a hotspot, join a television to it, or go hunting through Developer Options. The desktop puts its
 * equivalent in the same place for the same reason: finding out on step five that the device in the
 * room can never work is a worse experience than being told on step two.
 *
 * The last step ends by running the probe, so the introduction finishes on an answer rather than on
 * an empty screen.
 */
object OnboardingCopy {
    val steps: List<OnboardingStep> = listOf(
        OnboardingStep(
            eyebrow = "Welcome",
            title = "Your phone is the host",
            body = "Flint turns this phone into the thing that casts and the television into the " +
                "screen it casts to. The phone runs the hotspot, the TV joins it, and everything " +
                "happens over that link.",
            points = listOf(
                "Mirror this screen onto the TV.",
                "Or use the TV as a second screen while the phone stays the controller.",
                "Or hand a video file over and let the TV play it at full quality.",
            ),
        ),
        OnboardingStep(
            eyebrow = "Read this first",
            title = "Check what your Fire TV runs",
            body = "On the TV, open Settings, then My Fire TV, then About. Fire OS 7 and Fire OS 8 " +
                "work. Vega OS cannot: it is not Android, so no app can be installed on it by any " +
                "method, and no future version of Flint will change that.",
            points = listOf(
                "Fire OS 5, 6, 7, 8, 14 and 16 are Android underneath and can run Flint.",
                "Vega OS cannot, and there is nothing to try.",
            ),
            tone = ToneIntent.LIVE,
        ),
        OnboardingStep(
            eyebrow = "On the TV, in this order",
            title = "Join the TV to this phone",
            body = "The phone's hotspot is the network Flint is built around. An access point can " +
                "always reach its own clients, so casting over your own hotspot avoids the setting " +
                "that silently breaks it on somebody else's Wi-Fi.",
            points = listOf(
                "Turn on this phone's hotspot.",
                "On the TV, open Settings, then Network, and join that hotspot.",
                "Open Flint on the TV.",
                "Come back here and let the phone find it.",
            ),
            ordered = true,
        ),
        OnboardingStep(
            eyebrow = "If nothing is found",
            title = "Flint will tell you why",
            body = "It looks several ways, in order, and stops at the first that works. When none " +
                "does, it says which network it was on and what it tried rather than showing an " +
                "empty list.",
            points = listOf(
                "You can always type the TV's address in by hand.",
                "The TV's address is on it, under Settings, My Fire TV, About, Network.",
                "Flint never scans for Wi-Fi networks, so it never asks for location permission.",
            ),
        ),
        OnboardingStep(
            eyebrow = "What the TV shows",
            title = "A second screen is not a second desktop",
            body = "In second-screen mode the TV shows Flint's own screens — a player, photos, what " +
                "is playing now — while the phone stays the controller. Android gives an app no way " +
                "to move another app's window onto a second display, so this is not an extension of " +
                "the phone itself.",
            points = listOf(
                "Mirror shows everything on this screen, and asks permission each time.",
                "Second screen shows only what Flint draws, and asks for nothing.",
            ),
        ),
        OnboardingStep(
            eyebrow = "What works today",
            title = "Flint says what it can prove",
            body = "Every mode is judged against this phone, this network and this television, and " +
                "reported with one of four words. A mode that is blocked tells you what to do. A mode " +
                "that is not possible tells you why and offers nothing, because offering a remedy for " +
                "an impossibility would be a lie.",
            points = listOf(
                "Ready means it has been checked and it works.",
                "Blocked means something can be changed.",
                "Not possible means this pair can never do it.",
                "Coming soon means it is not built yet, and nothing on either device will help.",
            ),
        ),
    )

    /** The last step ends on the probe, so the introduction finishes with an answer on screen. */
    const val FINAL_ACTION: String = "Find my TV"

    const val SKIP_ACTION: String = "Skip"
}
