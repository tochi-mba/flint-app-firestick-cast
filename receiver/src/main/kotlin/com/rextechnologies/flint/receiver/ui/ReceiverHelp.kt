package com.rextechnologies.flint.receiver.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import androidx.tv.material3.Button
import androidx.tv.material3.Text
import com.rextechnologies.flint.receiver.browser.BrowserHelpProgress
import com.rextechnologies.flint.receiver.browser.BrowserHelpTopics

/** Explicit help entry. No timer, automatic popup, or focus request outside its opened dialog. */
@Composable
internal fun ReceiverHelp(modifier: Modifier = Modifier) {
    val context = LocalContext.current.applicationContext
    val preferences = remember(context) { context.getSharedPreferences("flint-help", 0) }
    var progress by remember {
        mutableStateOf(BrowserHelpProgress(dismissed = runCatching {
            preferences.getBoolean("receiver-v1-dismissed", false)
        }.getOrDefault(false)))
    }
    fun complete() {
        progress = progress.complete()
        runCatching { preferences.edit().putBoolean("receiver-v1-dismissed", true).apply() }
    }
    Button(onClick = { progress = progress.show() }, modifier = modifier) {
        Text(if (progress.dismissed) "Help" else "New here? Help")
    }
    if (progress.open) {
        val closeFocus = remember { FocusRequester() }
        Dialog(onDismissRequest = { progress = progress.hide() },
            properties = DialogProperties(usePlatformDefaultWidth = false, dismissOnClickOutside = false)) {
            LaunchedEffect(Unit) { closeFocus.requestFocus() }
            Column(Modifier.fillMaxWidth(ReceiverOverscan.CONTENT_FRACTION)
                .background(ReceiverColors.Raised, ReceiverShapes.Medium).padding(24.dp),
                verticalArrangement = Arrangement.spacedBy(16.dp)) {
                val topic = BrowserHelpTopics.entries[progress.index]
                Text(topic.title, style = ReceiverType.BodyStrong, color = ReceiverColors.Text)
                Text(topic.body, style = ReceiverType.Body, color = ReceiverColors.Text)
                Text("${progress.index + 1} / ${BrowserHelpTopics.entries.size}", color = ReceiverColors.Muted)
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    Button(onClick = { progress = progress.hide() }, modifier = Modifier.focusRequester(closeFocus)) { Text("Close") }
                    Button(onClick = { progress = progress.back() }, enabled = progress.index > 0) { Text("Previous") }
                    Button(onClick = {
                        if (progress.index == BrowserHelpTopics.entries.lastIndex) complete()
                        else progress = progress.next()
                    }) { Text(if (progress.index == BrowserHelpTopics.entries.lastIndex) "Got it" else "Next") }
                    Button(onClick = { complete() }) { Text("Dismiss tips") }
                }
            }
        }
    }
}
