plugins {
    id("flint.android-application")
    id("flint.compose")
}

// Signing material is read through providers rather than by opening a file during configuration. A
// file opened during configuration is tracked by the configuration cache as an input, so the cache is
// rebuilt whenever it changes; environment variables read through providers are tracked the same way
// but need no file on disk at all, which is what a CI runner handing secrets in through the
// environment wants.
val keystorePath = providers.environmentVariable("MOBILE_KEYSTORE_PATH").orNull
val keystorePassword = providers.environmentVariable("MOBILE_KEYSTORE_PASSWORD").orNull
val releaseKeyAlias = providers.environmentVariable("MOBILE_KEY_ALIAS").orNull
val releaseKeyPassword = providers.environmentVariable("MOBILE_KEY_PASSWORD").orNull

// All four or none. A half-configured signing block produces an APK signed with something nobody
// intended, which is worse than an obviously unsigned one.
val signingMaterialPresent = !keystorePath.isNullOrBlank() &&
    !keystorePassword.isNullOrBlank() &&
    !releaseKeyAlias.isNullOrBlank() &&
    !releaseKeyPassword.isNullOrBlank()

android {
    namespace = "com.rextechnologies.flint.mobile"

    defaultConfig {
        applicationId = "com.rextechnologies.flint.mobile"
        minSdk = libs.versions.mobile.min.sdk.get().toInt()
    }

    signingConfigs {
        if (signingMaterialPresent) {
            create("release") {
                storeFile = file(keystorePath!!)
                storePassword = keystorePassword
                keyAlias = releaseKeyAlias
                keyPassword = releaseKeyPassword
            }
        }
    }

    buildTypes {
        release {
            if (signingMaterialPresent) {
                signingConfig = signingConfigs.getByName("release")
            }
        }
    }

    buildFeatures {
        // The app needs to know its own version name to show it on the About card, and that is the
        // only thing it reads from BuildConfig.
        buildConfig = true
    }

    packaging {
        resources {
            excludes += "/META-INF/{AL2.0,LGPL2.1}"
        }
    }
}

flint {
    // :phone:app may not declare a colour: the palette belongs to :phone:design, and a Color literal
    // here is a token that has escaped it.
    forbidColourLiterals = true
}

dependencies {
    val composeBom = platform(libs.compose.bom)
    implementation(composeBom)
    androidTestImplementation(composeBom)
    testImplementation(composeBom)

    implementation(project(":protocol"))
    implementation(project(":phone:core"))
    implementation(project(":phone:design"))

    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.activity.compose)
    implementation(libs.androidx.lifecycle.runtime)
    implementation(libs.androidx.lifecycle.viewmodel)
    implementation(libs.androidx.savedstate)
    implementation(libs.compose.foundation)
    implementation(libs.compose.ui)
    implementation(libs.compose.ui.tooling.preview)
    implementation(libs.kotlinx.coroutines.android)
    debugImplementation(libs.compose.ui.tooling)

    testImplementation(libs.junit)
    testImplementation(kotlin("test-junit"))
    testImplementation(libs.androidx.test.core)
    testImplementation(libs.robolectric)
    testImplementation(libs.compose.ui.test.junit4)
    debugImplementation(libs.compose.ui.test.manifest)

    androidTestImplementation(libs.androidx.test.ext.junit)
    androidTestImplementation(libs.androidx.test.runner)
    androidTestImplementation(libs.androidx.test.uiautomator)
    androidTestImplementation(libs.compose.ui.test.junit4)
}
