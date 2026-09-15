package com.rextechnologies.flint.buildlogic

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
 * A source scan rather than a runtime assertion, because the failure it guards against is one nothing
 * observes at runtime. On a phone tethering from mobile data, an unbound socket leaves over the
 * cellular network perfectly happily: it connects, it sends, and it reaches nothing. There is no
 * exception to catch and no log line to notice. The television simply never answers, which looks
 * exactly like the television being switched off.
 */
@CacheableTask
abstract class SocketBindingGuardTask : DefaultTask() {
    @get:InputFiles
    @get:PathSensitive(PathSensitivity.RELATIVE)
    abstract val sources: ConfigurableFileCollection

    /** Whether a `Color(0x…)` literal is an offence here. See [FlintExtension.forbidColourLiterals]. */
    @get:Input
    abstract val forbidColourLiterals: Property<Boolean>

    /** Addresses a source may name. */
    @get:Input
    abstract val permittedAddresses: SetProperty<String>

    @get:Input
    abstract val modulePath: Property<String>

    @get:OutputFile
    abstract val marker: RegularFileProperty

    @TaskAction
    fun scan() {
        val permitted = permittedAddresses.get()
        val palette = forbidColourLiterals.get()
        val offences = mutableListOf<String>()
        var scanned = 0

        sources.files.sorted().forEach { file ->
            scanned++
            file.readLines().forEachIndexed { index, line ->
                val code = codeOf(line)
                val at = "${file.name}:${index + 1}"
                if (WILDCARD.containsMatchIn(code) ||
                    BARE_SERVER_SOCKET.containsMatchIn(code) ||
                    BARE_DATAGRAM_SOCKET.containsMatchIn(code)
                ) {
                    offences += "$at: a socket must be bound to the chosen interface - ${line.trim()}"
                }
                DOTTED_QUAD.findAll(code).filter { it.value !in permitted }.forEach { _ ->
                    offences += "$at: an address is derived, never written down - ${line.trim()}"
                }
                if (palette && COLOUR_LITERAL.containsMatchIn(code)) {
                    offences += "$at: colour belongs to the design module - ${line.trim()}"
                }
            }
        }

        // A scan that silently matched no files is a passing check that proves nothing, which is
        // worse than no check at all.
        check(scanned >= MINIMUM_FILES) {
            "The source guard found only $scanned Kotlin files in ${modulePath.get()}; it is not looking at the module."
        }
        check(offences.isEmpty()) { offences.joinToString("\n", prefix = "\n") }
        marker.get().asFile.apply { parentFile.mkdirs() }.writeText("$scanned files\n")
    }

    private companion object {
        /** Fewer than this means the scan is pointed at the wrong directory. */
        const val MINIMUM_FILES = 3

        // A route written as CIDR, such as a WireGuard AllowedIPs of 0.0.0.0/0 in help text, is a
        // range the user types, not an address any socket here binds; the lookahead leaves it alone.
        val WILDCARD = Regex("""0\.0\.0\.0(?!/\d)""")
        val DOTTED_QUAD = Regex("""\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b(?!/\d)""")

        // The word boundary keeps a function such as newServerSocket(port), which is the binder that
        // does it properly, from reading as the offence it replaces.
        val BARE_SERVER_SOCKET = Regex("""\bServerSocket\s*\(\s*[^)\s]""")
        val BARE_DATAGRAM_SOCKET = Regex("""\bDatagramSocket\s*\(\s*\d""")
        val COLOUR_LITERAL = Regex("""\bColor\s*\(\s*0x""")

        /** The part of a line that is code. A comment or a KDoc line explains the rule; only code can break it. */
        fun codeOf(line: String): String {
            val trimmed = line.trimStart()
            return if (trimmed.startsWith("*") || trimmed.startsWith("/*")) "" else line.substringBefore("//")
        }
    }
}
