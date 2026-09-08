package com.rextechnologies.flint.receiver.browser

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch

/** Executes profile-picker mutations off the UI thread and publishes one coherent result. */
class BrowserProfileActions(
    private val scope: CoroutineScope,
    private val profiles: BrowserProfileSession,
    private val currentPage: () -> BrowserState,
    private val resetPage: () -> Unit,
    private val publishProfile: () -> Unit,
    private val publishCockpit: () -> Unit,
    private val showNotice: (String) -> Unit,
    private val persistSession: () -> Unit = {},
) {
    fun selectTv(profileId: String) {
        persistSession()
        onStore {
            if (!profiles.selectTvProfile(profileId)) {
                showNotice("That TV profile is no longer available.")
                return@onStore
            }
            changed(reset = true)
        }
    }

    fun selectDevice() {
        persistSession()
        val epoch = currentPage().epoch
        if (epoch == null || !profiles.requestConnectedDevice(epoch)) {
            showNotice("Connect and verify a device before using its profile.")
            return
        }
        // Publish the selected owner only after its library projection is adopted.
    }

    fun createTv(name: String) {
        persistSession()
        onStore {
            if (profiles.createTvProfile(name) == null) {
                showNotice("Use a unique profile name. Up to eight profiles can live on this TV.")
                return@onStore
            }
            changed(reset = true)
        }
    }

    fun renameTv(profileId: String, name: String) = onStore {
        if (!profiles.renameTvProfile(profileId, name)) {
            showNotice("That profile name is unavailable.")
        }
        changed(reset = false)
    }

    fun deleteTv(profileId: String) {
        persistSession()
        onStore {
            val wasActive = profiles.snapshot().profiles.activeTvProfileId == profileId
            if (!profiles.deleteTvProfile(profileId)) {
                showNotice("Keep at least one profile on this TV.")
                return@onStore
            }
            changed(reset = wasActive)
        }
    }

    private fun changed(reset: Boolean) {
        if (reset) resetPage()
        publishProfile()
        publishCockpit()
    }

    private fun onStore(block: () -> Unit) {
        scope.launch(Dispatchers.IO) { block() }
    }
}
