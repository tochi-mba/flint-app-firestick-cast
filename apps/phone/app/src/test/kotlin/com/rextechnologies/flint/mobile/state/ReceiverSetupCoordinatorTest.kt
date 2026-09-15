package com.rextechnologies.flint.mobile.state

import com.rextechnologies.flint.castcore.capability.AdbConnectionState
import com.rextechnologies.flint.castcore.capability.LocalNetwork
import com.rextechnologies.flint.castcore.capability.ReceiverDevice
import com.rextechnologies.flint.castcore.capability.ReceiverPlatform
import com.rextechnologies.flint.castcore.setup.BundledReceiver
import com.rextechnologies.flint.castcore.setup.InstallEvent
import com.rextechnologies.flint.castcore.setup.ReceiverInstallStage
import com.rextechnologies.flint.mobile.net.AdbAnswer
import com.rextechnologies.flint.mobile.net.ReceiverInstaller
import com.rextechnologies.flint.protocol.network.SelectedHotspotInterface
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.yield
import java.net.Inet4Address
import java.net.InetAddress
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

/** The coordinator's own decisions, with an installer that never opens a socket. */
class ReceiverSetupCoordinatorTest {
    private val host = LocalNetwork.PhoneIsHost(
        SelectedHotspotInterface("ap0", 7, InetAddress.getByName("192.168.43.1") as Inet4Address, 24),
    )
    private val stick = ReceiverDevice("192.168.43.31", friendlyName = "Fire TV Stick")
    private val cube = ReceiverDevice("192.168.43.40", friendlyName = "Fire TV Cube")
    private val bundle = BundledReceiver(BundledReceiver.DEBUG_PACKAGE, "0.3.0", 3, 7_340_032)
    private val apk = ByteArray(2_048) { it.toByte() }
    private val scope = CoroutineScope(Dispatchers.Unconfined)

    private class Installer(
        var answer: AdbAnswer = AdbAnswer.Identified(5_555, "AFTKA", "11", 30, ReceiverPlatform.FIRE_OS_8, null),
        var installEvent: InstallEvent = InstallEvent.InstallSucceeded,
        var removeEvent: InstallEvent = InstallEvent.RemoveSucceeded,
        val gate: CompletableDeferred<Unit>? = null,
    ) : ReceiverInstaller {
        val askedFor = mutableListOf<Set<String>>()
        val knownPorts = mutableListOf<Int>()
        var installed: Pair<ByteArray, String>? = null
        var removed: String? = null

        override suspend fun identify(
            network: LocalNetwork,
            address: String,
            knownPort: Int,
            receiverPackages: Set<String>,
        ): AdbAnswer {
            askedFor += receiverPackages
            knownPorts += knownPort
            gate?.await()
            return answer
        }

        override suspend fun install(
            network: LocalNetwork,
            address: String,
            knownPort: Int,
            apk: ByteArray,
            packageName: String,
        ): InstallEvent {
            installed = apk to packageName
            gate?.await()
            return installEvent
        }

        override suspend fun remove(
            network: LocalNetwork,
            address: String,
            knownPort: Int,
            packageName: String,
        ): InstallEvent {
            removed = packageName
            return removeEvent
        }
    }

    private fun coordinator(
        installer: Installer,
        bundled: BundledReceiver? = bundle,
        staged: ByteArray? = apk,
    ) = ReceiverSetupCoordinator(installer, scope, { bundled }, { staged })

    @Test
    fun `identifying a television updates the device and the stage from what it said`() = runBlocking<Unit> {
        val installer = Installer(
            answer = AdbAnswer.Identified(
                5_557,
                "AFTKA",
                "11",
                30,
                ReceiverPlatform.FIRE_OS_8,
                BundledReceiver.DEBUG_PACKAGE,
            ),
        )
        val coordinator = coordinator(installer)
        coordinator.load()
        yield()

        val look = coordinator.identify(host, stick.copy(adbPort = 5_560))

        assertEquals(listOf(5_560), installer.knownPorts)
        assertEquals(setOf(BundledReceiver.DEBUG_PACKAGE, BundledReceiver.RELEASE_PACKAGE), installer.askedFor.single())
        assertEquals(ReceiverPlatform.FIRE_OS_8, look.device.platform)
        assertEquals(AdbConnectionState.CONNECTED, look.device.adbState)
        assertEquals("AFTKA", look.device.model)
        assertEquals("11", look.device.androidRelease)
        assertEquals(30, look.device.androidApiLevel)
        assertEquals(5_557, look.device.adbPort)
        val state = coordinator.state.value
        assertEquals(ReceiverInstallStage.Installed, state.stage)
        assertEquals(stick.address, state.address)
        assertEquals(BundledReceiver.DEBUG_PACKAGE, state.installedPackage)
        assertTrue(!state.busy)
    }

    @Test
    fun `a television holding its prompt is waited for, and the port it answered on is kept`() = runBlocking<Unit> {
        val coordinator = coordinator(Installer(answer = AdbAnswer.Unauthorised(5_556)))
        val look = coordinator.identify(host, stick)
        assertEquals(AdbConnectionState.UNAUTHORIZED, look.device.adbState)
        assertEquals(5_556, look.device.adbPort)
        assertEquals(ReceiverInstallStage.AwaitingAuthorisation, coordinator.state.value.stage)
    }

    @Test
    fun `vega is impossible, and stays so`() = runBlocking<Unit> {
        val installer =
            Installer(answer = AdbAnswer.Identified(5_555, "AFTCA002", "14", 34, ReceiverPlatform.VEGA, null))
        val coordinator = coordinator(installer)
        assertEquals(ReceiverPlatform.VEGA, coordinator.identify(host, stick).device.platform)
        assertEquals(ReceiverInstallStage.Impossible, coordinator.state.value.stage)
        installer.answer = AdbAnswer.Identified(5_555, "AFTKA", "11", 30, ReceiverPlatform.FIRE_OS_8, null)
        coordinator.identify(host, stick)
        assertEquals(ReceiverInstallStage.Impossible, coordinator.state.value.stage)
    }

    @Test
    fun `a refusal and a silence are recorded on the device and change nothing on the card`() = runBlocking<Unit> {
        val installer = Installer(answer = AdbAnswer.Refused("refused every port"))
        val coordinator = coordinator(installer)
        assertEquals(AdbConnectionState.REFUSED, coordinator.identify(host, stick).device.adbState)
        assertEquals(ReceiverInstallStage.Unknown, coordinator.state.value.stage)
        installer.answer = AdbAnswer.Silent("nothing answered")
        assertEquals(AdbConnectionState.TIMED_OUT, coordinator.identify(host, stick).device.adbState)
        assertEquals(ReceiverInstallStage.Unknown, coordinator.state.value.stage)
    }

    @Test
    fun `installing sends the staged bytes under the bundled name and reaches installed`() = runBlocking<Unit> {
        val installer = Installer()
        val coordinator = coordinator(installer)
        coordinator.load()
        yield()

        val event = coordinator.install(host, stick)

        assertEquals(InstallEvent.InstallSucceeded, event)
        val (bytes, name) = assertNotNull(installer.installed)
        assertContentEquals(apk, bytes)
        assertEquals(BundledReceiver.DEBUG_PACKAGE, name)
        assertEquals(ReceiverInstallStage.Installed, coordinator.state.value.stage)
        assertEquals(BundledReceiver.DEBUG_PACKAGE, coordinator.state.value.installedPackage)
    }

    @Test
    fun `the card says installing while the bytes are in flight`() = runBlocking<Unit> {
        val gate = CompletableDeferred<Unit>()
        val coordinator = coordinator(Installer(gate = gate))
        coordinator.load()
        yield()

        val pending = async { coordinator.install(host, stick) }
        while (coordinator.state.value.stage != ReceiverInstallStage.Installing) yield()
        assertTrue(coordinator.state.value.busy)
        gate.complete(Unit)
        pending.await()
        assertTrue(!coordinator.state.value.busy)
    }

    @Test
    fun `a failed install keeps the television's words`() = runBlocking<Unit> {
        val coordinator =
            coordinator(Installer(installEvent = InstallEvent.InstallFailed("Failure [INSTALL_FAILED_OLDER_SDK]")))
        coordinator.load()
        yield()
        coordinator.install(host, stick)
        assertEquals(ReceiverInstallStage.Failed("Failure [INSTALL_FAILED_OLDER_SDK]"), coordinator.state.value.stage)
    }

    @Test
    fun `nothing is installed from a build that bundles nothing or cannot read its package`() = runBlocking<Unit> {
        val installer = Installer()
        assertIs<InstallEvent.InstallFailed>(coordinator(installer, bundled = null).install(host, stick))
        val unreadable = coordinator(installer, staged = null)
        unreadable.load()
        yield()
        assertIs<InstallEvent.InstallFailed>(unreadable.install(host, stick))
        assertNull(installer.installed)
    }

    @Test
    fun `removal names the package the television was seen to have, and needs to have seen one`() = runBlocking<Unit> {
        val installer = Installer(
            answer = AdbAnswer.Identified(
                5_555,
                "AFTKA",
                "11",
                30,
                ReceiverPlatform.FIRE_OS_8,
                BundledReceiver.RELEASE_PACKAGE,
            ),
        )
        val coordinator = coordinator(installer)
        assertIs<InstallEvent.RemoveFailed>(coordinator.remove(host, stick))
        assertNull(installer.removed)

        coordinator.identify(host, stick)
        assertEquals(InstallEvent.RemoveSucceeded, coordinator.remove(host, stick))
        assertEquals(BundledReceiver.RELEASE_PACKAGE, installer.removed)
        assertEquals(ReceiverInstallStage.NotInstalled, coordinator.state.value.stage)
        assertNull(coordinator.state.value.installedPackage)
    }

    @Test
    fun `a different television is a different question`() = runBlocking<Unit> {
        val installer = Installer(
            answer = AdbAnswer.Identified(
                5_555,
                "AFTKA",
                "11",
                30,
                ReceiverPlatform.FIRE_OS_8,
                BundledReceiver.RELEASE_PACKAGE,
            ),
        )
        val coordinator = coordinator(installer)
        coordinator.identify(host, stick)
        assertEquals(ReceiverInstallStage.Installed, coordinator.state.value.stage)

        installer.answer = AdbAnswer.Refused("refused")
        coordinator.identify(host, cube)
        assertEquals(ReceiverInstallStage.Unknown, coordinator.state.value.stage)
        assertEquals(cube.address, coordinator.state.value.address)
        assertNull(coordinator.state.value.installedPackage)
    }

    @Test
    fun `one conversation at a time`() = runBlocking<Unit> {
        val gate = CompletableDeferred<Unit>()
        val coordinator = coordinator(Installer(gate = gate))
        val first = async { coordinator.identify(host, stick) }
        while (!coordinator.state.value.busy) yield()
        val second = coordinator.identify(host, stick)
        assertIs<AdbAnswer.Failed>(second.answer)
        gate.complete(Unit)
        assertIs<AdbAnswer.Identified>(first.await().answer)
    }
}
