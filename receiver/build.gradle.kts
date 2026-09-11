import java.util.Properties

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.plugin.compose")
}

val keystoreProperties = Properties()
val keystorePropertiesFile = rootProject.file("keystore.properties")
if (keystorePropertiesFile.exists()) {
    keystorePropertiesFile.inputStream().use(keystoreProperties::load)
}

android {
    namespace = "com.rextechnologies.flint.receiver"
    compileSdk = libs.versions.compile.sdk.get().toInt()
    // Pin away from a corrupted local build-tools 36.0.0 install (missing aidl.exe).
    buildToolsVersion = "35.0.0"

    defaultConfig {
        applicationId = "com.rextechnologies.flint.receiver"
        // Fire OS 6 reports API 25; keeping this at 25 includes older Fire TV hardware.
        minSdk = libs.versions.receiver.min.sdk.get().toInt()
        targetSdk = libs.versions.target.sdk.get().toInt()
        versionCode = 1
        versionName = "0.1.0"
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    signingConfigs {
        if (keystorePropertiesFile.exists()) {
            create("release") {
                storeFile = rootProject.file(keystoreProperties.getProperty("storeFile"))
                storePassword = keystoreProperties.getProperty("storePassword")
                keyAlias = keystoreProperties.getProperty("keyAlias")
                keyPassword = keystoreProperties.getProperty("keyPassword")
            }
        }
    }

    buildTypes {
        debug {
            applicationIdSuffix = ".debug"
            versionNameSuffix = "-debug"
        }
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro",
            )
            if (keystorePropertiesFile.exists()) {
                signingConfig = signingConfigs.getByName("release")
            }
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
        // WireGuard tunnel uses newer JDK APIs; required by the upstream embedding notes.
        isCoreLibraryDesugaringEnabled = true
    }

    buildFeatures {
        compose = true
        // No receiver source reads BuildConfig. Leaving generation enabled creates
        // a needless Javac task whose JDK ZipFS classpath cleanup is unreliable on Windows.
        buildConfig = false
    }

    lint {
        disable += setOf("BidiSpoofing", "GradleDependency", "OutdatedLibrary")
    }

    testOptions {
        unitTests {
            // Robolectric needs the merged resources and the manifest to inflate anything, and the
            // screenshot tests render real themed Compose rather than a stand-in.
            isIncludeAndroidResources = true

            // Android framework calls in code under test return defaults instead of throwing.
            // Without this, a plain `Log.i` on a path a unit test happens to reach fails the test
            // with "not mocked" — which punishes diagnostics rather than defects.
            isReturnDefaultValues = true

            // Robolectric instruments the platform reflectively, and on JDK 17 that needs the
            // module system opened to it. Without `java.net` in particular, a Robolectric class
            // running earlier in the same worker JVM leaves JSSE unable to resolve a peer address,
            // and the real-socket TLS tests fail a handshake that is perfectly correct — passing on
            // their own and failing in the suite, which is the worst way for this to present.
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

dependencies {
    // WireGuard embedding requires desugaring (see wireguard-android README).
    coreLibraryDesugaring(libs.desugar.jdk.libs)

    val composeBom = platform(libs.compose.bom)
    implementation(composeBom)
    androidTestImplementation(composeBom)

    implementation(libs.wireguard.tunnel)
    implementation(libs.androidx.activity.compose)
    implementation(libs.compose.foundation)
    implementation(libs.compose.ui)
    implementation(libs.compose.ui.tooling.preview)
    implementation(libs.androidx.tv.material)
    // Feature-gated WebView APIs. The platform classes alone cannot ask whether a capability is
    // present, and the browser refuses to claim one it has not checked for.
    implementation(libs.androidx.webkit)
    implementation(libs.media3.exoplayer)
    implementation(libs.media3.exoplayer.dash)
    implementation(libs.media3.exoplayer.hls)
    implementation(libs.media3.ui)
    implementation(libs.kotlinx.coroutines.android)
    implementation(libs.zxing.core)
    implementation(libs.bouncycastle.pkix)
    implementation(project(":protocol"))
    debugImplementation(libs.compose.ui.tooling)

    testImplementation(libs.junit)
    testImplementation(kotlin("test-junit"))
    testImplementation(libs.androidx.test.core)
    testImplementation(libs.robolectric)
    testImplementation(composeBom)
    testImplementation(libs.compose.ui.test.junit4)
    debugImplementation(libs.compose.ui.test.manifest)
    androidTestImplementation(libs.androidx.test.ext.junit)
    androidTestImplementation(libs.androidx.test.runner)
    androidTestImplementation(libs.androidx.test.uiautomator)
}

