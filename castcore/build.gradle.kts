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
    args("src/**/*.kt", "--reporter=plain", "--relative")
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
