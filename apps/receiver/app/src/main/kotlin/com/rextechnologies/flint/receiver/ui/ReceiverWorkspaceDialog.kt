package com.rextechnologies.flint.receiver.ui

import android.view.KeyEvent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.input.key.*
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import androidx.tv.material3.Button
import androidx.tv.material3.Text
import com.rextechnologies.flint.receiver.browser.*
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceDialogs

/** A separate modal window prevents page input while a native JavaScript result is pending. */
@Composable
internal fun ReceiverWorkspaceDialog(
    request: BrowserWorkspaceDialogs.Request,
    onAnswer: (BrowserDialogAnswer) -> Unit,
) {
    val cancelFocus = remember(request.id) { FocusRequester() }
    val keyboard = remember { BrowserKeyboard() }
    var input by remember(request.id) { mutableStateOf(BrowserKeyboardState(text = request.dialog.defaultValue.orEmpty())) }
    var editing by remember(request.id) { mutableStateOf(false) }
    val prompt = request.dialog.kind == BrowserDialogKind.PROMPT
    Dialog(
        onDismissRequest = { onAnswer(BrowserDialogAnswer.Cancel) },
        properties = DialogProperties(usePlatformDefaultWidth = false, dismissOnClickOutside = false),
    ) {
        LaunchedEffect(request.id) { cancelFocus.requestFocus() }
        Column(
            modifier = Modifier.fillMaxWidth(ReceiverOverscan.CONTENT_FRACTION)
                .background(ReceiverColors.Raised, ReceiverShapes.Medium)
                .padding(24.dp)
                .onPreviewKeyEvent { event ->
                    if (!editing) return@onPreviewKeyEvent false
                    val key = event.nativeKeyEvent.keyCode
                    if (event.type != KeyEventType.KeyDown) return@onPreviewKeyEvent true
                    val direction = when (key) {
                        KeyEvent.KEYCODE_DPAD_UP -> CursorDirection.UP
                        KeyEvent.KEYCODE_DPAD_DOWN -> CursorDirection.DOWN
                        KeyEvent.KEYCODE_DPAD_LEFT -> CursorDirection.LEFT
                        KeyEvent.KEYCODE_DPAD_RIGHT -> CursorDirection.RIGHT
                        else -> null
                    }
                    when {
                        direction != null -> input = input.copy(cursor = keyboard.move(input.cursor, direction, input.page))
                        key == KeyEvent.KEYCODE_BACK -> editing = false
                        (key == KeyEvent.KEYCODE_DPAD_CENTER || key == KeyEvent.KEYCODE_ENTER) &&
                            event.nativeKeyEvent.repeatCount == 0 -> {
                            val selected = keyboard.keyAt(input.cursor, input.page)
                            if (selected == BrowserKey.Submit) editing = false
                            else input = keyboard.press(input, selected)
                        }
                    }
                    true
                },
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            val origin = runCatching { java.net.URI(request.dialog.originUrl).host }.getOrNull()
            Text("Page ${request.paneId} · ${origin ?: "This page"}", color = ReceiverColors.Muted)
            Text(request.dialog.message, color = ReceiverColors.Text, maxLines = 6)
            if (prompt) {
                Text(input.text.ifEmpty { "Empty response" }, color = ReceiverColors.Text, maxLines = 3)
                if (editing) ReceiverBrowserKeyboard(input, keyboard, submitLabel = "done")
            }
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Button(onClick = { onAnswer(BrowserDialogAnswer.Cancel) }, modifier = Modifier.focusRequester(cancelFocus)) {
                    Text("Cancel")
                }
                if (prompt) Button(onClick = { editing = true }) { Text("Edit response") }
                Button(onClick = {
                    onAnswer(if (prompt) BrowserDialogAnswer.Prompt(input.text) else BrowserDialogAnswer.Confirm)
                }) { Text(if (request.dialog.kind == BrowserDialogKind.BEFORE_UNLOAD) "Leave page" else "OK") }
            }
        }
    }
}
