package com.rextechnologies.flint.mobile.platform

import android.content.Context
import android.content.SharedPreferences
import android.util.Base64
import com.rextechnologies.flint.castcore.setup.AdbIdentityCodec
import com.rextechnologies.flint.protocol.adb.AdbAuth
import com.rextechnologies.flint.protocol.adb.AdbIdentity

/**
 * The one RSA identity this phone presents to televisions over ADB.
 *
 * Generated once and kept, because the television remembers the phone by its public key: a fresh
 * pair on every run would put the authorisation prompt back on the television every time, and that
 * prompt is the owner's say in what runs on their device, not a hoop. The private half is sealed at
 * rest by the same keystore-held key the session tokens use.
 *
 * Generation is slow on purpose -- a 2048-bit pair takes a noticeable fraction of a second on a
 * phone -- and so [identity] is blocking and must be called off the main thread, which the ADB
 * client is anyway.
 */
class AdbIdentityStore(context: Context, private val box: SecretBox = SecretBox()) {
    private val preferences: SharedPreferences =
        context.applicationContext.getSharedPreferences(PREFERENCES_NAME, Context.MODE_PRIVATE)

    private val lock = Any()

    /** The stored identity, or a new one that is stored before it is returned. */
    fun identity(): AdbIdentity = synchronized(lock) {
        stored() ?: AdbAuth.generateIdentity(AdbIdentityCodec.DEFAULT_COMMENT).also { remember(it) }
    }

    /** Whether a key pair has been generated on this phone yet. */
    val exists: Boolean
        get() = preferences.contains(KEY)

    /** Drops the identity. The next connection will show the television's prompt again. */
    fun forget() = synchronized(lock) {
        preferences.edit().remove(KEY).apply()
    }

    private fun stored(): AdbIdentity? {
        val encoded = preferences.getString(KEY, null) ?: return null
        val sealed = runCatching { Base64.decode(encoded, Base64.NO_WRAP) }.getOrNull() ?: return null
        val plain = box.open(sealed) ?: return null
        return AdbIdentityCodec.decode(plain)
    }

    private fun remember(identity: AdbIdentity) {
        val sealed = box.seal(AdbIdentityCodec.encode(identity)) ?: return
        preferences.edit().putString(KEY, Base64.encodeToString(sealed, Base64.NO_WRAP)).apply()
    }

    private companion object {
        const val PREFERENCES_NAME = "flint-mobile-adb"
        const val KEY = "identity"
    }
}
