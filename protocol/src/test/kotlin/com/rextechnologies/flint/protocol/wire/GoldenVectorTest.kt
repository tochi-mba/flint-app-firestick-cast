package com.rextechnologies.flint.protocol.wire

import com.rextechnologies.flint.protocol.BinaryData
import java.nio.file.Files
import java.nio.file.Path
import kotlin.io.path.exists
import kotlin.io.path.isDirectory
import kotlin.io.path.listDirectoryEntries
import kotlin.io.path.name
import kotlin.io.path.readBytes
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlin.test.fail

/**
 * Asserts Kotlin agrees byte-for-byte with the Rust-generated corpus in `testdata/golden/`.
 *
 * Case names are the cross-language contract. A case defined here without a committed `.bin`, or a
 * committed `.bin` without a Kotlin case, fails completeness rather than drifting on a television.
 */
class GoldenVectorTest {
    @Test
    fun `every case encodes to its committed bytes`() {
        for ((name, frame) in GoldenVectors.all) {
            val expected = Files.readAllBytes(goldenFile(name))
            val actual = WireCodec.encode(frame)
            assertContentEquals(expected, actual, name)
        }
    }

    @Test
    fun `every committed vector decodes to its case`() {
        for ((name, frame) in GoldenVectors.all) {
            val decoded = WireCodec.decode(Files.readAllBytes(goldenFile(name)))
            assertEquals(normalise(frame), normalise(decoded), name)
        }
    }

    @Test
    fun `no committed vector is orphaned`() {
        val expected = GoldenVectors.all.keys.map { "$it.bin" }.toSet()
        val committed = goldenDirectory()
            .listDirectoryEntries("*.bin")
            .map { it.name }
            .toSet()
        assertTrue(committed.isNotEmpty(), "the golden corpus should not be empty")
        for (name in committed) {
            assertTrue(
                expected.contains(name),
                "$name is committed but has no Kotlin case. Add it to GoldenVectors, or delete the file.",
            )
        }
        for (name in expected) {
            assertTrue(committed.contains(name), "$name has a Kotlin case but no committed vector")
        }
    }

    private fun goldenFile(name: String): Path = goldenDirectory().resolve("$name.bin")

    private fun goldenDirectory(): Path {
        var current: Path? = Path.of("").toAbsolutePath()
        while (current != null) {
            val candidate = current.resolve("testdata").resolve("golden")
            if (candidate.exists() && candidate.isDirectory()) {
                return candidate
            }
            current = current.parent
        }
        fail(
            "Could not find testdata/golden. Generate vectors with: " +
                "cargo test --test golden -- --ignored regenerate",
        )
    }

    private fun normalise(frame: WireFrame): WireFrame {
        val message = when (val payload = frame.message) {
            is HelloMessage -> payload.copy(
                codecCapabilities = payload.codecCapabilities.toSortedSet(compareBy { it.value }),
            )
            else -> payload
        }
        return frame.copy(message = message)
    }
}

/** Mirrors `flint-engine/tests/golden.rs` and `Flint.Protocol.Tests.GoldenVectors`. */
internal object GoldenVectors {
    val all: Map<String, WireFrame> = build()

    private fun build(): Map<String, WireFrame> {
        val vectors = linkedMapOf(
            "hello-minimal" to WireFrame(
                1,
                HelloMessage(1, 1, "TV", emptySet(), 1280, 720, 160),
            ),
            "hello-full" to WireFrame(
                1,
                HelloMessage(
                    1,
                    3,
                    "Living Room \uD83D\uDCFA",
                    setOf(CodecId.H265, CodecId.H264, CodecId.H265, CodecId.OPUS),
                    3840,
                    2160,
                    320,
                ),
                flags = 0xabcd,
            ),
            "auth-pairing-code" to WireFrame(
                1,
                AuthMessage(AuthMethod.PAIRING_CODE, BinaryData.of("012345".toByteArray())),
            ),
            "auth-with-fingerprint" to WireFrame(
                1,
                AuthMessage(
                    AuthMethod.SESSION_TOKEN,
                    BinaryData.of(byteArrayOf(0x00, 0xff.toByte(), 0x7f, 0x80.toByte())),
                    "sha256:01:02:ab",
                ),
            ),
            "video-config" to WireFrame(
                1,
                VideoConfigMessage(
                    CodecId.H264,
                    1920,
                    1080,
                    listOf(
                        BinaryData.of(byteArrayOf(0, 0, 0, 1, 0x67)),
                        BinaryData.of(byteArrayOf(0, 0, 0, 1, 0x68)),
                    ),
                ),
            ),
            "video-config-no-csd" to WireFrame(1, VideoConfigMessage(CodecId.AV1, 1, 1, emptyList())),
            "video-keyframe" to WireFrame(
                1,
                VideoPacket(9_876_543, true, BinaryData.of(byteArrayOf(0, 0, 1, 0x65))),
            ),
            "video-delta" to WireFrame(
                1,
                VideoPacket(0, false, BinaryData.of(byteArrayOf(0x41))),
            ),
            "audio-config" to WireFrame(1, AudioConfigMessage(CodecId.OPUS, 48_000, 2)),
            "audio-packet" to WireFrame(
                1,
                AudioPacket(1, BinaryData.of(byteArrayOf(9, 8, 7))),
            ),
            "control-transport-play" to WireFrame(
                1,
                ControlMessage(1, TransportControl(TransportAction.PLAY)),
            ),
            "control-transport-seek" to WireFrame(
                1,
                ControlMessage(Long.MAX_VALUE, TransportControl(TransportAction.SEEK_TO, 123_456)),
            ),
            "control-pointer" to WireFrame(
                1,
                ControlMessage(3, PointerControl(PointerAction.MOVE, 0.25f, 0.75f, 1)),
            ),
            "control-key" to WireFrame(1, ControlMessage(4, KeyControl(KeyAction.DOWN, 23))),
            "control-text" to WireFrame(
                1,
                ControlMessage(5, TextControl("Hello, TV \uD83D\uDC4B")),
            ),
            "control-volume" to WireFrame(1, ControlMessage(6, VolumeControl(0.5f))),
            "stats" to WireFrame(1, StatsMessage(3, 17_000, 8_000, 12)),
            "stats-zero" to WireFrame(1, StatsMessage(0, 0, 0, 0)),
            "bye-normal" to WireFrame(1, ByeMessage(ByeReason.NORMAL)),
            "bye-protocol-error" to WireFrame(
                1,
                ByeMessage(ByeReason.PROTOCOL_ERROR, "malformed packet"),
            ),
            "unknown-future-message" to WireFrame(
                1,
                UnknownMessage(0xfefe, BinaryData.of(byteArrayOf(1, 2, 3, 4))),
                flags = 0x0001,
            ),
            "media-command-load" to WireFrame(
                1,
                MediaCommandMessage(
                    MediaAction.LOAD,
                    url = "",
                    title = "Demo Clip",
                    mimeType = "video/mp4",
                    durationMs = 12_345,
                    startPositionMs = 0,
                    subtitleUrl = "https://example.test/subs.vtt",
                ),
            ),
            "media-command-clear" to WireFrame(1, MediaCommandMessage(MediaAction.CLEAR)),
            "media-data-chunk" to WireFrame(
                1,
                MediaDataMessage(BinaryData.of(byteArrayOf(0x00, 0x01, 0xfe.toByte(), 0xff.toByte())), false),
            ),
            "media-data-final" to WireFrame(1, MediaDataMessage(BinaryData.EMPTY, true)),
            "surface-idle" to WireFrame(1, SurfaceMessage(SurfaceMode.IDLE)),
            "surface-player" to WireFrame(1, SurfaceMessage(SurfaceMode.PLAYER, "Playing")),
            "playback-state-playing" to WireFrame(
                1,
                PlaybackStateMessage(PlaybackState.PLAYING, 1_000, 12_345),
            ),
            "playback-state-error" to WireFrame(
                1,
                PlaybackStateMessage(PlaybackState.ERROR, detail = "decode failed"),
            ),
            "browser-capability-available" to WireFrame(
                2,
                BrowserCapabilityMessage(
                    BrowserCapabilityStatus.AVAILABLE,
                    8443,
                    25,
                    "120.0.1",
                    true,
                    960,
                    540,
                    5,
                    1,
                    768 * 1024,
                    "ready",
                ),
            ),
            "browser-command-open" to WireFrame(
                2,
                BrowserCommandMessage(1, 2, BrowserCommandAction.OPEN, "https://example.test/start"),
            ),
            "browser-command-preview" to WireFrame(
                2,
                BrowserCommandMessage(1, 3, BrowserCommandAction.SET_PREVIEW_ENABLED, previewEnabled = true),
            ),
            "browser-input-pointer" to WireFrame(
                2,
                BrowserInputMessage(
                    1,
                    1,
                    BrowserPointerInput(BrowserPointerAction.DOWN, 3, 4, 32_767, 65_535, 1),
                ),
            ),
            "browser-input-scroll" to WireFrame(
                2,
                BrowserInputMessage(1, 2, BrowserScrollInput(3, 4, 1, 2, 0, -120)),
            ),
            "browser-input-key" to WireFrame(
                2,
                BrowserInputMessage(1, 3, BrowserSemanticKeyInput(BrowserSemanticKey.SELECT)),
            ),
            "browser-input-text" to WireFrame(
                2,
                BrowserInputMessage(1, 4, BrowserTextInput("A browser input value")),
            ),
            "browser-state-loaded" to WireFrame(
                2,
                BrowserStateMessage(
                    1,
                    7,
                    3,
                    3,
                    4,
                    BrowserLoadState.LOADED,
                    "https://example.test/start",
                    "Example",
                    100,
                    true,
                    false,
                    1920,
                    1080,
                    BrowserPreviewState.DISABLED,
                ),
            ),
            "browser-preview-jpeg" to WireFrame(
                2,
                BrowserPreviewMessage(
                    1,
                    3,
                    9,
                    2,
                    2,
                    BinaryData.of(byteArrayOf(0xff.toByte(), 0xd8.toByte(), 0xff.toByte(), 0xd9.toByte())),
                ),
            ),
            "browser-dialog-confirm" to WireFrame(
                2,
                BrowserDialogMessage(
                    1,
                    11,
                    BrowserDialogType.CONFIRM,
                    "https://example.test",
                    "Leave this page?",
                    "",
                    10_000,
                ),
            ),
            "browser-dialog-reply-accept" to WireFrame(
                2,
                BrowserDialogReplyMessage(1, 11, accepted = true),
            ),
            "surface-browser" to WireFrame(2, SurfaceMessage(SurfaceMode.BROWSER, "Browser")),
        )
        vectors.putAll(GoldenBrowserPhase2Vectors.all)
        vectors.putAll(GoldenBrowserPhase3Vectors.all)
        return vectors
    }
}
