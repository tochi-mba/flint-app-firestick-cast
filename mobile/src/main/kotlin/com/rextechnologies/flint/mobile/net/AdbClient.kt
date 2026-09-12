package com.rextechnologies.flint.mobile.net

import com.rextechnologies.flint.castcore.capability.LocalNetwork
import com.rextechnologies.flint.castcore.capability.ReceiverPlatform
import com.rextechnologies.flint.castcore.setup.AdbPortScan
import com.rextechnologies.flint.castcore.setup.BundledReceiver
import com.rextechnologies.flint.castcore.setup.FireOsPlatformResolver
import com.rextechnologies.flint.castcore.setup.InstallEvent
import com.rextechnologies.flint.protocol.adb.AdbAuthorizationRequiredException
import com.rextechnologies.flint.protocol.adb.AdbConnection
import com.rextechnologies.flint.protocol.adb.AdbFormatException
import com.rextechnologies.flint.protocol.adb.AdbIdentity
import com.rextechnologies.flint.protocol.network.InterfaceSocketBinder
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withPermit
import kotlinx.coroutines.withContext
import java.io.BufferedInputStream
import java.io.BufferedOutputStream
import java.io.IOException
import java.net.ConnectException
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.Socket
import java.net.SocketTimeoutException

/** What one look at an address over ADB established. */
sealed interface AdbAnswer {
    /** Connected and authorised, and the television answered the read-only questions. */
    data class Identified(
        val port: Int,
        val model: String,
        val androidRelease: String,
        val apiLevel: Int?,
        val platform: ReceiverPlatform,
        /** The Flint receiver package the television lists, or `null` when it lists none. */
        val installedPackage: String?,
    ) : AdbAnswer {
        val receiverInstalled: Boolean
            get() = installedPackage != null
    }

    /** ADB answered on [port], and the television is holding its authorisation prompt. */
    data class Unauthorised(val port: Int) : AdbAnswer

    /** Something is there: it closed every port, or answered one with something that is not ADB. */
    data class Refused(val detail: String) : AdbAnswer

    /** Nothing answered on any port. */
    data class Silent(val detail: String) : AdbAnswer

    /** ADB answered on [port] but the exchange broke before it said anything useful. */
    data class Failed(val port: Int, val detail: String) : AdbAnswer
}

/**
 * The three things this phone does to a television over ADB, without saying how.
 *
 * The coordinator above depends on this rather than on [AdbClient] so its own decisions -- what an
 * answer does to the card, what it does to the device in the list -- can be tested with a fake that
 * never opens a socket.
 */
interface ReceiverInstaller {
    /** Read-only. Asks the television what it is and whether one of [receiverPackages] is on it. */
    suspend fun identify(
        network: LocalNetwork,
        address: String,
        knownPort: Int,
        receiverPackages: Set<String>,
    ): AdbAnswer

    suspend fun install(
        network: LocalNetwork,
        address: String,
        knownPort: Int,
        apk: ByteArray,
        packageName: String,
    ): InstallEvent

    suspend fun remove(
        network: LocalNetwork,
        address: String,
        knownPort: Int,
        packageName: String,
    ): InstallEvent
}

/**
 * ADB over the network, to one television, on the interface the cast uses.
 *
 * Every socket is bound to the network's address rather than left to the process default, for the
 * same reason as every other socket in this app: while the phone tethers from mobile data, the
 * default route is the cellular network, and an unbound socket goes there and reaches nothing.
 *
 * One address, the bounded port range, never a sweep. The ports are checked for a listener
 * concurrently and cheaply first; the ADB greeting, which is what puts the authorisation prompt on
 * the television, is then made on one port at a time and stops at the first that answers as ADB.
 * A television is asked to authorise this phone once per look, not once per port.
 */
class AdbClient(
    private val identity: () -> AdbIdentity,
    private val connectTimeoutMillis: Int = CONNECT_TIMEOUT_MILLIS,
    private val readTimeoutMillis: Int = READ_TIMEOUT_MILLIS,
    private val installTimeoutMillis: Int = INSTALL_TIMEOUT_MILLIS,
    private val portsFor: (Int) -> List<Int> = AdbPortScan::order,
) : ReceiverInstaller {
    override suspend fun identify(
        network: LocalNetwork,
        address: String,
        knownPort: Int,
        receiverPackages: Set<String>,
    ): AdbAnswer = withDevice<AdbAnswer>(network, address, knownPort) { session ->
        val connection: AdbConnection = session.connection
        val apiLevel = connection.shell("getprop ro.build.version.sdk").trim().toIntOrNull()
        val release = connection.shell("getprop ro.build.version.release").trim()
        val model = connection.shell("getprop ro.product.model").trim().ifBlank { connection.banner?.model.orEmpty() }
        val listed = listedPackages(connection)
        AdbAnswer.Identified(
            port = session.port,
            model = model,
            androidRelease = release,
            apiLevel = apiLevel,
            platform = FireOsPlatformResolver.resolve(provesAndroid = true, apiLevel = apiLevel, buildModel = model),
            installedPackage = receiverPackages.firstOrNull { it in listed },
        )
    }.answerOrElse { it }

    override suspend fun install(
        network: LocalNetwork,
        address: String,
        knownPort: Int,
        apk: ByteArray,
        packageName: String,
    ): InstallEvent = withDevice<InstallEvent>(network, address, knownPort) { session ->
        // The package manager answers once it has read every byte and finished installing, which
        // on a Fire TV can be tens of seconds after the last byte went out.
        session.readTimeout(installTimeoutMillis)
        val result = session.connection.installApk(apk)
        if (!result.succeeded) {
            return@withDevice InstallEvent.InstallFailed(result.output.take(DETAIL_LIMIT).ifBlank { NO_REASON })
        }
        // "Success" is what the installer said. Listing the package afterwards is what proves it.
        if (packageName !in listedPackages(session.connection)) {
            return@withDevice InstallEvent.InstallFailed(
                "The installer answered Success, but $packageName is not listed afterwards.",
            )
        }
        // Opened so that it answers on its port at once, which is what the disclosure promised and
        // what the Cast screen is about to look for. A launch that fails is not an install that
        // failed: the package is there, and the television's own launcher can open it.
        session.readTimeout(readTimeoutMillis)
        runCatching { session.connection.launchActivity("$packageName/${BundledReceiver.MAIN_ACTIVITY}") }
        InstallEvent.InstallSucceeded
    }.answerOrElse { it.toInstallEvent() }

    override suspend fun remove(
        network: LocalNetwork,
        address: String,
        knownPort: Int,
        packageName: String,
    ): InstallEvent = withDevice<InstallEvent>(network, address, knownPort) { session ->
        val result = session.connection.uninstallPackage(packageName)
        if (result.succeeded) {
            InstallEvent.RemoveSucceeded
        } else {
            InstallEvent.RemoveFailed(result.output.take(DETAIL_LIMIT).ifBlank { NO_REASON })
        }
    }.answerOrElse { it.toInstallEvent() }

    /** One open, authorised ADB connection and the port it is on. */
    private class Session(val connection: AdbConnection, val port: Int, private val socket: Socket) {
        fun readTimeout(millis: Int) {
            socket.soTimeout = millis
        }
    }

    /** What the scan found, or what [block] made of the connection it opened. */
    private sealed interface Reached<out T> {
        data class Answered<T>(val value: T) : Reached<T>

        /** The look ended before [Answered] could be reached, and this is why. */
        data class Elsewhere(val answer: AdbAnswer) : Reached<Nothing>
    }

    private inline fun <T> Reached<T>.answerOrElse(elsewhere: (AdbAnswer) -> T): T = when (this) {
        is Reached.Answered -> value
        is Reached.Elsewhere -> elsewhere(answer)
    }

    /**
     * Finds the port ADB answers on, greets it, and hands the connection to [block].
     *
     * Returns [AdbAnswer] for every way of not getting that far, and whatever [block] produced
     * once it did. A block that throws an [IOException] is reported as [AdbAnswer.Failed].
     */
    private suspend fun <T> withDevice(
        network: LocalNetwork,
        address: String,
        knownPort: Int,
        block: (Session) -> T,
    ): Reached<T> = withContext(Dispatchers.IO) {
        val bound = network.boundAddress
            ?: return@withContext Reached.Elsewhere(AdbAnswer.Silent("This phone is not on a local network."))
        val host = runCatching { InetAddress.getByName(address) }.getOrNull()
            ?: return@withContext Reached.Elsewhere(AdbAnswer.Silent("$address is not an address."))
        val binder = InterfaceSocketBinder(bound)

        val ports = when (val open = openPorts(binder, host, address, knownPort)) {
            is Reached.Elsewhere -> return@withContext open
            is Reached.Answered -> open.value
        }

        var lastRefusal: String? = null
        for (port in ports) {
            val socket = runCatching { connectTo(binder, host, port) }.getOrNull() ?: continue
            try {
                val connection = AdbConnection(
                    BufferedInputStream(socket.getInputStream(), BUFFER_BYTES),
                    BufferedOutputStream(socket.getOutputStream(), BUFFER_BYTES),
                    identity(),
                )
                try {
                    connection.connect()
                } catch (denied: AdbAuthorizationRequiredException) {
                    return@withContext Reached.Elsewhere(AdbAnswer.Unauthorised(port))
                } catch (notAdb: AdbFormatException) {
                    // Something listens on this port and it is not ADB, or it closed at once. The
                    // next port may be the real one.
                    lastRefusal = "$address answered on port $port, but not as ADB: ${reason(notAdb)}"
                    continue
                } catch (broken: IOException) {
                    lastRefusal = "$address answered on port $port, then went quiet: ${reason(broken)}"
                    continue
                }
                return@withContext try {
                    Reached.Answered(block(Session(connection, port, socket)))
                } catch (failure: IOException) {
                    Reached.Elsewhere(AdbAnswer.Failed(port, reason(failure)))
                } catch (failure: IllegalArgumentException) {
                    Reached.Elsewhere(AdbAnswer.Failed(port, reason(failure)))
                } finally {
                    connection.close()
                }
            } finally {
                runCatching { socket.close() }
            }
        }
        Reached.Elsewhere(AdbAnswer.Refused(lastRefusal ?: "Nothing at $address answered as ADB."))
    }

    /**
     * The ports something is listening on, cheapest check first.
     *
     * A plain connect, closed at once, on every port at a time. The greeting is deliberately not
     * made here: thirty-one concurrent greetings would be thirty-one authorisation prompts.
     */
    private suspend fun openPorts(
        binder: InterfaceSocketBinder,
        host: InetAddress,
        address: String,
        knownPort: Int,
    ): Reached<List<Int>> {
        val gate = Semaphore(SCAN_CONCURRENCY)
        val looks = coroutineScope {
            portsFor(knownPort).map { port ->
                async(Dispatchers.IO) { gate.withPermit { port to look(binder, host, port) } }
            }.awaitAll()
        }
        val open = looks.filter { it.second == Look.OPEN }.map { it.first }
        if (open.isNotEmpty()) return Reached.Answered(open)
        val refused = looks.count { it.second == Look.REFUSED }
        val silent = looks.count { it.second == Look.SILENT }
        return Reached.Elsewhere(closedVerdict(address, refused, silent, connectTimeoutMillis))
    }

    private enum class Look { OPEN, REFUSED, SILENT }

    private fun look(binder: InterfaceSocketBinder, host: InetAddress, port: Int): Look = try {
        connectTo(binder, host, port).close()
        Look.OPEN
    } catch (_: ConnectException) {
        Look.REFUSED
    } catch (_: SocketTimeoutException) {
        Look.SILENT
    } catch (_: IOException) {
        Look.SILENT
    }

    private fun connectTo(binder: InterfaceSocketBinder, host: InetAddress, port: Int): Socket =
        binder.newOutgoingSocket().apply {
            try {
                tcpNoDelay = true
                soTimeout = readTimeoutMillis
                connect(InetSocketAddress(host, port), connectTimeoutMillis)
            } catch (failure: IOException) {
                runCatching { close() }
                throw failure
            }
        }

    private fun listedPackages(connection: AdbConnection): Set<String> =
        connection.shell("pm list packages $PACKAGE_PREFIX")
            .lineSequence()
            .map { it.trim() }
            .filter { it.startsWith(PACKAGE_LINE) }
            .map { it.removePrefix(PACKAGE_LINE).trim() }
            .filter { it.isNotEmpty() }
            .toSet()

    /** Every way of not reaching the television, as the install reducer hears it. */
    private fun AdbAnswer.toInstallEvent(): InstallEvent = when (this) {
        is AdbAnswer.Unauthorised -> InstallEvent.AuthorisationRequired
        is AdbAnswer.Refused -> InstallEvent.Unreachable(detail)
        is AdbAnswer.Silent -> InstallEvent.Unreachable(detail)
        is AdbAnswer.Failed -> InstallEvent.Unreachable(detail)
        // A greeting that succeeded is not a way of failing to reach the television. The type
        // allows it only because every answer shares one hierarchy.
        is AdbAnswer.Identified -> InstallEvent.Unreachable("The television answered, then the exchange was lost.")
    }

    private fun reason(failure: Throwable): String =
        failure.message.orEmpty().ifBlank { failure.javaClass.simpleName }.take(DETAIL_LIMIT)

    companion object {
        const val CONNECT_TIMEOUT_MILLIS: Int = 1_500
        const val READ_TIMEOUT_MILLIS: Int = 8_000
        const val INSTALL_TIMEOUT_MILLIS: Int = 180_000
        const val SCAN_CONCURRENCY: Int = 8
        const val PACKAGE_PREFIX: String = "com.rextechnologies.flint"
        private const val PACKAGE_LINE = "package:"
        private const val BUFFER_BYTES = 64 * 1024
        private const val DETAIL_LIMIT = 240
        private const val NO_REASON = "The television's package manager refused without saying why."

        /**
         * What every port being closed means, told apart by how it was closed.
         *
         * A refusal means something is there and shut the door, which is genuinely ambiguous
         * between Vega and Fire OS with debugging off. Silence on every port means nothing is
         * there at all, which is almost always a wrong or stale address.
         */
        internal fun closedVerdict(address: String, refused: Int, silent: Int, timeoutMillis: Int): AdbAnswer {
            val range = "ports ${AdbPortScan.FIRST_PORT} to ${AdbPortScan.LAST_PORT}"
            return if (refused > 0) {
                AdbAnswer.Refused(
                    "Something at $address refused every ADB port ($refused of ${refused + silent} on $range).",
                )
            } else {
                AdbAnswer.Silent("Nothing at $address answered on $range within ${timeoutMillis / 1_000.0} seconds.")
            }
        }
    }
}
