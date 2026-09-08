package com.rextechnologies.flint.protocol.adb

import com.rextechnologies.flint.protocol.BinaryData
import java.io.ByteArrayOutputStream
import java.io.Closeable
import java.io.InputStream
import java.io.OutputStream
import java.nio.charset.StandardCharsets

class AdbAuthorizationRequiredException(
    message: String,
    cause: Throwable? = null,
) : AdbFormatException(message, cause)

/** What the remote device reported when it accepted the connection. */
data class AdbBanner(val raw: String) {
    val model: String? get() = property("ro.product.model")
    val device: String? get() = property("ro.product.device")
    val name: String? get() = property("ro.product.name")

    /** A human label for the TV, falling back through the banner fields it actually sent. */
    val displayName: String
        get() = model ?: name ?: device ?: "Android device"

    private fun property(key: String): String? = raw
        .split("::", limit = 2)
        .getOrNull(1)
        ?.split(';')
        ?.firstOrNull { it.startsWith("$key=") }
        ?.substringAfter('=')
        ?.takeIf { it.isNotEmpty() }
}

/**
 * A minimal ADB client over an already-connected byte stream.
 *
 * Only the subset needed to install, launch, and remove the receiver is
 * implemented. The transport is supplied rather than opened here so tests can
 * drive a fake device over piped streams, and so the caller keeps
 * responsibility for binding the socket to the tether interface.
 */
class AdbConnection(
    private val input: InputStream,
    private val output: OutputStream,
    private val identity: AdbIdentity,
) : Closeable {
    private var nextLocalId: Long = 1
    private var maximumPayload: Int = AdbMessage.DEFAULT_MAX_DATA.toInt()

    var banner: AdbBanner? = null
        private set

    /**
     * Performs CNXN and, when challenged, the AUTH exchange.
     *
     * The first challenge is answered with a signature and only a second
     * challenge produces the public key. Offering the key first would raise the
     * trust prompt on the TV on every connection, even after the user already
     * approved this phone.
     */
    fun connect(): AdbBanner {
        send(AdbMessage.connect())
        var signatureRejected = false
        while (true) {
            val message = receive()
            when (message.command) {
                AdbCommands.CNXN -> return acceptBanner(message)

                AdbCommands.AUTH -> {
                    if (message.argument0 != AdbAuthType.TOKEN) {
                        throw AdbFormatException("Unexpected ADB AUTH type: ${message.argument0}")
                    }
                    if (signatureRejected) {
                        send(AdbAuth.publicKeyMessage(identity))
                        return awaitAuthorization()
                    }
                    val token = message.payload.toByteArray()
                    if (token.size != AdbAuth.TOKEN_BYTES) {
                        throw AdbFormatException("ADB AUTH token must contain 20 bytes")
                    }
                    send(AdbAuth.signatureMessage(identity, token))
                    signatureRejected = true
                }

                AdbCommands.CLSE -> throw AdbFormatException("Device closed the ADB connection")
                else -> throw AdbFormatException("Unexpected ADB command: ${message.command}")
            }
        }
    }

    private fun awaitAuthorization(): AdbBanner {
        while (true) {
            val message = try {
                receive()
            } catch (exception: AdbFormatException) {
                throw AdbAuthorizationRequiredException(AUTHORIZE_ON_TV, exception)
            }
            when (message.command) {
                AdbCommands.CNXN -> return acceptBanner(message)
                // The device repeats AUTH while its prompt is still on screen.
                AdbCommands.AUTH -> continue
                else -> throw AdbAuthorizationRequiredException(AUTHORIZE_ON_TV)
            }
        }
    }

    private fun acceptBanner(message: AdbMessage): AdbBanner {
        maximumPayload = message.argument1
            .coerceIn(MINIMUM_PAYLOAD.toLong(), AdbMessage.DEFAULT_MAX_DATA)
            .toInt()
        return AdbBanner(message.payload.asBannerText()).also { banner = it }
    }

    /**
     * Opens one ADB stream, optionally writes [payload], and returns the reply.
     *
     * Writing before reading matches how `exec:` services work: the device
     * consumes the whole payload, then answers on the same stream.
     */
    fun request(destination: String, payload: ByteArray = ByteArray(0)): ByteArray {
        require(destination.isNotEmpty()) { "ADB destination must not be empty" }
        val localId = nextLocalId++
        send(AdbMessage.open(localId, destination))

        var remoteId = 0L
        var offset = 0
        val collected = ByteArrayOutputStream()

        while (true) {
            val message = receive()
            if (message.argument1 != localId && message.argument1 != 0L) continue
            when (message.command) {
                AdbCommands.OKAY -> {
                    remoteId = message.argument0
                    if (offset < payload.size) {
                        val length = minOf(maximumPayload, payload.size - offset)
                        send(AdbMessage.write(localId, remoteId, payload.copyOfRange(offset, offset + length)))
                        offset += length
                    }
                }

                AdbCommands.WRTE -> {
                    if (remoteId == 0L) remoteId = message.argument0
                    message.payload.withBytes(collected::write)
                    send(AdbMessage.okay(localId, message.argument0))
                }

                AdbCommands.CLSE -> {
                    if (remoteId != 0L) runCatching { send(AdbMessage.close(localId, remoteId)) }
                    return collected.toByteArray()
                }

                else -> throw AdbFormatException("Unexpected ADB command: ${message.command}")
            }
        }
    }

    /**
     * Runs a shell command and returns its combined output.
     *
     * The device runs this string through its shell, so the command is checked
     * against an allowlist rather than a list of forbidden characters. Spaces
     * have to be permitted for `pm` and `am` arguments, which is exactly why
     * every metacharacter that could chain a second command must not be.
     */
    fun shell(command: String): String {
        require(command.isNotBlank() && SAFE_SHELL_COMMAND.matches(command)) {
            "Shell command contains characters that are not allowed"
        }
        return request("shell:$command").toString(StandardCharsets.UTF_8)
    }

    /**
     * Streams an APK straight into the package manager.
     *
     * `exec:cmd package install` avoids the sync protocol and never leaves a
     * copy of the APK in the TV temporary storage, so a failed install cannot
     * strand a file the user then has to find and delete.
     */
    fun installApk(apk: ByteArray, reinstall: Boolean = true): AdbInstallResult {
        require(apk.isNotEmpty()) { "APK payload must not be empty" }
        val flags = if (reinstall) "-r " else ""
        val output = request("exec:cmd package install ${flags}-S ${apk.size}", apk)
            .toString(StandardCharsets.UTF_8)
            .trim()
        return AdbInstallResult(succeeded = output.startsWith("Success"), output = output)
    }

    fun uninstallPackage(packageName: String): AdbInstallResult {
        require(PACKAGE_NAME.matches(packageName)) { "Invalid package name: $packageName" }
        val output = shell("pm uninstall $packageName").trim()
        return AdbInstallResult(succeeded = output.startsWith("Success"), output = output)
    }

    fun launchActivity(component: String): String {
        require(COMPONENT_NAME.matches(component)) { "Invalid component name: $component" }
        return shell("am start -n $component")
    }

    /**
     * Lists the TV launcher entries installed on the device.
     *
     * This is what makes "play it on the TV instead" possible. Protected video
     * cannot be mirrored, so the honest alternative is to start the streaming
     * app on the TV, where it plays at full quality with its own DRM path.
     */
    fun listLeanbackActivities(): List<TvActivity> {
        val output = shell(
            "cmd package query-activities --brief -a android.intent.action.MAIN " +
                "-c android.intent.category.LEANBACK_LAUNCHER",
        )
        return parseComponents(output)
    }

    /** Falls back to the phone-style launcher category for non-TV Android devices. */
    fun listLauncherActivities(): List<TvActivity> {
        val output = shell(
            "cmd package query-activities --brief -a android.intent.action.MAIN " +
                "-c android.intent.category.LAUNCHER",
        )
        return parseComponents(output)
    }

    private fun parseComponents(output: String): List<TvActivity> = output
        .lineSequence()
        .map { it.trim() }
        .filter { COMPONENT_NAME.matches(it) }
        .map { TvActivity(component = it, packageName = it.substringBefore('/')) }
        .distinctBy { it.component }
        .sortedBy { it.packageName }
        .toList()

    override fun close() {
        runCatching { output.flush() }
        runCatching { input.close() }
        runCatching { output.close() }
    }

    private fun send(message: AdbMessage) {
        AdbMessageCodec.writeTo(output, message)
        output.flush()
    }

    private fun receive(): AdbMessage =
        AdbMessageCodec.readFrom(input) ?: throw AdbFormatException("ADB connection ended")

    private fun BinaryData.asBannerText(): String =
        withBytes { String(it, StandardCharsets.UTF_8) }.trimEnd('\u0000', ' ')

    private companion object {
        const val MINIMUM_PAYLOAD = 4_096
        const val AUTHORIZE_ON_TV = "Accept the USB debugging prompt on the TV, then try again"
        val PACKAGE_NAME = Regex("[A-Za-z][A-Za-z0-9_]*(\\.[A-Za-z0-9_]+)+")
        val COMPONENT_NAME = Regex("[A-Za-z][A-Za-z0-9_.]*/\\.?[A-Za-z][A-Za-z0-9_.]*")

        // Letters, digits, and the punctuation that package/component/flag arguments
        // need. Notably absent: ; | & $ ` ( ) < > newline quotes backslash * ? ~ #
        val SAFE_SHELL_COMMAND = Regex("[A-Za-z0-9 ._:/=,+@-]{1,512}")
    }
}

data class AdbInstallResult(val succeeded: Boolean, val output: String)

/** A launchable entry point on the connected TV. */
data class TvActivity(val component: String, val packageName: String) {
    /**
     * A readable name derived from the package id.
     *
     * The package manager brief listing carries no labels, and fetching each
     * one costs a separate round trip, so the last identifier segment is used.
     */
    val displayName: String
        get() = packageName.substringAfterLast('.')
            .replace('_', ' ')
            .replaceFirstChar { it.uppercase() }
}

