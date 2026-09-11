plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.plugin.compose")
}

// Everything the release build needs is read through a provider rather than by opening a file
// during configuration. :receiver reads keystore.properties directly, which silently invalidates
// the configuration cache this build has switched on; doing the same here would have made every
// phone build a cold one.
val mobileVersionName = providers.gradleProperty("mobile.version").get()
val mobileVersionSuffix = providers.gradleProperty("mobile.versionSuffix").getOrElse("")
val mobileVersionCode = providers.gradleProperty("mobile.versionCode").map(String::toInt).getOrElse(1)

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
    compileSdk = libs.versions.compile.sdk.get().toInt()

    defaultConfig {
        applicationId = "com.rextechnologies.flint.mobile"
        minSdk = libs.versions.mobile.min.sdk.get().toInt()
        targetSdk = libs.versions.target.sdk.get().toInt()
        versionCode = mobileVersionCode
        versionName = mobileVersionName + mobileVersionSuffix
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
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
            if (signingMaterialPresent) {
                signingConfig = signingConfigs.getByName("release")
            }
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    buildFeatures {
        compose = true
        // The app needs to know its own version name to show it on the About card, and that is the
        // only thing it reads from BuildConfig.
        buildConfig = true
    }

    lint {
        disable += setOf("BidiSpoofing", "GradleDependency", "OutdatedLibrary")
    }

    packaging {
        resources {
            excludes += "/META-INF/{AL2.0,LGPL2.1}"
        }
    }

    testOptions {
        unitTests {
            isIncludeAndroidResources = true
            isReturnDefaultValues = true
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
    implementation(composeBom)
    androidTestImplementation(composeBom)
    testImplementation(composeBom)

    implementation(project(":protocol"))
    implementation(project(":castcore"))
    implementation(project(":design"))

    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.activity.compose)
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

    ktlint(libs.ktlint.cli)
}

val ktlintCheck by tasks.registering(JavaExec::class) {
    group = "verification"
    description = "Checks Kotlin formatting in this module."
    classpath = ktlint
    mainClass.set("com.pinterest.ktlint.Main")
    args("src/**/*.kt", "--reporter=plain", "--relative")
}

tasks.named("check") {
    dependsOn(ktlintCheck)
}
