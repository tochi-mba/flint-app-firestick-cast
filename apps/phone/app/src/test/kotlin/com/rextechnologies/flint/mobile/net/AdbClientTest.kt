package com.rextechnologies.flint.mobile.net

import com.rextechnologies.flint.castcore.capability.LocalNetwork
import com.rextechnologies.flint.castcore.capability.ReceiverPlatform
import com.rextechnologies.flint.castcore.setup.BundledReceiver
import com.rextechnologies.flint.castcore.setup.InstallEvent
import com.rextechnologies.flint.protocol.BinaryData
import com.rextechnologies.flint.protocol.adb.AdbAuth
import com.rextechnologies.flint.protocol.adb.AdbAuthType
import com.rextechnologies.flint.protocol.adb.AdbCommands
import com.rextechnologies.flint.protocol.adb.AdbMessage
import com.rextechnologies.flint.protocol.adb.AdbMessageCodec
import kotlinx.coroutines.runBlocking
import java.io.BufferedInputStream
import java.io.BufferedOutputStream
import java.io.ByteArrayOutputStream
import java.io.Closeable
import java.io.InputStream
import java.io.OutputStream
import java.net.Inet4Address
import java.net.InetAddress
import java.net.ServerSocket
import java.net.Socket
import java.nio.charset.StandardCharsets
import java.util.Collections
import java.util.concurrent.atomic.AtomicInteger
import kotlin.concurrent.thread
import kotlin.test.AfterTest
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertTrue

/** ADB terminates its banner and destination payloads with a NUL byte. */
private const val NUL = '\u0000'

/**
 * Drives [AdbClient] against a scripted device listening on loopback.
 *
 * The fake speaks the real framing over a real socket, so the scan, the greeting, the
 * authorisation wait and the install stream are the production code paths rather than mocks.
 */
class AdbClientTest {
    private val loopback = InetAddress.getLoopbackAddress() as Inet4Address
    private val network = LocalNetwork.PhoneIsClient("lo", 1, loopback, 8)
    private val identity = AdbAuth.generateIdentity("flint-mobile@test")
    private val devices = mutableListOf<FakeDevice>()

    @AfterTest
    fun tearDown() = devices.forEach { it.close() }

    private fun device(
        authorised: Boolean = true,
        packages: Set<String> = emptySet(),
        installReply: String = "Success",
        uninstallReply: String = "Success",
    ): FakeDevice = FakeDevice(authorised, packages.toMutableSet(), installReply, uninstallReply).also { devices += it }

    // The fake device answers from threads of its own, and a machine busy with other builds can take
    // longer than 400ms to schedule one, which read as a television that went quiet mid-greeting. Two
    // seconds leaves room for that. The two tests where the television holds its prompt wait out the
    // whole read timeout by design, and that wait is the price of the margin.
    private fun client(vararg ports: Int) = AdbClient(
        identity = { identity },
        connectTimeoutMillis = 1_000,
        readTimeoutMillis = 2_000,
        installTimeoutMillis = 5_000,
        portsFor = { ports.toList() },
    )

    private fun closedPort(): Int = ServerSocket(0, 1, loopback).let { socket ->
        socket.localPort.also { socket.close() }
    }

    @Test
    fun `identifying reads the platform, the model and the receiver package`() = runBlocking<Unit> {
        val device = device(packages = setOf("com.example.other", BundledReceiver.DEBUG_PACKAGE))
        val answer = client(device.port).identify(network, "127.0.0.1", 0, BundledReceiver.KNOWN_PACKAGES)

        val identified = assertIs<AdbAnswer.Identified>(answer)
        assertEquals(device.port, identified.port)
        assertEquals("AFTKA", identified.model)
        assertEquals("11", identified.androidRelease)
        assertEquals(30, identified.apiLevel)
        assertEquals(ReceiverPlatform.FIRE_OS_8, identified.platform)
        assertEquals(BundledReceiver.DEBUG_PACKAGE, identified.installedPackage)
        assertEquals("0.7.0", identified.installedVersionName)
        assertEquals(7, identified.installedVersionCode)
        assertTrue(identified.receiverInstalled)
        assertTrue(device.commands.any { it == "shell:getprop ro.build.version.sdk" }, device.commands.toString())
    }

    @Test
    fun `a television holding its prompt is reported as waiting, with the port that answered`() = runBlocking<Unit> {
        val device = device(authorised = false)
        val answer = client(device.port).identify(network, "127.0.0.1", 0, BundledReceiver.KNOWN_PACKAGES)
        assertEquals(AdbAnswer.Unauthorised(device.port), answer)
        assertEquals(1, device.keysOffered.get())
    }

    @Test
    fun `the greeting is made once per look, however many ports are open`() = runBlocking<Unit> {
        val first = device()
        val second = device()
        val answer = client(first.port, second.port).identify(network, "127.0.0.1", 0, emptySet())
        assertIs<AdbAnswer.Identified>(answer)
        assertEquals(1, first.greetings.get())
        assertEquals(0, second.greetings.get())
    }

    @Test
    fun `a port that refuses is a refusal, and says so with the address`() = runBlocking<Unit> {
        val answer = client(closedPort()).identify(network, "127.0.0.1", 0, emptySet())
        val refused = assertIs<AdbAnswer.Refused>(answer)
        assertTrue(refused.detail.contains("127.0.0.1"), refused.detail)
    }

    @Test
    fun `every port silent is silence, every port refused is a refusal`() {
        assertIs<AdbAnswer.Silent>(
            AdbClient.closedVerdict("192.168.43.31", refused = 0, silent = 31, timeoutMillis = 1_500),
        )
        val refused = assertIs<AdbAnswer.Refused>(
            AdbClient.closedVerdict("192.168.43.31", refused = 3, silent = 28, timeoutMillis = 1_500),
        )
        assertTrue(refused.detail.contains("5555") && refused.detail.contains("5585"), refused.detail)
    }

    @Test
    fun `no local network is silence rather than a socket bound to nothing`() = runBlocking<Unit> {
        val device = device()
        assertIs<AdbAnswer.Silent>(
            client(device.port).identify(LocalNetwork.NoLocalNetwork, "127.0.0.1", 0, emptySet()),
        )
        assertEquals(0, device.greetings.get())
    }

    @Test
    fun `installing streams every byte, confirms the listing and opens the receiver`() = runBlocking<Unit> {
        val device = device()
        val apk = ByteArray(10_000) { (it * 7).toByte() }
        val event = client(device.port).install(network, "127.0.0.1", 0, apk, BundledReceiver.DEBUG_PACKAGE)

        assertEquals(InstallEvent.InstallSucceeded, event)
        assertContentEquals(apk, device.received.toByteArray())
        assertTrue(
            device.commands.any {
                it.startsWith("exec:cmd package install -r -S 10000")
            },
            device.commands.toString(),
        )
        assertTrue(
            device.commands.any { it == "shell:am start -n ${BundledReceiver.DEBUG_PACKAGE}/.ReceiverActivity" },
            device.commands.toString(),
        )
    }

    @Test
    fun `a refused install keeps the package manager's words`() = runBlocking<Unit> {
        val device = device(installReply = "Failure [INSTALL_FAILED_OLDER_SDK]")
        val event = client(device.port).install(network, "127.0.0.1", 0, ByteArray(64), BundledReceiver.DEBUG_PACKAGE)
        assertEquals(InstallEvent.InstallFailed("Failure [INSTALL_FAILED_OLDER_SDK]"), event)
    }

    @Test
    fun `an install that is not listed afterwards is not an install`() = runBlocking<Unit> {
        val device = device(installReply = "Success, apparently")
        device.listAfterInstall = false
        val event = client(device.port).install(network, "127.0.0.1", 0, ByteArray(64), BundledReceiver.DEBUG_PACKAGE)
        val failed = assertIs<InstallEvent.InstallFailed>(event)
        assertTrue(failed.detail.contains("not listed"), failed.detail)
    }

    @Test
    fun `installing on a television that has not authorised the phone waits for it`() = runBlocking<Unit> {
        val device = device(authorised = false)
        val event = client(device.port).install(network, "127.0.0.1", 0, ByteArray(64), BundledReceiver.DEBUG_PACKAGE)
        assertEquals(InstallEvent.AuthorisationRequired, event)
    }

    @Test
    fun `removal reports the television's answer either way`() = runBlocking<Unit> {
        val device = device(packages = setOf(BundledReceiver.RELEASE_PACKAGE))
        val removed = client(device.port).remove(network, "127.0.0.1", 0, BundledReceiver.RELEASE_PACKAGE)
        assertEquals(InstallEvent.RemoveSucceeded, removed)
        assertTrue(device.commands.any { it == "shell:pm uninstall ${BundledReceiver.RELEASE_PACKAGE}" })

        val refusing = device(uninstallReply = "Failure [DELETE_FAILED_INTERNAL_ERROR]")
        val event = client(refusing.port).remove(network, "127.0.0.1", 0, BundledReceiver.RELEASE_PACKAGE)
        assertEquals(InstallEvent.RemoveFailed("Failure [DELETE_FAILED_INTERNAL_ERROR]"), event)
    }

    @Test
    fun `an unreachable television is unreachable for install and removal alike`() = runBlocking<Unit> {
        val closed = closedPort()
        assertIs<InstallEvent.Unreachable>(client(closed).install(network, "127.0.0.1", 0, ByteArray(64), "a.b"))
        assertIs<InstallEvent.Unreachable>(client(closed).remove(network, "127.0.0.1", 0, "a.b"))
    }

    /** A device that speaks ADB's framing over a loopback socket. */
    private class FakeDevice(
        private val authorised: Boolean,
        private val packages: MutableSet<String>,
        private val installReply: String,
        private val uninstallReply: String,
    ) : Closeable {
        private val server = ServerSocket(0, 8, InetAddress.getLoopbackAddress())
        val port: Int = server.localPort
        val commands: MutableList<String> = Collections.synchronizedList(mutableListOf())
        val received = ByteArrayOutputStream()
        val greetings = AtomicInteger()
        val keysOffered = AtomicInteger()

        @Volatile
        var listAfterInstall = true

        private val acceptor = thread(isDaemon = true, name = "fake-adbd-$port") {
            while (!server.isClosed) {
                val socket = runCatching { server.accept() }.getOrNull() ?: break
                thread(isDaemon = true) { runCatching { serve(socket) } }
            }
        }

        override fun close() {
            runCatching { server.close() }
            acceptor.join(2_000)
        }

        private fun serve(socket: Socket) = socket.use { serve(it.getInputStream(), it.getOutputStream()) }

        private fun serve(rawInput: InputStream, rawOutput: OutputStream) {
            val input = BufferedInputStream(rawInput)
            val output = BufferedOutputStream(rawOutput)
            // The scan connects and closes without a word; only a greeting counts.
            val greeting = read(input) ?: return
            check(greeting.command == AdbCommands.CNXN)
            greetings.incrementAndGet()
            send(output, authToken())
            val signature = read(input) ?: return
            check(signature.argument0 == AdbAuthType.SIGNATURE)
            if (!authorised) {
                send(output, authToken())
                val key = read(input) ?: return
                check(key.argument0 == AdbAuthType.RSA_PUBLIC_KEY)
                keysOffered.incrementAndGet()
                // The prompt is on screen. Nothing is said until the client gives up.
                input.read()
                return
            }
            send(output, banner())
            while (true) {
                val open = read(input) ?: return
                if (open.command != AdbCommands.OPEN) continue
                val destination = String(open.payload.toByteArray(), StandardCharsets.UTF_8).trimEnd(NUL)
                commands += destination
                val localId = open.argument0
                send(output, AdbMessage.okay(REMOTE_ID, localId))
                if (destination.startsWith("exec:cmd package install")) {
                    val size = destination.substringAfterLast("-S ").trim().toInt()
                    var got = 0
                    while (got < size) {
                        val chunk = read(input) ?: return
                        if (chunk.command != AdbCommands.WRTE) continue
                        val bytes = chunk.payload.toByteArray()
                        received.write(bytes)
                        got += bytes.size
                        send(output, AdbMessage.okay(REMOTE_ID, localId))
                    }
                    if (installReply.startsWith("Success") &&
                        listAfterInstall
                    ) {
                        packages += BundledReceiver.DEBUG_PACKAGE
                    }
                }
                send(output, AdbMessage.write(REMOTE_ID, localId, reply(destination).toByteArray()))
                read(input) ?: return
                send(output, AdbMessage.close(REMOTE_ID, localId))
                read(input) ?: return
            }
        }

        private fun reply(destination: String): String = when {
            destination == "shell:getprop ro.build.version.sdk" -> "30\n"

            destination == "shell:getprop ro.build.version.release" -> "11\n"

            destination == "shell:getprop ro.product.model" -> "AFTKA\n"

            destination.startsWith("shell:pm list packages") ->
                packages.joinToString("") { "package:$it\n" }

            destination.startsWith("shell:dumpsys package ") ->
                "  versionCode=7 minSdk=21 targetSdk=35\n  versionName=0.7.0\n"

            destination.startsWith("shell:pm uninstall ") -> {
                if (uninstallReply.startsWith("Success")) packages -= destination.removePrefix("shell:pm uninstall ")
                "$uninstallReply\n"
            }

            destination.startsWith("exec:cmd package install") -> "$installReply\n"

            destination.startsWith("shell:am start") -> "Starting: Intent\n"

            else -> ""
        }

        private fun read(input: InputStream): AdbMessage? = runCatching { AdbMessageCodec.readFrom(input) }.getOrNull()

        private fun send(output: OutputStream, message: AdbMessage) {
            AdbMessageCodec.writeTo(output, message)
            output.flush()
        }

        private fun banner() = AdbMessage(
            AdbCommands.CNXN,
            AdbMessage.VERSION,
            AdbMessage.DEFAULT_MAX_DATA,
            BinaryData.of(("device::ro.product.name=firetv;ro.product.model=AFTKA;features=cmd" + NUL).toByteArray()),
        )

        private fun authToken() = AdbMessage(
            AdbCommands.AUTH,
            AdbAuthType.TOKEN,
            0,
            BinaryData.of(ByteArray(AdbAuth.TOKEN_BYTES) { it.toByte() }),
        )

        private companion object {
            const val REMOTE_ID = 77L
        }
    }
}
