package com.rextechnologies.flint.receiver

import android.app.Activity
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.ServiceConnection
import android.net.VpnService
import android.os.Build
import android.os.Bundle
import android.os.IBinder
import android.view.KeyEvent
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.annotation.OptIn
import androidx.compose.runtime.mutableStateOf
import androidx.media3.common.util.UnstableApi
import com.rextechnologies.flint.receiver.browser.VpnConsentHost
import com.rextechnologies.flint.receiver.ui.ReceiverScreen
import com.rextechnologies.flint.receiver.ui.ReceiverTheme

/** Full-screen, remote-first surface for the Flint receiver service. */
@OptIn(UnstableApi::class)
class ReceiverActivity : ComponentActivity() {
    private val serviceState = mutableStateOf<ReceiverService?>(null)
    private var bound = false
    private var pendingVpnConsent: ((Boolean) -> Unit)? = null

    private val vpnConsentLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult(),
    ) { result ->
        val callback = pendingVpnConsent
        pendingVpnConsent = null
        callback?.invoke(result.resultCode == Activity.RESULT_OK)
    }

    private val vpnConsentHost = VpnConsentHost { onResult ->
        val prepare = runCatching { VpnService.prepare(this) }.getOrNull()
        if (prepare == null) {
            onResult(true)
            return@VpnConsentHost
        }
        pendingVpnConsent = onResult
        vpnConsentLauncher.launch(prepare)
    }

    private val connection = object : ServiceConnection {
        override fun onServiceConnected(name: ComponentName?, binder: IBinder?) {
            val service = (binder as? ReceiverService.LocalBinder)?.service
            service?.attachVpnConsentHost(vpnConsentHost)
            serviceState.value = service
        }

        override fun onServiceDisconnected(name: ComponentName?) {
            serviceState.value?.attachVpnConsentHost(null)
            serviceState.value = null
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)

        val serviceIntent = Intent(this, ReceiverService::class.java)
        if (Build.VERSION.SDK_INT >= 26) {
            startForegroundService(serviceIntent)
        } else {
            startService(serviceIntent)
        }

        enableEdgeToEdge()
        setContent {
            ReceiverTheme {
                ReceiverScreen(serviceState.value)
            }
        }
    }

    override fun onStart() {
        super.onStart()
        bound = bindService(Intent(this, ReceiverService::class.java), connection, Context.BIND_AUTO_CREATE)
    }

    override fun onStop() {
        serviceState.value?.attachVpnConsentHost(null)
        if (bound) unbindService(connection)
        bound = false
        serviceState.value = null
        super.onStop()
    }

    override fun onKeyDown(keyCode: Int, event: KeyEvent?): Boolean =
        serviceState.value?.dispatchTvKey(keyCode) == true || super.onKeyDown(keyCode, event)
}
