package com.rextechnologies.flint.buildlogic

import org.gradle.api.Project
import org.gradle.api.tasks.testing.Test
import org.gradle.kotlin.dsl.configure
import org.gradle.kotlin.dsl.named
import org.gradle.testing.jacoco.plugins.JacocoPluginExtension
import org.gradle.testing.jacoco.tasks.JacocoCoverageVerification
import org.gradle.testing.jacoco.tasks.JacocoReport

/** JaCoCo reports on every test run, and a line and branch floor that `check` fails below. */
internal fun Project.configureCoverageFloor(line: Double, branch: Double, excludes: List<String>) {
    pluginManager.apply("jacoco")
    extensions.configure<JacocoPluginExtension> {
        toolVersion = libs.version("jacoco")
    }

    val report = tasks.named<JacocoReport>("jacocoTestReport") {
        dependsOn(tasks.named("test"))
        reports {
            xml.required.set(true)
            html.required.set(true)
            csv.required.set(false)
        }
    }
    tasks.named<Test>("test") {
        finalizedBy(report)
    }

    val verification = tasks.named<JacocoCoverageVerification>("jacocoTestCoverageVerification") {
        dependsOn(tasks.named("test"))
        if (excludes.isNotEmpty()) {
            classDirectories.setFrom(
                files(classDirectories.files.map { directory -> fileTree(directory) { exclude(excludes) } }),
            )
        }
        violationRules {
            rule {
                element = "BUNDLE"
                limit {
                    counter = "LINE"
                    value = "COVEREDRATIO"
                    minimum = line.toString().toBigDecimal()
                }
                limit {
                    counter = "BRANCH"
                    value = "COVEREDRATIO"
                    minimum = branch.toString().toBigDecimal()
                }
            }
        }
    }
    tasks.named("check") {
        dependsOn(verification)
    }
}
