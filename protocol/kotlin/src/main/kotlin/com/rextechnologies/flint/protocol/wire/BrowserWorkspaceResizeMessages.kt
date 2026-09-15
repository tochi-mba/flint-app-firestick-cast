package com.rextechnologies.flint.protocol.wire

data class BrowserWorkspaceResizeMessage(
    val epoch: Long,
    val commandId: Long,
    val expectedRevision: Long,
    val column: Int,
    val row: Int,
    val mode: Int = 0,
) : WireMessage {
    override val typeId = WireMessageType.BROWSER_WORKSPACE_RESIZE.id
}

data class BrowserWorkspaceGeometryMessage(
    val epoch: Long,
    val revision: Long,
    val column: Int,
    val row: Int,
    val mode: Int = 0,
) : WireMessage {
    override val typeId = WireMessageType.BROWSER_WORKSPACE_GEOMETRY.id
}
