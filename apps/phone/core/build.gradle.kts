plugins {
    id("flint.jvm-library")
}

flint {
    // The copy package is prose. Constructing a verdict marks its whole reason string as covered with
    // no assertion about what it says, so leaving it in inflates the line ratio of every package that
    // has logic in it. The copy has its own tests, about its voice rather than its coverage.
    coverageFloor(line = 0.95, branch = 0.85, excludes = listOf("**/copy/**"))
}

dependencies {
    api(project(":protocol"))
    testImplementation(kotlin("test-junit"))
    testImplementation(libs.junit)
}
