plugins {
    kotlin("jvm")
    jacoco
}

kotlin {
    jvmToolchain(17)
    compilerOptions {
        // Kotlin was the only language in this repo without a warnings-as-errors gate: Rust runs
        // `clippy -D warnings` and the .NET projects set TreatWarningsAsErrors. A deprecation that
        // compiles quietly here is one that gets discovered on a television.
        allWarningsAsErrors.set(true)
    }
}

// ktlint runs as a plain command-line tool rather than through its Gradle plugin, so the lint gate
// has no opinion about which Gradle or Kotlin version this build uses.
val ktlint: Configuration by configurations.creating

dependencies {
    api(project(":protocol"))
    testImplementation(kotlin("test-junit"))
    testImplementation(libs.junit)
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

tasks.test {
    useJUnit()
    finalizedBy(tasks.jacocoTestReport)
}

jacoco {
    toolVersion = libs.versions.jacoco.get()
}

tasks.jacocoTestReport {
    dependsOn(tasks.test)
    reports {
        xml.required.set(true)
        html.required.set(true)
        csv.required.set(false)
    }
}

tasks.jacocoTestCoverageVerification {
    dependsOn(tasks.test)
    violationRules {
        rule {
            element = "BUNDLE"
            limit {
                counter = "LINE"
                value = "COVEREDRATIO"
                minimum = "0.95".toBigDecimal()
            }
            limit {
                counter = "BRANCH"
                value = "COVEREDRATIO"
                minimum = "0.85".toBigDecimal()
            }
        }
    }
}

tasks.check {
    dependsOn(tasks.jacocoTestCoverageVerification, ktlintCheck)
}

apply(from = rootProject.file("gradle/scripts/source-checks.gradle.kts"))
