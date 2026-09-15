package com.rextechnologies.flint.protocol.wire

import java.nio.file.Path
import kotlin.io.path.exists
import kotlin.io.path.isDirectory
import kotlin.io.path.readLines
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlin.test.fail

/**
 * Every browser enum here must agree with the committed manifest, value for value.
 *
 * The golden corpus proves the three implementations encode the same *messages* identically. It
 * cannot prove they agree on the values those messages may carry — a vector exercises only the
 * values it happens to use, and most are used by none. A value present on one side and missing on
 * another therefore passes every test it has, and fails on a real television at the moment someone
 * presses the button.
 *
 * Keyed on wire values rather than names: the three languages deliberately spell the same value
 * differently (`SET_UA` / `SetUa` / `SetUserAgent`), and only the number crosses the wire.
 */
class BrowserEnumManifestTest {
    /**
     * Kotlin's enum name paired with the manifest's name for it.
     *
     * Written out rather than derived, because a reflective sweep would silently skip an enum that
     * was renamed — which is the failure this test exists to catch.
     */
    private val enums: Map<String, List<Int>> = mapOf(
        "BrowserCapabilityStatus" to BrowserCapabilityStatus.entries.map { it.id },
        "BrowserCommandAction" to BrowserCommandAction.entries.map { it.id },
        "BrowserPointerAction" to BrowserPointerAction.entries.map { it.id },
        "BrowserSemanticKey" to BrowserSemanticKey.entries.map { it.id },
        "BrowserLoadState" to BrowserLoadState.entries.map { it.id },
        "BrowserPreviewState" to BrowserPreviewState.entries.map { it.id },
        "BrowserDialogType" to BrowserDialogType.entries.map { it.id },
        "BrowserTabAction" to BrowserTabAction.entries.map { it.id },
        "BrowserViewAction" to BrowserViewAction.entries.map { it.id },
        "BrowserUserAgentMode" to BrowserUserAgentMode.entries.map { it.id },
        "BrowserDarkMode" to BrowserDarkMode.entries.map { it.id },
        "BrowserInteractionMode" to BrowserInteractionMode.entries.map { it.id },
        "BrowserSearchEngine" to BrowserSearchEngine.entries.map { it.id },
        "BrowserLibraryAction" to BrowserLibraryAction.entries.map { it.id },
        "BrowserLibraryEntryKind" to BrowserLibraryEntryKind.entries.map { it.id },
        "BrowserNetworkAction" to BrowserNetworkAction.entries.map { it.id },
        "BrowserVpnProvider" to BrowserVpnProvider.entries.map { it.id },
        "BrowserVpnSessionState" to BrowserVpnSessionState.entries.map { it.id },
        "BrowserProfileAction" to BrowserProfileAction.entries.map { it.id },
        "BrowserProfileSource" to BrowserProfileSource.entries.map { it.id },
        "BrowserWorkspaceCommandAction" to BrowserWorkspaceCommandAction.entries.map { it.id },
        "BrowserWorkspaceInputKind" to BrowserWorkspaceInputKind.entries.map { it.id },
        "BrowserWorkspaceWireInteractionMode" to
            BrowserWorkspaceWireInteractionMode.entries.map { it.id },
        "BrowserWorkspaceWireLayout" to BrowserWorkspaceWireLayout.entries.map { it.id },
        "BrowserWorkspaceWireMuteApplication" to
            BrowserWorkspaceWireMuteApplication.entries.map { it.id },
        "BrowserWorkspaceWireObservedPlayback" to
            BrowserWorkspaceWireObservedPlayback.entries.map { it.id },
        "BrowserWorkspaceWirePaneResidency" to
            BrowserWorkspaceWirePaneResidency.entries.map { it.id },
    )

    @Test
    fun `kotlin browser enums match the committed manifest`() {
        val manifest = readManifest()
        assertTrue(manifest.isNotEmpty(), "the committed manifest is empty")

        for ((name, expected) in manifest) {
            val actual = enums[name]
                ?: fail("$name is in the manifest but this test does not check it")
            assertEquals(
                expected,
                actual.sorted(),
                "$name wire values differ from the committed manifest. If this is a deliberate " +
                    "protocol change, regenerate with: " +
                    "cargo test --test enum_manifest -- --ignored regenerate",
            )
        }
    }

    @Test
    fun `every enum this test knows about is in the manifest`() {
        // The other direction: an enum added here without regenerating would be checked by nobody.
        val manifest = readManifest()
        val unlisted = enums.keys.filterNot(manifest::containsKey).sorted()
        assertTrue(
            unlisted.isEmpty(),
            "absent from the committed manifest, so no other language checks them: $unlisted",
        )
    }

    private fun readManifest(): Map<String, List<Int>> =
        goldenDirectory().resolve("browser-enums.txt")
            .readLines()
            .filter { it.isNotBlank() }
            .associate { line ->
                val (name, values) = line.split("=", limit = 2)
                name to values.split(",").filter { it.isNotBlank() }.map(String::toInt)
            }

    private fun goldenDirectory(): Path {
        var current: Path? = Path.of("").toAbsolutePath()
        while (current != null) {
            val candidate = current.resolve("protocol").resolve("golden")
            if (candidate.exists() && candidate.isDirectory()) {
                return candidate
            }
            current = current.parent
        }
        fail(
            "Could not find protocol/golden. Generate the manifest with: " +
                "cargo test --test enum_manifest -- --ignored regenerate",
        )
    }
}
