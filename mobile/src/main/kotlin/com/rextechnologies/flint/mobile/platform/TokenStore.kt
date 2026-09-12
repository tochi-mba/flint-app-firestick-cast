package com.rextechnologies.flint.mobile.platform

import android.content.Context
import android.content.SharedPreferences
import android.util.Base64
import com.rextechnologies.flint.protocol.http.SessionToken

/**
 * Where a session token lives between runs.
 *
 * A token is a standing proof that this phone is allowed to cast to that television, and it outlives
 * the session it was granted in, so it is sealed at rest by [SecretBox] rather than kept as text.
 * Sealing it protects the stored copy and nothing else: the cast link it authorises carries video in
 * the clear, and no string in this app describes that link as private, secure or encrypted.
 */
class TokenStore(context: Context, private val box: SecretBox = SecretBox()) {
    private val preferences: SharedPreferences =
        context.applicationContext.getSharedPreferences(PREFERENCES_NAME, Context.MODE_PRIVATE)

    /** The token granted by this receiver, or `null` when there is none or it no longer decrypts. */
    fun tokenFor(receiverAddress: String): SessionToken? {
        val stored = preferences.getString(keyFor(receiverAddress), null) ?: return null
        val packed = runCatching { Base64.decode(stored, Base64.NO_WRAP) }.getOrNull() ?: return null
        val plain = box.open(packed) ?: return null
        return SessionToken.parseOrNull(String(plain, Charsets.US_ASCII))
    }

    fun remember(receiverAddress: String, token: SessionToken) {
        val sealed = box.seal(token.toString().toByteArray(Charsets.US_ASCII)) ?: return
        preferences.edit().putString(keyFor(receiverAddress), Base64.encodeToString(sealed, Base64.NO_WRAP)).apply()
    }

    fun forget(receiverAddress: String) {
        preferences.edit().remove(keyFor(receiverAddress)).apply()
    }

    /** Used by the setting that unpairs everything, which exists so a lent phone can be handed back. */
    fun forgetEverything() {
        preferences.edit().clear().apply()
    }

    private fun keyFor(receiverAddress: String): String = "token:$receiverAddress"

    private companion object {
        const val PREFERENCES_NAME = "flint-mobile-tokens"
    }
}
