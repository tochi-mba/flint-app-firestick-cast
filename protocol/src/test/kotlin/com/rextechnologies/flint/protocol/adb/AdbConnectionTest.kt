package com.rextechnologies.flint.protocol.adb

import com.rextechnologies.flint.protocol.BinaryData
import java.io.ByteArrayOutputStream
import java.io.PipedInputStream
import java.io.PipedOutputStream
import java.util.concurrent.TimeUnit
import kotlin.test.AfterTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

/** ADB terminates its banner, destination, and key payloads with a NUL byte. */
private const val NUL = '\u0000'

/**
 * Drives [AdbConnection] against a scripted device on the other end of a pipe.
 *
 * The fake speaks the real framing, so these tests exercise the actual message
 * loop rather than a mock of it.
 */
class AdbConnectionTest {
    private val identity = AdbAuth.generateIdentity("rexcast@test")
    private val toDevice = PipedOutputStream()
    private val fromDevice = PipedOutputStream()
    private val deviceInput = PipedInputStream(toDevice, 1 shl 20)
    private val clientInput = PipedInputStream(fromDevice, 1 shl 20)
    private var device: Thread? = null

    private val connection = AdbConnection(clientInput, toDevice, identity)

    @AfterTest
    fun tearDown() {
        device?.interrupt()
        runCatching { connection.close() }
        runCatching { fromDevice.close() }
        device?.join(TimeUnit.SECONDS.toMillis(2))
    }

    /**
     * Waits for the scripted device to finish.
     *
     * The client returns as soon as it sees CLSE, so anything the device records
     * after replying is not yet visible to the assertions without this.
     */
    private fun awaitDevice() {
        val thread = requireNotNull(device) { "No device script is running" }
        thread.join(TimeUnit.SECONDS.toMillis(5))
        check(!thread.isAlive) { "The scripted device did not finish" }
    }

    private fun runDevice(script: DeviceScript.() -> Unit) {
        device = Thread {
            runCatching { DeviceScript(deviceInput, fromDevice).script() }
        }.apply {
            isDaemon = true
            start()
        }
    }

    private class DeviceScript(
        private val input: PipedInputStream,
        private val output: PipedOutputStream,
    ) {
        fun expect(): AdbMessage =
            AdbMessageCodec.readFrom(input) ?: error("Client closed the connection")

        fun send(message: AdbMessage) {
            AdbMessageCodec.writeTo(output, message)
            output.flush()
        }

        /** Drops the transport, which is how an unauthorised device ends the exchange. */
        fun close() = output.close()

        fun banner(text: String = BANNER) = AdbMessage(
            AdbCommands.CNXN,
            AdbMessage.VERSION,
            AdbMessage.DEFAULT_MAX_DATA,
            BinaryData.of((text + NUL).toByteArray()),
        )

        fun authToken() = AdbMessage(
            AdbCommands.AUTH,
            AdbAuthType.TOKEN,
            0,
            BinaryData.of(ByteArray(AdbAuth.TOKEN_BYTES) { it.toByte() }),
        )

        /** Answers one OPEN with [reply] and closes the stream. */
        fun serveStream(reply: String, remoteId: Long = 77): AdbMessage {
            val open = expect()
            send(AdbMessage.okay(remoteId, open.argument0))
            send(AdbMessage.write(remoteId, open.argument0, reply.toByteArray()))
            expect()
            send(AdbMessage.close(remoteId, open.argument0))
            expect()
            return open
        }

        companion object {
            const val BANNER =
                "device::ro.product.name=firetv;ro.product.model=AFTKA;ro.product.device=karaoke;features=cmd"
        }
    }

    @Test
    fun `connect returns the device banner when no authentication is demanded`() {
        runDevice {
            expect()
            send(banner())
        }

        val banner = connection.connect()

        assertEquals("AFTKA", banner.model)
        assertEquals("karaoke", banner.device)
        assertEquals("firetv", banner.name)
        assertEquals("AFTKA", banner.displayName)
        assertEquals(banner, connection.banner)
    }

    @Test
    fun `a recognised signature authorises without showing the TV prompt`() {
        var offeredKey = false
        runDevice {
            expect()
            send(authToken())
            val response = expect()
            offeredKey = response.argument0 == AdbAuthType.RSA_PUBLIC_KEY
            send(banner())
        }

        connection.connect()

        assertFalse(offeredKey, "The key must not be offered before the signature is refused")
    }

    @Test
    fun `a refused signature falls back to offering the public key`() {
        var keyPayload: BinaryData? = null
        runDevice {
            expect()
            send(authToken())
            expect()
            send(authToken())
            keyPayload = expect().payload
            send(banner())
        }

        connection.connect()

        val line = keyPayload?.toByteArray()?.toString(Charsets.US_ASCII).orEmpty()
        assertTrue(line.endsWith("rexcast@test" + NUL), "Expected an ADB public key line, got: $line")
    }

    @Test
    fun `an unauthorised device produces an actionable error`() {
        runDevice {
            expect()
            send(authToken())
            expect()
            send(authToken())
            expect()
            close()
        }

        val failure = assertFailsWith<AdbAuthorizationRequiredException> { connection.connect() }

        assertTrue(failure.message.orEmpty().contains("prompt on the TV"))
    }

    @Test
    fun `an unexpected auth type is refused`() {
        runDevice {
            expect()
            send(AdbMessage(AdbCommands.AUTH, AdbAuthType.SIGNATURE, 0, BinaryData.of(ByteArray(20))))
        }

        assertFailsWith<AdbFormatException> { connection.connect() }
    }

    @Test
    fun `a malformed auth token is refused`() {
        runDevice {
            expect()
            send(AdbMessage(AdbCommands.AUTH, AdbAuthType.TOKEN, 0, BinaryData.of(ByteArray(4))))
        }

        assertFailsWith<AdbFormatException> { connection.connect() }
    }

    @Test
    fun `a device that closes or misbehaves during connect is refused`() {
        runDevice {
            expect()
            send(AdbMessage.close(1, 1))
        }
        assertFailsWith<AdbFormatException> { connection.connect() }
    }

    @Test
    fun `shell returns the device output`() {
        var destination: String? = null
        runDevice {
            expect()
            send(banner())
            destination = serveStream("package:com.rextechnologies.flint.receiver\n").payload
                .toByteArray()
                .toString(Charsets.UTF_8)
                .trimEnd(NUL)
        }
        connection.connect()

        val output = connection.shell("pm")
        awaitDevice()

        assertEquals("shell:pm", destination)
        assertEquals("package:com.rextechnologies.flint.receiver\n", output)
    }

    @Test
    fun `installApk streams the payload and reports success`() {
        val apk = ByteArray(3_000) { (it % 251).toByte() }
        val received = ByteArrayOutputStream()
        var destination: String? = null

        runDevice {
            expect()
            send(banner())
            val open = expect()
            destination = open.payload.toByteArray().toString(Charsets.UTF_8).trimEnd(NUL)
            send(AdbMessage.okay(5, open.argument0))
            while (received.size() < apk.size) {
                val message = expect()
                message.payload.withBytes(received::write)
                send(AdbMessage.okay(5, open.argument0))
            }
            send(AdbMessage.write(5, open.argument0, "Success\n".toByteArray()))
            expect()
            send(AdbMessage.close(5, open.argument0))
            expect()
        }
        connection.connect()

        val result = connection.installApk(apk)
        awaitDevice()

        assertTrue(result.succeeded)
        assertEquals("Success", result.output)
        assertEquals("exec:cmd package install -r -S 3000", destination)
        assertTrue(apk.contentEquals(received.toByteArray()))
    }

    @Test
    fun `a failed install is reported rather than thrown`() {
        runDevice {
            expect()
            send(banner())
            val open = expect()
            send(AdbMessage.okay(5, open.argument0))
            expect()
            send(AdbMessage.write(5, open.argument0, "Failure [INSTALL_FAILED_INVALID_APK]".toByteArray()))
            expect()
            send(AdbMessage.close(5, open.argument0))
            expect()
        }
        connection.connect()

        val result = connection.installApk(byteArrayOf(1, 2, 3), reinstall = false)

        assertFalse(result.succeeded)
        assertTrue(result.output.contains("INSTALL_FAILED_INVALID_APK"))
    }

    @Test
    fun `uninstall and launch use validated names`() {
        runDevice {
            expect()
            send(banner())
            serveStream("Success\n")
            serveStream("Starting: Intent\n")
        }
        connection.connect()

        assertTrue(connection.uninstallPackage("com.rextechnologies.flint.receiver").succeeded)
        assertTrue(connection.launchActivity("com.rextechnologies.flint.receiver/.ReceiverActivity").isNotEmpty())
        awaitDevice()
    }

    @Test
    fun `injection attempts through package, component, and shell arguments are refused`() {
        assertFailsWith<IllegalArgumentException> { connection.uninstallPackage("a b; rm -rf /") }
        assertFailsWith<IllegalArgumentException> { connection.uninstallPackage("nodots") }
        assertFailsWith<IllegalArgumentException> { connection.launchActivity("no-slash") }
        assertFailsWith<IllegalArgumentException> { connection.shell("pm list; evil") }
        assertFailsWith<IllegalArgumentException> { connection.shell("echo $(id)") }
        assertFailsWith<IllegalArgumentException> { connection.shell("a | b") }
        assertFailsWith<IllegalArgumentException> { connection.shell("a && b") }
        assertFailsWith<IllegalArgumentException> { connection.shell("cat <file") }
        assertFailsWith<IllegalArgumentException> { connection.shell("a\nb") }
        assertFailsWith<IllegalArgumentException> { connection.shell("  ") }
        assertFailsWith<IllegalArgumentException> { connection.installApk(ByteArray(0)) }
        assertFailsWith<IllegalArgumentException> { connection.request("") }
    }

    @Test
    fun `a spaced command is allowed because pm and am arguments need it`() {
        var destination: String? = null
        runDevice {
            expect()
            send(banner())
            destination = serveStream("Success\n").payload
                .toByteArray()
                .toString(Charsets.UTF_8)
                .trimEnd(NUL)
        }
        connection.connect()

        connection.shell("pm uninstall com.rextechnologies.flint.receiver")
        awaitDevice()

        assertEquals("shell:pm uninstall com.rextechnologies.flint.receiver", destination)
    }

    @Test
    fun `an unexpected command inside a stream is refused`() {
        runDevice {
            expect()
            send(banner())
            val open = expect()
            send(AdbMessage(AdbCommands.CNXN, 1, 1024, BinaryData.EMPTY).copy(argument1 = open.argument0))
        }
        connection.connect()

        assertFailsWith<AdbFormatException> { connection.shell("pm") }
    }

    @Test
    fun `listing TV activities parses the brief package-manager output`() {
        runDevice {
            expect()
            send(banner())
            serveStream(
                "com.amazon.firebat/.MainActivity\n" +
                    "com.netflix.ninja/.MainActivity\n" +
                    "  com.google.android.youtube.tv/.StartActivity  \n" +
                    "com.netflix.ninja/.MainActivity\n" +
                    "No activities found\n",
            )
        }
        connection.connect()

        val activities = connection.listLeanbackActivities()
        awaitDevice()

        assertEquals(3, activities.size)
        assertEquals(
            listOf("com.amazon.firebat", "com.google.android.youtube.tv", "com.netflix.ninja"),
            activities.map { it.packageName },
        )
        assertEquals("com.netflix.ninja/.MainActivity", activities.last().component)
    }

    @Test
    fun `the phone launcher category is available for non-TV devices`() {
        runDevice {
            expect()
            send(banner())
            serveStream("com.example.app/.Main\n")
        }
        connection.connect()

        assertEquals(1, connection.listLauncherActivities().size)
        awaitDevice()
    }

    @Test
    fun `a TV activity derives a readable name from its package`() {
        assertEquals("Ninja", TvActivity("com.netflix.ninja/.Main", "com.netflix.ninja").displayName)
        assertEquals("Firebat", TvActivity("a/.b", "com.amazon.firebat").displayName)
        assertEquals("Media player", TvActivity("a/.b", "org.x.media_player").displayName)
    }

    @Test
    fun `banner parsing tolerates a device that sends nothing useful`() {
        val empty = AdbBanner("device::")

        assertNull(empty.model)
        assertNull(empty.device)
        assertNull(empty.name)
        assertEquals("Android device", empty.displayName)
        assertNull(AdbBanner("no-separator").model)
    }
}

