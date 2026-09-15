package com.rextechnologies.flint.receiver.browser

import com.rextechnologies.flint.protocol.wire.BrowserLibraryEntryKind
import com.rextechnologies.flint.protocol.wire.BrowserLibraryStateMessage
import com.rextechnologies.flint.protocol.wire.BrowserLibraryEntry as WireLibraryEntry

/** Converts one already-validated local/profile projection into its bounded full wire snapshot. */
fun BrowserLibraryState.toWireMessage(epoch: Long, revision: Long): BrowserLibraryStateMessage =
    BrowserLibraryStateMessage(
        epoch = epoch,
        revision = revision,
        bookmarks = bookmarks.map { entry -> entry.toWire(BrowserLibraryEntryKind.BOOKMARK) },
        history = history.map { entry -> entry.toWire(BrowserLibraryEntryKind.HISTORY) },
    )

private fun BrowserLibraryEntry.toWire(kind: BrowserLibraryEntryKind): WireLibraryEntry = WireLibraryEntry(
    kind = kind,
    faviconId = faviconId,
    lastVisitedMs = lastVisitedMs,
    url = url,
    title = title,
)
