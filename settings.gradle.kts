pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
    }
}

rootProject.name = "FlintReceiver"
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


