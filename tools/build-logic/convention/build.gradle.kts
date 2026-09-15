plugins {
    `kotlin-dsl`
}

dependencies {
    // Compiled against, never bundled: the build that applies these plugins already has the Android
    // and Kotlin Gradle plugins on its classpath, at the versions the catalog pins.
    compileOnly(libs.android.gradle.plugin)
    compileOnly(libs.kotlin.gradle.plugin)
}

gradlePlugin {
    plugins {
        register("jvmLibrary") {
            id = "flint.jvm-library"
            implementationClass = "com.rextechnologies.flint.buildlogic.JvmLibraryConventionPlugin"
        }
        register("androidLibrary") {
            id = "flint.android-library"
            implementationClass = "com.rextechnologies.flint.buildlogic.AndroidLibraryConventionPlugin"
        }
        register("androidApplication") {
            id = "flint.android-application"
            implementationClass = "com.rextechnologies.flint.buildlogic.AndroidApplicationConventionPlugin"
        }
        register("compose") {
            id = "flint.compose"
            implementationClass = "com.rextechnologies.flint.buildlogic.ComposeConventionPlugin"
        }
        register("quality") {
            id = "flint.quality"
            implementationClass = "com.rextechnologies.flint.buildlogic.QualityConventionPlugin"
        }
    }
}
