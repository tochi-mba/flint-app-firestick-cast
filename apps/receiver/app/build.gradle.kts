import java.util.Properties

plugins {
    id("flint.android-application")
    id("flint.compose")
}

// Read through a provider rather than by opening the file. A file opened during configuration is an
// input the configuration cache cannot see, so a change to it would be missed; read this way it is
// tracked, and an absent file is simply an absent value rather than a branch on the file system.
val keystoreProperties: Properties? =
    providers.fileContents(rootProject.layout.projectDirectory.file("keystore.properties"))
        .asText
        .map { text -> Properties().apply { load(text.reader()) } }
        .orNull

android {
    namespace = "com.rextechnologies.flint.receiver"

    defaultConfig {
        applicationId = "com.rextechnologies.flint.receiver"
        // Fire OS 6 reports API 25; keeping this at 25 includes older Fire TV hardware.
        minSdk = libs.versions.receiver.min.sdk.get().toInt()
    }

    signingConfigs {
        keystoreProperties?.let { properties ->
            create("release") {
                storeFile = rootProject.file(properties.getProperty("storeFile"))
                storePassword = properties.getProperty("storePassword")
                keyAlias = properties.getProperty("keyAlias")
                keyPassword = properties.getProperty("keyPassword")
            }
        }
    }

    buildTypes {
        release {
            if (keystoreProperties != null) {
                signingConfig = signingConfigs.getByName("release")
            }
        }
    }

    compileOptions {
        // The WireGuard tunnel uses newer JDK APIs; its embedding notes require desugaring.
        isCoreLibraryDesugaringEnabled = true
    }
}

dependencies {
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
