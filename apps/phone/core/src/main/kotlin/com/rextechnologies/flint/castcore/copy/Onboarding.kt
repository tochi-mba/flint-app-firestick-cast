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
 * Three steps, because the introduction is what stands between a person and Find my TV. What else
 * they might need — a hidden network, what each mode can do — is on the screen where the need
 * arises rather than in a walkthrough they must read first.
 *
 * Step two is the important one and it is where it is on purpose. It carries the failure tone and it
 * states the one fact that decides whether the app can work at all — before asking anybody to turn on
 * a hotspot or join a television to it. The desktop puts its equivalent in the same place for the
 * same reason: finding out on the last step that the device in the room can never work is a worse
 * experience than being told on step two.
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
    )

    /** The last step ends on the probe, so the introduction finishes with an answer on screen. */
    const val FINAL_ACTION: String = "Find my TV"

    const val SKIP_ACTION: String = "Skip"
}
