pluginManagement {
    // The convention plugins every Kotlin module applies. An included build rather than buildSrc,
    // which Gradle puts on the classpath of every project whether it uses the plugins or not.
    includeBuild("tools/build-logic")
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

plugins {
    // Provisions the JDK a module's toolchain asks for when this machine does not have it.
    id("org.gradle.toolchains.foojay-resolver-convention") version "1.0.0"
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
    }
}

rootProject.name = "Flint"

// Gradle paths follow the product and folders follow the repository: each app lives under apps/,
// and the Kotlin protocol sits under protocol/ beside the .NET one. Parent projects are pointed at
// real folders because Gradle refuses a project whose directory does not exist.
include(":protocol")
project(":protocol").projectDir = file("protocol/kotlin")

include(":phone:app", ":phone:core", ":phone:design")
project(":phone").projectDir = file("apps/phone")
project(":phone:app").projectDir = file("apps/phone/app")
project(":phone:core").projectDir = file("apps/phone/core")
project(":phone:design").projectDir = file("apps/phone/design")

include(":receiver:app")
project(":receiver").projectDir = file("apps/receiver")
project(":receiver:app").projectDir = file("apps/receiver/app")
