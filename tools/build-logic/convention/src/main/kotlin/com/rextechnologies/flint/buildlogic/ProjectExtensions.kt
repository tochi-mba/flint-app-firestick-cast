package com.rextechnologies.flint.buildlogic

import org.gradle.api.Project
import org.gradle.api.artifacts.MinimalExternalModuleDependency
import org.gradle.api.artifacts.VersionCatalog
import org.gradle.api.artifacts.VersionCatalogsExtension
import org.gradle.api.provider.Provider
import org.gradle.kotlin.dsl.getByType

/** The JDK every Kotlin and Java compilation targets. */
internal const val JAVA_VERSION = 17

/** gradle/libs.versions.toml, where every version in the build is written. */
internal val Project.libs: VersionCatalog
    get() = extensions.getByType<VersionCatalogsExtension>().named("libs")

internal fun VersionCatalog.version(alias: String): String =
    findVersion(alias)
        .orElseThrow { IllegalStateException("gradle/libs.versions.toml has no version named '$alias'.") }
        .requiredVersion

internal fun VersionCatalog.library(alias: String): Provider<MinimalExternalModuleDependency> =
    findLibrary(alias)
        .orElseThrow { IllegalStateException("gradle/libs.versions.toml has no library named '$alias'.") }

/**
 * VERSION at the repository root, the one version every build reads.
 *
 * Read through a provider so the configuration cache tracks the file as an input.
 */
internal val Project.flintVersion: String
    get() = providers.fileContents(rootProject.layout.projectDirectory.file("VERSION")).asText.get().trim()
