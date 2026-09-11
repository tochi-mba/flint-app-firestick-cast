package com.rextechnologies.flint.castcore.copy

/** The Diagnostics sheet. Rows, never prose, and never a number nobody measured. */
object DiagnosticsCopy {
    val empty: EmptyStateCopy = EmptyStateCopy(
        glyph = "—",
        title = "Nothing measured yet",
        body = "Run a probe from the Cast tab and the results appear here.",
    )

    const val SECTION_PHONE: String = "This phone"
    const val SECTION_RECEIVER: String = "Receiver"
    const val SECTION_PATH: String = "Network path"

    /**
     * Why the round trip is the phone's own measurement.
     *
     * The receiver reports a round-trip time of zero in every STATS frame because it never measures
     * one. Passing that through would put a zero on a diagnostics row, which reads as an
     * extraordinarily good link rather than as no measurement at all.
     */
    const val ROUND_TRIP_SOURCE: String =
        "Measured by this phone when it connects. The television does not measure a round trip and " +
            "reports zero, which is not a reading."
}
