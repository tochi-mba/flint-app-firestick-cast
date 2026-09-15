package com.rextechnologies.flint.buildlogic

import com.android.build.api.dsl.ApplicationExtension
import com.android.build.api.variant.ApplicationAndroidComponentsExtension
import com.android.build.api.variant.HostTestBuilder
import org.gradle.api.Plugin
import org.gradle.api.Project
import org.gradle.kotlin.dsl.configure
import org.gradle.kotlin.dsl.getByType

/**
 * `flint.android-application`: an installable app, versioned from VERSION.
 *
 * The version name is VERSION at the repository root. The release workflow passes
 * `flint.versionSuffix`, a short commit sha on anything that is not a tag build, and
 * `flint.versionCode`, taken from the run number, so a rolling build is always distinguishable from
 * the tagged release it came after.
 */
class AndroidApplicationConventionPlugin : Plugin<Project> {
    override fun apply(target: Project) {
        with(target) {
            pluginManager.apply("com.android.application")
            pluginManager.apply(QualityConventionPlugin::class.java)

            val android = extensions.getByType<ApplicationExtension>()
            configureAndroid(android)

            android.defaultConfig.targetSdk = libs.version("target-sdk").toInt()
            android.defaultConfig.versionName =
                flintVersion + providers.gradleProperty("flint.versionSuffix").getOrElse("")
            android.defaultConfig.versionCode =
                providers.gradleProperty("flint.versionCode").map(String::toInt).getOrElse(1)

            android.buildTypes {
                getByName("debug") {
                    // Installable beside a release build of the same app, and never mistaken for one.
                    applicationIdSuffix = ".debug"
                    versionNameSuffix = "-debug"
                }
                getByName("release") {
                    isMinifyEnabled = true
                    isShrinkResources = true
                    proguardFiles(
                        android.getDefaultProguardFile("proguard-android-optimize.txt"),
                        "proguard-rules.pro",
                    )
                }
            }

            extensions.configure<ApplicationAndroidComponentsExtension> {
                beforeVariants(selector().withBuildType(UNIT_TESTED_BUILD_TYPE_SKIPPED)) { variant ->
                    variant.hostTests[HostTestBuilder.UNIT_TEST_TYPE]?.enable = false
                }
            }
        }
    }
}
