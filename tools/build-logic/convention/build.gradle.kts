plugins {
    `kotlin-dsl`
}

// The gates every other module is held to are written in this project, so it is held to them too. A
// deprecated Gradle or AGP call here is exactly the kind of thing the convention plugins exist to
// stop elsewhere, and it compiled quietly in the one build that defines them.
kotlin {
    compilerOptions {
        allWarningsAsErrors.set(true)
    }
}

val ktlint: Configuration by configurations.creating

dependencies {
    // Compiled against, never bundled: the build that applies these plugins already has the Android
    // and Kotlin Gradle plugins on its classpath, at the versions the catalog pins.
    compileOnly(libs.android.gradle.plugin)
    compileOnly(libs.kotlin.gradle.plugin)

    ktlint(libs.ktlint.cli)
}

// The same check flint.quality gives every other module, written out by hand because this project
// cannot apply a plugin it is itself compiling.
val ktlintCheck by tasks.registering(JavaExec::class) {
    group = "verification"
    description = "Checks the convention plugins' own formatting."
    classpath = ktlint
    mainClass.set("com.pinterest.ktlint.Main")
    // The glob is relative to the process's working directory, which Gradle does not otherwise
    // guarantee is this project.
    workingDir = projectDir
    args("src/**/*.kt", "--reporter=plain", "--relative")

    inputs.files(fileTree("src") { include("**/*.kt") }).withPathSensitivity(PathSensitivity.RELATIVE)
    inputs.file(repositoryFile(".editorconfig"))
    val marker = layout.buildDirectory.file("ktlint/passed.txt")
    outputs.file(marker)
    doLast {
        marker.get().asFile.apply { parentFile.mkdirs() }.writeText("ok\n")
    }
}

val ktlintFormat by tasks.registering(JavaExec::class) {
    group = "formatting"
    description = "Applies ktlint's layout to the convention plugins."
    classpath = ktlint
    mainClass.set("com.pinterest.ktlint.Main")
    workingDir = projectDir
    args("-F", "src/**/*.kt", "--reporter=plain", "--relative")
}

tasks.named("check") {
    dependsOn(ktlintCheck)
}

/** A file in the repository root, two levels above this included build. */
fun repositoryFile(name: String) = rootProject.layout.projectDirectory.dir("../..").file(name)

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
