package com.rextechnologies.flint.receiver.browser

import java.io.File
import java.nio.charset.StandardCharsets
import java.security.MessageDigest
import kotlin.test.AfterTest
import kotlin.test.BeforeTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

class BrowserNetworkStoreTest {
    private val directory = File(
        System.getProperty("java.io.tmpdir"),
        "flint-browser-network-${System.nanoTime()}",
    )
    private val file = File(directory, "browser-network.json")
    private val libraryFile = File(directory, "browser-library.json")
    private val crypto = AuthenticatedTestCrypto()
    private lateinit var library: BrowserLibraryStore

    @BeforeTest
    fun setUp() {
        library = BrowserLibraryStore(libraryFile) { "kids" }
    }

    @AfterTest
    fun cleanUp() {
        directory.deleteRecursively()
    }

    @Test
    fun `put get and remove survive restart without writing the config plaintext`() {
        val first = store()
        val settings = autoConnectSettings()

        assertTrue(first.put(BrowserLibraryStore.DEFAULT_PROFILE_ID, settings))
        assertEquals(settings, first.get(BrowserLibraryStore.DEFAULT_PROFILE_ID))
        val raw = file.readText()
        assertTrue(raw.contains("\"version\":2"))
        assertTrue(raw.contains("\"secret\""))
        assertFalse(raw.contains("configText"))
        assertFalse(raw.contains("PrivateKey"))
        assertFalse(raw.contains(settings.configText))

        val restarted = store()
        assertEquals(settings, restarted.get(BrowserLibraryStore.DEFAULT_PROFILE_ID))
        assertTrue(restarted.remove(BrowserLibraryStore.DEFAULT_PROFILE_ID))
        assertNull(store().get(BrowserLibraryStore.DEFAULT_PROFILE_ID))
    }

    @Test
    fun `invalid enabled settings are refused without writing`() {
        val store = store()

        assertFalse(
            store.put(
                BrowserLibraryStore.DEFAULT_PROFILE_ID,
                ProfileNetworkSettings(
                    vpnEnabled = true,
                    provider = VpnProvider.WIREGUARD,
                    autoConnectOnBrowserStart = true,
                    configText = "[Interface]\n",
                ),
            ),
        )
        assertNull(store.get(BrowserLibraryStore.DEFAULT_PROFILE_ID))
        assertFalse(file.exists())
    }

    @Test
    fun `disabled update destroys any stored credential rather than retaining a dormant secret`() {
        val store = store()
        assertTrue(store.put(BrowserLibraryStore.DEFAULT_PROFILE_ID, autoConnectSettings()))

        assertTrue(store.put(BrowserLibraryStore.DEFAULT_PROFILE_ID, ProfileNetworkSettings(vpnEnabled = false)))
        assertNull(store.get(BrowserLibraryStore.DEFAULT_PROFILE_ID))
        assertFalse(file.readText().contains("PrivateKey"))
        assertFalse(file.readText().contains("\"secret\""))
    }

    @Test
    fun `malformed file is replaced by an empty encrypted schema`() {
        directory.mkdirs()
        file.writeText("{not-json")

        assertNull(store().get(BrowserLibraryStore.DEFAULT_PROFILE_ID))
        assertTrue(file.readText().contains("\"version\":2"))
        assertFalse(file.readText().contains("not-json"))
    }

    @Test
    fun `legacy plaintext file is never migrated or auto activated`() {
        directory.mkdirs()
        file.writeText(
            """{"version":1,"profiles":{"default":{"vpnEnabled":true,"provider":"WIREGUARD","autoConnectOnBrowserStart":true,"configText":"$ESCAPED_VALID_CONFIG"}}}""",
        )

        val reopened = store()

        assertNull(reopened.get(BrowserLibraryStore.DEFAULT_PROFILE_ID))
        assertTrue(reopened.snapshot().isEmpty())
        assertFalse(reopened.get(BrowserLibraryStore.DEFAULT_PROFILE_ID)?.isConfiguredForAutoConnect ?: false)
        assertFalse(file.readText().contains("PrivateKey"))
        assertFalse(file.readText().contains("configText"))
    }

    @Test
    fun `nonexistent and device IDs cannot own persistent network settings`() {
        val store = store()
        val settings = autoConnectSettings()

        assertFalse(store.put("connected-device", settings))
        assertFalse(store.put("missing-tv-profile", settings))
        assertNull(store.get("connected-device"))
        assertFalse(file.exists())

        val kids = assertNotNull(library.createProfile("Kids"))
        assertTrue(store.put(kids.id, settings))
        assertEquals(settings, store.get(kids.id))
    }

    @Test
    fun `ciphertext is bound to its profile ID and cannot be swapped`() {
        val store = store()
        val kids = assertNotNull(library.createProfile("Kids"))
        assertTrue(store.put(BrowserLibraryStore.DEFAULT_PROFILE_ID, autoConnectSettings()))
        val swapped = file.readText().replace(
            "\"${BrowserLibraryStore.DEFAULT_PROFILE_ID}\"",
            "\"${kids.id}\"",
        )
        file.writeText(swapped)

        val reopened = store()

        assertNull(reopened.get(kids.id))
        assertTrue(reopened.snapshot().isEmpty())
    }

    @Test
    fun `deleted TV profile has an explicit credential cascade path`() {
        val store = store()
        val kids = assertNotNull(library.createProfile("Kids"))
        assertTrue(store.put(kids.id, autoConnectSettings()))
        assertFalse(store.removeForDeletedTvProfile(kids.id))

        assertTrue(library.deleteProfile(kids.id))
        assertTrue(store.removeForDeletedTvProfile(kids.id))
        assertNull(store.get(kids.id))
        assertFalse(store.snapshot().containsKey(kids.id))
        assertFalse(store().snapshot().containsKey(kids.id))
    }

    @Test
    fun `crypto failure rejects persistence rather than using plaintext fallback`() {
        val unavailable = BrowserNetworkStore(
            file = file,
            crypto = object : BrowserNetworkCrypto {
                override fun encrypt(
                    plaintext: ByteArray,
                    associatedData: ByteArray,
                ): BrowserNetworkCiphertext = throw BrowserNetworkCryptoException("Keystore unavailable")

                override fun decrypt(
                    ciphertext: BrowserNetworkCiphertext,
                    associatedData: ByteArray,
                ): ByteArray = throw BrowserNetworkCryptoException("Keystore unavailable")
            },
            profileAuthority = BrowserLibraryNetworkProfileAuthority(library),
        )

        assertFalse(unavailable.put(BrowserLibraryStore.DEFAULT_PROFILE_ID, autoConnectSettings()))
        assertFalse(file.exists())
    }

    private fun store(): BrowserNetworkStore = BrowserNetworkStore(
        file = file,
        crypto = crypto,
        profileAuthority = BrowserLibraryNetworkProfileAuthority(library),
    )

    private fun autoConnectSettings(): ProfileNetworkSettings = ProfileNetworkSettings(
        vpnEnabled = true,
        provider = VpnProvider.WIREGUARD,
        autoConnectOnBrowserStart = true,
        configText = VALID_CONFIG,
    )

    /** A deterministic authenticated fake; production coverage belongs to Android Keystore. */
    private class AuthenticatedTestCrypto : BrowserNetworkCrypto {
        override fun encrypt(plaintext: ByteArray, associatedData: ByteArray): BrowserNetworkCiphertext {
            val tag = tag(associatedData)
            val encrypted = ByteArray(tag.size + plaintext.size)
            tag.copyInto(encrypted)
            plaintext.forEachIndexed { index, byte -> encrypted[tag.size + index] = (byte.toInt() xor KEY).toByte() }
            return BrowserNetworkCiphertext(IV, encrypted)
        }

        override fun decrypt(ciphertext: BrowserNetworkCiphertext, associatedData: ByteArray): ByteArray {
            if (!ciphertext.initializationVector.contentEquals(IV) ||
                ciphertext.encryptedBytes.size < TAG_BYTES
            ) {
                throw BrowserNetworkCryptoException("Invalid encrypted test payload")
            }
            val encrypted = ciphertext.encryptedBytes
            if (!encrypted.copyOfRange(0, TAG_BYTES).contentEquals(tag(associatedData))) {
                throw BrowserNetworkCryptoException("Associated data mismatch")
            }
            return encrypted.copyOfRange(TAG_BYTES, encrypted.size)
                .map { (it.toInt() xor KEY).toByte() }
                .toByteArray()
        }

        private fun tag(associatedData: ByteArray): ByteArray = MessageDigest.getInstance("SHA-256")
            .digest(associatedData)
            .copyOf(TAG_BYTES)

        private companion object {
            const val KEY = 0x5a
            const val TAG_BYTES = 16
            val IV = ByteArray(12) { (it + 1).toByte() }
        }
    }

    companion object {
        private val VALID_CONFIG = """
            [Interface]
            PrivateKey = YAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
            Address = 10.0.0.2/32

            [Peer]
            PublicKey = XAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
            Endpoint = 203.0.113.1:51820
            AllowedIPs = 0.0.0.0/0
        """.trimIndent()

        private val ESCAPED_VALID_CONFIG = VALID_CONFIG
            .replace("\\", "\\\\")
            .replace("\n", "\\n")
            .replace("\"", "\\\"")
    }
}
