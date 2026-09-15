package com.rextechnologies.flint.buildlogic

import org.gradle.api.Project
import org.gradle.api.provider.Property
import javax.inject.Inject

/** The `flint { }` block: the few decisions a module makes that the conventions cannot make for it. */
abstract class FlintExtension @Inject constructor(private val project: Project) {
    /**
     * Whether a `Color(0x…)` literal in this module's production code fails the build.
     *
     * True in an app that takes its palette from a design module, where a literal is a token that has
     * escaped it. False by default, because design modules are where colours are declared.
     */
    abstract val forbidColourLiterals: Property<Boolean>

    /**
     * Holds this module's tests to a coverage floor, which `check` verifies.
     *
     * For pure JVM modules only. Over Robolectric, JaCoCo measures the framework as much as the code,
     * so an Android module's number would describe the harness rather than the tests.
     *
     * @param excludes class-file patterns left out of the measurement, each for a reason stated where
     *   it is passed.
     */
    fun coverageFloor(line: Double, branch: Double, excludes: List<String> = emptyList()) {
        project.configureCoverageFloor(line, branch, excludes)
    }
}
