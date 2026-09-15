// Source checks that are grammatical rather than behavioural, wired as real Gradle tasks.
//
// They used to be JUnit tests inside :phone:core that walked the filesystem from File("."). That
// made them invisible to Gradle: the sources they scanned were not declared inputs, so a change
// confined to :phone:app left :phone:core:test up to date and the guard did not run at all. It also
// meant one module's test knew where three other modules kept their sources. A task with declared
// inputs over its own module's tree cannot be skipped for a change it would have caught, and it
// lives in the module whose rule it is.
//
// The work is in named task classes rather than in `doLast` lambdas. A lambda written in a script
// plugin holds a reference to the script object, which the configuration cache cannot serialise, so
// the choice is between a task class and switching the cache off for the whole build.

import org.gradle.api.DefaultTask
import org.gradle.api.file.ConfigurableFileCollection
import org.gradle.api.file.RegularFileProperty
import org.gradle.api.provider.Property
import org.gradle.api.provider.SetProperty
import org.gradle.api.tasks.CacheableTask
import org.gradle.api.tasks.Input
import org.gradle.api.tasks.InputFiles
import org.gradle.api.tasks.OutputFile
import org.gradle.api.tasks.PathSensitive
import org.gradle.api.tasks.PathSensitivity
import org.gradle.api.tasks.TaskAction

/**
 * Fails when a socket binds a wildcard, or an address is written down.
 *
 * A source scan rather than a runtime assertion because the failure it guards against is one nothing
 * observes at runtime: on a phone tethering from mobile data, an unbound socket leaves over the
 * cellular network perfectly happily. It connects, it sends, and it reaches nothing. There is no
 * exception to catch and no log line to notice -- the television simply never answers, and the
 * symptom is indistinguishable from the television being switched off.
 */
@CacheableTask
abstract class SocketBindingGuardTask : DefaultTask() {
    @get:InputFiles
    @get:PathSensitive(PathSensitivity.RELATIVE)
    abstract val sources: ConfigurableFileCollection

    /** Whether a `Color(0x…)` literal in this module is an offence. It is wherever :design is not. */
    @get:Input
    abstract val forbidColourLiterals: Property<Boolean>

    /** Addresses a source may name: multicast groups, which are protocol constants, and loopback. */
    @get:Input
    abstract val permittedAddresses: SetProperty<String>

    @get:Input
    abstract val moduleName: Property<String>

    @get:OutputFile
    abstract val marker: RegularFileProperty

    @TaskAction
    fun scan() {
        val wildcard = "0.0.0.0"
        val bareServerSocket = Regex("""ServerSocket\s*\(\s*[^)\s]""")
        val bareDatagramSocket = Regex("""DatagramSocket\s*\(\s*\d""")
        val dottedQuad = Regex("""\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b""")
        val colourLiteral = Regex("""\bColor\s*\(\s*0x""")
        val permitted = permittedAddresses.get()
        val palette = forbidColourLiterals.get()

        val offences = mutableListOf<String>()
        var scanned = 0
        sources.files.sorted().forEach { file ->
            scanned++
            file.readLines().forEachIndexed { index, line ->
                // A comment or a KDoc line explains the rule; only code can break it.
                val trimmed = line.trimStart()
                val code = if (trimmed.startsWith("*") || trimmed.startsWith("/*")) {
                    ""
                } else {
                    line.substringBefore("//")
                }
                val at = "${file.name}:${index + 1}"
                if (wildcard in code ||
                    bareServerSocket.containsMatchIn(code) ||
                    bareDatagramSocket.containsMatchIn(code)
                ) {
                    offences += "$at: a socket must be bound to the chosen interface - ${line.trim()}"
                }
                dottedQuad.findAll(code).forEach { match ->
                    if (match.value !in permitted) {
                        offences += "$at: an address is derived, never written down - ${line.trim()}"
                    }
                }
                if (palette && colourLiteral.containsMatchIn(code)) {
                    offences += "$at: colour belongs to :design - ${line.trim()}"
                }
            }
        }

        // A scan that silently matched no files is a passing check that proves nothing, which is
        // worse than no check at all.
        check(scanned >= MINIMUM_FILES) {
            "The source guard found only $scanned Kotlin files in :${moduleName.get()}; " +
                "it is not looking at the module."
        }
        check(offences.isEmpty()) { offences.joinToString("\n", prefix = "\n") }
        marker.get().asFile.apply { parentFile.mkdirs() }.writeText("$scanned files\n")
    }

    companion object {
        /** Fewer than this means the scan is pointed at the wrong directory. */
        const val MINIMUM_FILES = 3
    }
}

/** Fails when a source file is long enough that nobody will read all of it. */
@CacheableTask
abstract class FileLengthGuardTask : DefaultTask() {
    @get:InputFiles
    @get:PathSensitive(PathSensitivity.RELATIVE)
    abstract val sources: ConfigurableFileCollection

    @get:Input
    abstract val maximumLines: Property<Int>

    @get:OutputFile
    abstract val marker: RegularFileProperty

    @TaskAction
    fun scan() {
        val limit = maximumLines.get()
        val offences = sources.files.sorted().mapNotNull { file ->
            val lines = file.readLines().size
            if (lines > limit) "${file.name}: $lines lines" else null
        }
        check(offences.isEmpty()) {
            "No source file may exceed $limit lines:\n" + offences.joinToString("\n")
        }
        marker.get().asFile.apply { parentFile.mkdirs() }.writeText("ok\n")
    }
}

val socketBindingGuard = tasks.register<SocketBindingGuardTask>("socketBindingGuard") {
    group = "verification"
    description = "Fails when a socket binds a wildcard, or an address is written down."
    // Production sources only. A test is entitled to write an address down -- naming one is how it
    // states the case it is testing -- and the rule is about what the shipped app does.
    sources.from(fileTree("src/main") { include("**/*.kt") })
    forbidColourLiterals.set(project.findProperty("forbidColourLiterals") == "true")
    permittedAddresses.set(setOf("224.0.0.251", "239.255.255.250", "127.0.0.1"))
    moduleName.set(project.name)
    marker.set(layout.buildDirectory.file("source-guard/binding.ok"))
}

val fileLengthGuard = tasks.register<FileLengthGuardTask>("fileLengthGuard") {
    group = "verification"
    description = "Fails when a source file is long enough that nobody will read all of it."
    sources.from(fileTree("src") { include("**/*.kt") })
    // A file this long is one nobody reads to the end of, whatever is in it.
    maximumLines.set(1_000)
    marker.set(layout.buildDirectory.file("source-guard/length.ok"))
}

tasks.named("check") {
    dependsOn(socketBindingGuard, fileLengthGuard)
}
