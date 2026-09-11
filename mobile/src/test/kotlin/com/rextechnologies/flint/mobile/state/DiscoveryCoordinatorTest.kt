package com.rextechnologies.flint.mobile.state

import com.rextechnologies.flint.castcore.capability.DiscoverySource
import com.rextechnologies.flint.castcore.capability.LocalNetwork
import com.rextechnologies.flint.castcore.capability.ReceiverDevice
import com.rextechnologies.flint.castcore.discovery.DiscoveryRung
import com.rextechnologies.flint.mobile.net.ReceiverFinder
import com.rextechnologies.flint.mobile.net.RungResult
import com.rextechnologies.flint.protocol.network.SelectedHotspotInterface
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.async
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.yield
import java.net.Inet4Address
import java.net.InetAddress
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

/** The coordinator's own decisions, with a finder that never opens a socket. */
class DiscoveryCoordinatorTest {
    private val host = LocalNetwork.PhoneIsHost(
        SelectedHotspotInterface("ap0", 7, InetAddress.getByName("192.168.43.1") as Inet4Address, 24),
    )
    private val stick = ReceiverDevice(
        "192.168.43.31",
        friendlyName = "Fire TV Stick",
        source = DiscoverySource.LINE_PROBE,
    )
    private val cube = ReceiverDevice(
        "192.168.43.40",
        friendlyName = "Fire TV Cube",
        source = DiscoverySource.LINE_PROBE,
    )

    private class Finder(
        var found: List<ReceiverDevice> = emptyList(),
        var manual: ReceiverDevice? = null,
        var samples: List<Double> = listOf(4.0, 5.0, 6.0, 5.0, 4.5),
        val gate: CompletableDeferred<Unit>? = null,
    ) : ReceiverFinder {
        var discoveries = 0

        override suspend fun discover(network: LocalNetwork): List<RungResult> {
            discoveries++
            gate?.await()
            return listOf(RungResult(DiscoveryRung.LINE_PROBE, found, attempted = true))
        }

        override suspend fun probeManual(network: LocalNetwork, address: String): ReceiverDevice? = manual

        override suspend fun measureRoundTripMillis(network: LocalNetwork, device: ReceiverDevice) = samples
    }

    @Test
    fun `a sweep that finds a television selects it and measures the path to it`() = runBlocking {
        val coordinator = DiscoveryCoordinator(Finder(found = listOf(stick)))
        val outcome = coordinator.probe(host)
        assertEquals(LookupState.Found(1), outcome)
        val state = coordinator.state.value
        assertEquals(stick, state.selected)
        assertEquals(listOf(DiscoveryRung.LINE_PROBE), state.rungsAttempted)
        val path = assertNotNull(state.path)
        assertEquals(5.0, path.roundTripMs)
        assertEquals(2.0, path.jitterMs)
        assertEquals(0.0, path.packetLossPercent)
        assertTrue(!path.throughputMeasured)
    }

    @Test
    fun `a sweep that finds nothing says what it tried and keeps what it had`() = runBlocking {
        val finder = Finder(found = listOf(stick))
        val coordinator = DiscoveryCoordinator(finder)
        coordinator.probe(host)

        finder.found = emptyList()
        val outcome = coordinator.probe(host)
        assertEquals(LookupState.FoundNothing(listOf(DiscoveryRung.LINE_PROBE)), outcome)
        // The television that was asleep for four seconds is still the selected one.
        assertEquals(stick, coordinator.state.value.selected)
        assertEquals(listOf(stick), coordinator.state.value.receivers)
    }

    @Test
    fun `a second probe while one is running is refused rather than run twice`() = runBlocking {
        val gate = CompletableDeferred<Unit>()
        val finder = Finder(found = listOf(stick), gate = gate)
        val coordinator = DiscoveryCoordinator(finder)

        val first = async { coordinator.probe(host) }
        while (coordinator.state.value.lookup !is LookupState.Running) yield()
        assertEquals(LookupState.Running, coordinator.probe(host))
        gate.complete(Unit)
        assertIs<LookupState.Found>(first.await())
        assertEquals(1, finder.discoveries)
    }

    @Test
    fun `lost samples become a loss percentage against the attempts actually made`() = runBlocking {
        val finder = Finder(found = listOf(stick), samples = listOf(3.0, 3.0, 3.0, 3.0))
        val coordinator = DiscoveryCoordinator(finder)
        coordinator.probe(host)
        assertEquals(20.0, assertNotNull(coordinator.state.value.path).packetLossPercent)
    }

    @Test
    fun `no samples means no path rather than a path of zeros`() = runBlocking {
        val coordinator = DiscoveryCoordinator(Finder(found = listOf(stick), samples = emptyList()))
        coordinator.probe(host)
        assertNull(coordinator.state.value.path)
    }

    @Test
    fun `choosing another television discards the path measured to the first`() = runBlocking {
        val coordinator = DiscoveryCoordinator(Finder(found = listOf(stick, cube)))
        coordinator.probe(host)
        assertNotNull(coordinator.state.value.path)
        coordinator.select(cube)
        assertEquals(cube, coordinator.state.value.selected)
        assertNull(coordinator.state.value.path)
    }

    @Test
    fun `a typed address that answers becomes the selection and joins the list once`() = runBlocking {
        val manual = stick.copy(source = DiscoverySource.MANUAL)
        val coordinator = DiscoveryCoordinator(Finder(found = listOf(stick), manual = manual))
        coordinator.probe(host)
        assertEquals(manual, coordinator.probeManual(host, " 192.168.43.31 "))
        assertEquals(LookupState.Found(1), coordinator.state.value.manualLookup)
        assertEquals(1, coordinator.state.value.receivers.size)
        assertEquals(DiscoverySource.MANUAL, coordinator.state.value.receivers.single().source)
    }

    @Test
    fun `a typed address that does not answer is remembered as a failed lookup`() = runBlocking {
        val coordinator = DiscoveryCoordinator(Finder())
        assertNull(coordinator.probeManual(host, "192.168.43.99"))
        assertEquals(
            LookupState.FoundNothing(listOf(DiscoveryRung.MANUAL)),
            coordinator.state.value.manualLookup,
        )
        assertTrue(coordinator.state.value.receivers.isEmpty())
    }
}
