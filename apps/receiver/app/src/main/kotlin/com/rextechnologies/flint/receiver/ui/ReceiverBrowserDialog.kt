package com.rextechnologies.flint.receiver.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.tv.material3.Text
import com.rextechnologies.flint.receiver.BrowserDialogUi

/**
 * Native TV dialog for page alert/confirm/prompt and clear-data confirmation.
 *
 * D-pad operable via the activity's key path; Cancel is visually the default so a Back press is
 * safe. Lives in the 5% overscan-safe region.
 */
@Composable
internal fun ReceiverBrowserDialog(
    dialog: BrowserDialogUi,
    onConfirm: () -> Unit,
    onCancel: () -> Unit,
) {
    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(ReceiverColors.Ink.copy(alpha = 0.72f))
            .testTag(ReceiverTags.BROWSER_DIALOG)
            .padding(horizontal = 48.dp, vertical = 36.dp),
        contentAlignment = Alignment.Center,
    ) {
        Column(
            modifier = Modifier
                .widthIn(max = 720.dp)
                .fillMaxWidth()
                .background(ReceiverColors.Raised, RoundedCornerShape(16.dp))
                .border(1.dp, ReceiverColors.Line, RoundedCornerShape(16.dp))
                .padding(28.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            Text(
                text = dialog.origin.ifBlank { "This page" },
                color = ReceiverColors.Muted,
                fontSize = 13.sp,
                fontWeight = FontWeight.Bold,
            )
            Text(
                text = dialog.message,
                color = ReceiverColors.Text,
                fontSize = 20.sp,
                fontWeight = FontWeight.SemiBold,
            )
            if (!dialog.defaultValue.isNullOrBlank()) {
                Text(
                    text = dialog.defaultValue,
                    color = ReceiverColors.Muted,
                    fontSize = 15.sp,
                )
            }
            Spacer(modifier = Modifier.height(8.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                DialogAction(label = "Cancel", emphasised = true, onClick = onCancel)
                if (dialog.kind != "ALERT") {
                    DialogAction(label = "OK", emphasised = false, onClick = onConfirm)
                } else {
                    DialogAction(label = "OK", emphasised = false, onClick = onConfirm)
                }
            }
        }
    }
}

@Composable
private fun DialogAction(label: String, emphasised: Boolean, onClick: () -> Unit) {
    // TV Material buttons need focus; for snapshot/tests this is a labelled target the Activity
    // key path can also drive through the service dialog reply API.
    androidx.tv.material3.Button(
        onClick = onClick,
        modifier = Modifier.testTag("browser-dialog-${label.lowercase()}"),
    ) {
        Text(
            text = label.uppercase(),
            fontWeight = if (emphasised) FontWeight.Bold else FontWeight.Medium,
            fontSize = 14.sp,
        )
    }
}
