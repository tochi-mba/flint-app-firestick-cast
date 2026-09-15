package com.rextechnologies.flint.buildlogic

import com.android.build.api.dsl.LibraryExtension
import com.android.build.api.variant.HostTestBuilder
import com.android.build.api.variant.LibraryAndroidComponentsExtension
import org.gradle.api.Plugin
import org.gradle.api.Project
import org.gradle.kotlin.dsl.configure
import org.gradle.kotlin.dsl.getByType

/** `flint.android-library`: an Android module another module depends on. */
class AndroidLibraryConventionPlugin : Plugin<Project> {
    override fun apply(target: Project) {
        with(target) {
            pluginManager.apply("com.android.library")
            pluginManager.apply(QualityConventionPlugin::class.java)

            configureAndroid(extensions.getByType<LibraryExtension>())

            extensions.configure<LibraryAndroidComponentsExtension> {
                beforeVariants(selector().withBuildType(UNIT_TESTED_BUILD_TYPE_SKIPPED)) { variant ->
                    variant.hostTests[HostTestBuilder.UNIT_TEST_TYPE]?.enable = false
                }
            }
        }
    }
}
