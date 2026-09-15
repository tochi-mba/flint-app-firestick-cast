package com.rextechnologies.flint.protocol.adb

import com.rextechnologies.flint.protocol.BinaryData

data class AdbCommand(val rawValue: Int) {
    val ascii: String
        get() = buildString(4) {
            repeat(4) { index -> append(((rawValue ushr (index * 8)) and 0xff).toChar()) }
        }

    override fun toString(): String = ascii

    companion object {
        fun fromAscii(value: String): AdbCommand {
            require(value.length == 4 && value.all { it.code in 0x20..0x7e }) {
                "ADB command must contain four printable ASCII characters"
            }
            var raw = 0
            value.forEachIndexed { index, character -> raw = raw or (character.code shl (index * 8)) }
            return AdbCommand(raw)
        }
    }
}

object AdbCommands {
    val CNXN: AdbCommand = AdbCommand.fromAscii("CNXN")
    val AUTH: AdbCommand = AdbCommand.fromAscii("AUTH")
    val OPEN: AdbCommand = AdbCommand.fromAscii("OPEN")
    val WRTE: AdbCommand = AdbCommand.fromAscii("WRTE")
    val OKAY: AdbCommand = AdbCommand.fromAscii("OKAY")
    val CLSE: AdbCommand = AdbCommand.fromAscii("CLSE")
}

object AdbAuthType {
    const val TOKEN: Long = 1
    const val SIGNATURE: Long = 2
    const val RSA_PUBLIC_KEY: Long = 3
}

data class AdbMessage(
    val command: AdbCommand,
    val argument0: Long = 0,
    val argument1: Long = 0,
    val payload: BinaryData = BinaryData.EMPTY,
) {
    init {
        require(argument0 in 0..0xffff_ffffL) { "ADB argument0 is not uint32" }
        require(argument1 in 0..0xffff_ffffL) { "ADB argument1 is not uint32" }
    }

    companion object {
        const val VERSION: Long = 0x01000001
        const val DEFAULT_MAX_DATA: Long = 1024 * 1024

        fun connect(
            systemIdentity: String = "host::features=shell_v2,cmd",
            version: Long = VERSION,
            maximumData: Long = DEFAULT_MAX_DATA,
        ): AdbMessage {
            require('\u0000' !in systemIdentity)
            val payload = BinaryData.of((systemIdentity + '\u0000').toByteArray(Charsets.UTF_8))
            return AdbMessage(AdbCommands.CNXN, version, maximumData, payload)
        }

        fun open(localId: Long, destination: String): AdbMessage {
            require(localId in 1..0xffff_ffffL)
            require(destination.isNotEmpty() && '\u0000' !in destination)
            return AdbMessage(
                AdbCommands.OPEN,
                localId,
                0,
                BinaryData.of((destination + '\u0000').toByteArray(Charsets.UTF_8)),
            )
        }

        fun write(localId: Long, remoteId: Long, data: ByteArray): AdbMessage =
            AdbMessage(AdbCommands.WRTE, localId, remoteId, BinaryData.of(data))

        fun okay(localId: Long, remoteId: Long): AdbMessage =
            AdbMessage(AdbCommands.OKAY, localId, remoteId)

        fun close(localId: Long, remoteId: Long): AdbMessage =
            AdbMessage(AdbCommands.CLSE, localId, remoteId)
    }
}
