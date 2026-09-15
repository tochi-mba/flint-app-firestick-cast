package com.rextechnologies.flint.buildlogic

import org.gradle.api.Plugin
import org.gradle.api.Project
import org.gradle.api.tasks.JavaExec
import org.gradle.api.tasks.PathSensitivity
import org.gradle.kotlin.dsl.create
import org.gradle.kotlin.dsl.register

/**
 * `flint.quality`: ktlint and the socket-binding guard, wired into `check` for every Kotlin module.
 *
 * Every module gets the same gates. They used to be applied module by module, which left the receiver,
 * the module with the most sockets in the repository, with none of them.
 */
class QualityConventionPlugin : Plugin<Project> {
    override fun apply(target: Project) {
        with(target) {
            val flint = extensions.create<FlintExtension>("flint")
            flint.forbidColourLiterals.convention(false)

            val ktlintDependencies = configurations.dependencyScope("ktlint")
            val ktlintClasspath = configurations.resolvable("ktlintClasspath") {
                extendsFrom(ktlintDependencies.get())
            }
            dependencies.add(ktlintDependencies.name, libs.library("ktlint-cli"))

            val ktlintCheck = tasks.register<KtlintCheckTask>("ktlintCheck") {
                group = "verification"
                description = "Checks the layout of this module's Kotlin."
                setClasspath(files(ktlintClasspath))
                mainClass.set(KTLINT_MAIN)
                // The glob is relative to the process's working directory, which Gradle does not
                // otherwise promise is this module.
                setWorkingDir(projectDir)
                args("src/**/*.kt", "--reporter=plain", "--relative")
                // Declared so Gradle skips the check when nothing it reads has changed, and cannot skip
                // it when something has.
                inputs.files(fileTree("src") { include("**/*.kt") }).withPathSensitivity(PathSensitivity.RELATIVE)
                inputs.file(rootProject.file(".editorconfig"))
                marker.set(layout.buildDirectory.file("ktlint/passed.txt"))
            }

            tasks.register<JavaExec>("ktlintFormat") {
                group = "formatting"
                description = "Rewrites the layout of this module's Kotlin to what ktlintCheck expects."
                setClasspath(files(ktlintClasspath))
                mainClass.set(KTLINT_MAIN)
                setWorkingDir(projectDir)
                args("-F", "src/**/*.kt", "--relative")
            }

            val socketBindingGuard = tasks.register<SocketBindingGuardTask>("socketBindingGuard") {
                group = "verification"
                description = "Fails when a socket binds a wildcard, or an address is written down."
                // Production sources only. A test is entitled to write an address down, since naming one
                // is how it states its case, and the rule is about what ships.
                sources.from(fileTree("src/main") { include("**/*.kt") })
                forbidColourLiterals.set(flint.forbidColourLiterals)
                permittedAddresses.set(PERMITTED_ADDRESSES)
                modulePath.set(path)
                marker.set(layout.buildDirectory.file("source-guard/binding.ok"))
            }

            tasks.matching { it.name == "check" }.configureEach {
                dependsOn(ktlintCheck, socketBindingGuard)
            }
        }
    }

    private companion object {
        const val KTLINT_MAIN = "com.pinterest.ktlint.Main"

        /** Multicast groups, which are protocol constants, and loopback. */
        val PERMITTED_ADDRESSES = setOf("224.0.0.251", "239.255.255.250", "127.0.0.1")
    }
}
