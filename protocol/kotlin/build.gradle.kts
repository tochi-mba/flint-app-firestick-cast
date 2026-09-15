plugins {
    id("flint.jvm-library")
}

flint {
    coverageFloor(line = 0.95, branch = 0.85)
}

dependencies {
    testImplementation(kotlin("test-junit"))
    testImplementation(libs.junit)
}
