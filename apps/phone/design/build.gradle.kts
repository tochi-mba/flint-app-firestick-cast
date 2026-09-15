plugins {
    id("com.android.library")
    id("org.jetbrains.kotlin.plugin.compose")
}

android {
    namespace = "com.rextechnologies.flint.design"
    compileSdk = libs.versions.compile.sdk.get().toInt()

    defaultConfig {
        // The receiver's floor, not the phone's. :design has no API requirement above 21; holding it
        // at the lower of the two apps is what lets the Fire TV app adopt these tokens later without
        // the module itself being the reason it cannot.
        minSdk = libs.versions.receiver.min.sdk.get().toInt()
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    buildFeatures {
        compose = true
        // Nothing in :design reads BuildConfig, and generating it adds a Javac task for no source.
        buildConfig = false
    }

    lint {
        disable += setOf("BidiSpoofing", "GradleDependency", "OutdatedLibrary")
    }

    testOptions {
        unitTests {
            // The token and component tests render real themed Compose rather than reading
            // properties back, so they need merged resources and the manifest.
            isIncludeAndroidResources = true
            isReturnDefaultValues = true

            // Carried verbatim from :receiver. Robolectric instruments the platform reflectively and
            // on JDK 17 that needs the module system opened to it; without java.net in particular a
            // Robolectric class earlier in the same worker JVM leaves JSSE unable to resolve a peer
            // address, and a perfectly correct test fails only when run in the suite.
            all { test ->
                test.jvmArgs(
                    "--add-opens=java.base/java.net=ALL-UNNAMED",
                    "--add-opens=java.base/java.lang=ALL-UNNAMED",
                    "--add-opens=java.base/java.util=ALL-UNNAMED",
                    "--add-opens=java.base/java.io=ALL-UNNAMED",
                    "--add-opens=java.base/java.security=ALL-UNNAMED",
                    "--add-opens=java.base/javax.net.ssl=ALL-UNNAMED",
                )
            }
        }
    }
}

val ktlint: Configuration by configurations.creating

dependencies {
    val composeBom = platform(libs.compose.bom)
    api(composeBom)
    androidTestImplementation(composeBom)
    testImplementation(composeBom)

    api(libs.compose.foundation)
    api(libs.compose.ui)
    implementation(libs.androidx.core.ktx)
    implementation(libs.compose.ui.tooling.preview)

    testImplementation(libs.junit)
    testImplementation(kotlin("test-junit"))
    testImplementation(libs.androidx.test.core)
    testImplementation(libs.robolectric)
    testImplementation(libs.compose.ui.test.junit4)
    debugImplementation(libs.compose.ui.test.manifest)

    ktlint(libs.ktlint.cli)
}

val ktlintCheck by tasks.registering(JavaExec::class) {
    group = "verification"
    description = "Checks Kotlin formatting in this module."
    classpath = ktlint
    mainClass.set("com.pinterest.ktlint.Main")
    // The glob is relative to the process's working directory, which Gradle does not otherwise
    // guarantee is this module.
    workingDir = projectDir
    args("src/**/*.kt", "--reporter=plain", "--relative")

    // Declared so Gradle can skip it when nothing it reads has changed -- and, more usefully, so it
    // cannot skip it when something has. Without these it re-ran on every build and was cached on
    // none, which is the wrong answer in both directions.
    inputs.files(fileTree("src") { include("**/*.kt") }).withPathSensitivity(PathSensitivity.RELATIVE)
    inputs.file(rootProject.file(".editorconfig"))
    val marker = layout.buildDirectory.file("ktlint/passed.txt")
    outputs.file(marker)
    doLast {
        marker.get().asFile.apply { parentFile.mkdirs() }.writeText("ok\n")
    }
}

tasks.named("check") {
    dependsOn(ktlintCheck)
}

apply(from = rootProject.file("gradle/scripts/source-checks.gradle.kts"))
