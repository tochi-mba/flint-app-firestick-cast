package com.rextechnologies.flint.buildlogic

import com.android.build.api.dsl.CommonExtension
import org.gradle.api.JavaVersion
import org.gradle.api.Project
import org.gradle.kotlin.dsl.withType
import org.jetbrains.kotlin.gradle.tasks.KotlinCompilationTask

/**
 * What every Android module shares.
 *
 * A module's own build file then says only what is true of that module: its namespace, its minimum
 * SDK, its signing and its dependencies.
 */
internal fun Project.configureAndroid(android: CommonExtension) {
    android.compileSdk = libs.version("compile-sdk").toInt()
    android.defaultConfig.testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    android.compileOptions.sourceCompatibility = JavaVersion.toVersion(javaVersion)
    android.compileOptions.targetCompatibility = JavaVersion.toVersion(javaVersion)

    // Off unless a module turns it on. Generating BuildConfig adds a Javac task for no source, and
    // that task's JDK classpath cleanup is unreliable on Windows.
    android.buildFeatures.buildConfig = false

    // Both report that something newer has been published, so the same commit could pass one day and
    // fail the next. Dependabot proposes upgrades instead, as pull requests that can be reviewed.
    android.lint.disable += setOf("GradleDependency", "OutdatedLibrary")

    android.testOptions.unitTests.apply {
        // Robolectric needs the merged resources and the manifest to inflate anything, and the snapshot
        // tests render real themed Compose rather than a stand-in.
        isIncludeAndroidResources = true
        // Framework calls in code under test return defaults instead of throwing, so a plain Log.i on
        // a path a test happens to reach does not fail it as "not mocked".
        isReturnDefaultValues = true
        // Robolectric instruments the platform reflectively, and on JDK 17 that needs the module system
        // opened to it. Without java.net in particular, a Robolectric class earlier in the same worker
        // leaves JSSE unable to resolve a peer address, and a correct TLS test fails only in the suite.
        all { test -> test.jvmArgs(ROBOLECTRIC_OPENS) }
    }

    // Kotlin was the one language here without a warnings-as-errors gate, while Rust runs clippy with
    // -D warnings and .NET sets TreatWarningsAsErrors. A deprecation that compiles quietly is one that
    // gets discovered on a television.
    tasks.withType<KotlinCompilationTask<*>>().configureEach {
        compilerOptions.allWarningsAsErrors.set(true)
    }
}

/**
 * Unit tests run against the debug variant only.
 *
 * Release differs from debug by R8 and by signing, neither of which a unit test exercises, so running
 * the whole Robolectric suite a second time for release doubled the time and proved nothing new.
 */
internal const val UNIT_TESTED_BUILD_TYPE_SKIPPED = "release"

private val ROBOLECTRIC_OPENS = listOf(
    "--add-opens=java.base/java.net=ALL-UNNAMED",
    "--add-opens=java.base/java.lang=ALL-UNNAMED",
    "--add-opens=java.base/java.util=ALL-UNNAMED",
    "--add-opens=java.base/java.io=ALL-UNNAMED",
    "--add-opens=java.base/java.security=ALL-UNNAMED",
    "--add-opens=java.base/javax.net.ssl=ALL-UNNAMED",
)
