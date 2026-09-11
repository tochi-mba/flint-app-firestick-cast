package com.rextechnologies.flint.castcore.guard

import java.io.File
import kotlin.test.Test
import kotlin.test.assertTrue
import kotlin.test.fail

/**
 * The executable half of the rule that every socket is pinned to a chosen interface.
 *
 * It is a source scan rather than a runtime assertion because the failure it guards against is one
 * nothing observes at runtime: on a phone tethering from mobile data, an unbound socket leaves over
 * the cellular network perfectly happily. It connects, it sends, and it reaches nothing. There is no
 * exception to catch and no log line to notice — the television simply never answers, and the
 * symptom is indistinguishable from the television being switched off.
 *
 * So the guard is grammatical. Anything that binds a wildcard, or writes an address down instead of
 * deriving it, fails the build in the module that did it.
 */
class SocketBindingGuardTest {
    private val guardedModules = listOf("castcore", "mobile")

    @Test
    fun `no source binds a wildcard address`() {
        val offences = scan { line ->
            WILDCARD_LITERAL in line ||
                BARE_SERVER_SOCKET.containsMatchIn(line) ||
                BARE_DATAGRAM_SOCKET.containsMatchIn(line)
        }
        if (offences.isNotEmpty()) {
            fail(
                "A socket must be bound to the interface the ladder chose, never a wildcard:\n" +
                    offences.joinToString("\n"),
            )
        }
    }

    @Test
    fun `no source writes a subnet down instead of deriving it`() {
        val offences = scan { line ->
            DOTTED_QUAD.findAll(line).any { match ->
                val value = match.value
                value !in PERMITTED_ADDRESSES && !line.trimStart().startsWith("*")
            }
        }
        if (offences.isNotEmpty()) {
            fail(
                "A subnet, prefix or address is derived from the interface, never written down:\n" +
                    offences.joinToString("\n"),
            )
        }
    }

    @Test
    fun `no phone source declares a colour of its own`() {
        // The invariant behind ADR-0030. :design owns the palette; a Color literal anywhere in
        // :mobile is a token that has escaped it, and nothing about that fails to compile -- the
        // product simply stops looking like one product, slowly, one screen at a time.
        val offences = scanModule("mobile") { line -> COLOUR_LITERAL.containsMatchIn(line) }
        if (offences.isNotEmpty()) {
            fail(
                "Colour belongs to :design, not to a screen:\n" + offences.joinToString("\n"),
            )
        }
    }

    @Test
    fun `the guard is actually looking at something`() {
        // A scan that silently matches no files is a passing test that proves nothing, which is worse
        // than no test at all.
        val scanned = guardedModules.sumOf { module ->
            sourceRoot(module)?.walkTopDown()?.count { it.isFile && it.extension == "kt" } ?: 0
        }
        assertTrue(scanned > 10, "expected to scan the phone's sources, found $scanned files")
    }

    private fun scan(offends: (String) -> Boolean): List<String> =
        guardedModules.flatMap { scanModule(it, offends) }

    private fun scanModule(module: String, offends: (String) -> Boolean): List<String> = buildList {
        val root = sourceRoot(module) ?: return@buildList
        root.walkTopDown()
            .filter { it.isFile && it.extension == "kt" }
            .forEach { file ->
                file.readLines().forEachIndexed { index, line ->
                    val code = line.substringBefore("//")
                    if (offends(code)) add("${file.path}:${index + 1}: ${line.trim()}")
                }
            }
    }

    private fun sourceRoot(module: String): File? {
        var directory: File? = File(".").absoluteFile
        while (directory != null) {
            val candidate = File(directory, "$module/src/main/kotlin")
            if (candidate.isDirectory) return candidate
            directory = directory.parentFile
        }
        return null
    }

    private companion object {
        const val WILDCARD_LITERAL = "0.0.0.0"

        /** `ServerSocket(port)` binds every interface; the safe form binds explicitly afterwards. */
        val BARE_SERVER_SOCKET = Regex("""ServerSocket\s*\(\s*[^)\s]""")

        /** The same for datagrams: an int argument is a port on the wildcard address. */
        val BARE_DATAGRAM_SOCKET = Regex("""DatagramSocket\s*\(\s*\d""")

        val DOTTED_QUAD = Regex("""\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b""")

        /**
         * The only addresses allowed to appear as literals.
         *
         * Both are constants of a protocol rather than guesses about somebody's network: the mDNS
         * group and the SSDP one are the same numbers on every network in the world.
         */
        val PERMITTED_ADDRESSES = setOf("224.0.0.251", "239.255.255.250")

        /** `Color(0xFF…)`, in any of the forms Compose accepts. */
        val COLOUR_LITERAL = Regex("""\bColor\s*\(\s*0[xX]""")
    }
}
