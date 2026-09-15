plugins {
    id("flint.android-library")
    id("flint.compose")
}

android {
    namespace = "com.rextechnologies.flint.design"

    defaultConfig {
        // The receiver's floor, not the phone's. :phone:design has no API requirement above 21;
        // holding it at the lower of the two apps is what lets the Fire TV app adopt these tokens
        // later without the module itself being the reason it cannot.
        minSdk = libs.versions.receiver.min.sdk.get().toInt()
    }
}

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
}
