package com.rextechnologies.flint.protocol.wire

/** Additive browser workspace vectors mirrored from the Rust-authoritative golden corpus. */
internal object GoldenBrowserPhase3Vectors {
    val all: Map<String, WireFrame> = linkedMapOf(
        "browser-workspace-resize" to WireFrame(4, BrowserWorkspaceResizeMessage(4, 150, 140, 7000, 3000)),
        "browser-workspace-geometry" to WireFrame(4, BrowserWorkspaceGeometryMessage(4, 141, 7000, 3000)),
        "browser-workspace-command-focus" to WireFrame(
            3,
            BrowserWorkspaceCommandMessage(4, 130, 140, BrowserWorkspaceCommandAction.FOCUS, paneId = 42),
        ),
        "browser-workspace-command-open-pane" to WireFrame(
            3,
            BrowserWorkspaceCommandMessage(
                4,
                128,
                139,
                BrowserWorkspaceCommandAction.OPEN_PANE,
                url = "https://example.test/new",
            ),
        ),
        "browser-workspace-command-move-pane" to WireFrame(
            3,
            BrowserWorkspaceCommandMessage(
                4,
                143,
                140,
                BrowserWorkspaceCommandAction.MOVE_PANE,
                paneId = 42,
                value = 1,
            ),
        ),
        "browser-workspace-state-single" to WireFrame(
            3,
            BrowserWorkspaceStateMessage(
                4,
                140,
                BrowserWorkspaceWireLayout.SINGLE,
                42,
                BrowserWorkspaceWireInteractionMode.WORKSPACE_CHROME,
                0,
                0,
                2,
                4,
                listOf(
                    BrowserWorkspacePaneStateEntry(
                        42,
                        0,
                        BrowserWorkspaceWirePaneResidency.LIVE,
                        "https://example.test/new",
                        "",
                        false,
                        100,
                        false,
                        false,
                        false,
                        BrowserWorkspaceWireMuteApplication.NOT_REQUESTED,
                        BrowserWorkspaceWireObservedPlayback.UNKNOWN,
                    ),
                ),
            ),
        ),
        "browser-workspace-input-text" to WireFrame(
            3,
            BrowserWorkspaceInputMessage(
                4,
                131,
                140,
                42,
                BrowserWorkspaceInputKind.TEXT,
                text = "hello",
            ),
        ),
    )
}
