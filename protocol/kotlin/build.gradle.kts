plugins {
    id("flint.jvm-library")
}

flint {
    coverageFloor(
        line = 0.95,
        branch = 0.85,
        // The socket is an Android/Linux adapter. A Windows JVM cannot bind a multicast group
        // address, so its real-I/O test skips here and the pure DNS/subnet decisions remain covered.
        excludes = listOf("**/MulticastDnsSocket*"),
    )
}

dependencies {
    testImplementation(kotlin("test-junit"))
    testImplementation(libs.junit)
}
