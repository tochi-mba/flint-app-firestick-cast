package com.rextechnologies.flint.mobile.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import com.rextechnologies.flint.design.FlintSpace
import com.rextechnologies.flint.design.OutlineAction

/** Secondary information stays reachable without overwhelming the current task. */
@Composable
internal fun DetailSection(title: String, content: @Composable () -> Unit) {
    var expanded by rememberSaveable { mutableStateOf(false) }
    Column(
        modifier = Modifier.semantics { stateDescription = if (expanded) "Expanded" else "Collapsed" },
        verticalArrangement = Arrangement.spacedBy(FlintSpace.Small),
    ) {
        OutlineAction(text = if (expanded) "Hide $title" else title, onClick = { expanded = !expanded })
        if (expanded) content()
    }
}
