package com.rextechnologies.flint.mobile.platform

import android.content.Context
import android.content.pm.PackageInfo
import android.os.Build
import com.rextechnologies.flint.castcore.setup.BundledReceiver
import com.rextechnologies.flint.mobile.BuildConfig
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File

/**
 * The Fire TV package this build carries, if it carries one.
 *
 * The release workflow copies the receiver APK into the assets just before the phone app is
 * assembled, so the two are always from the same commit. A local or pull-request build has no such
 * asset, and that is an ordinary state rather than an error: pairing with a receiver somebody already
 * installed still works, and the setup screen says plainly that there is nothing to offer.
 *
 * What is shown above the words "this is what will be installed" is read from the APK itself rather
 * than from the sidecar the workflow writes. A properties file says what somebody intended to bundle;
 * the archive says what is actually there, and only one of those is worth showing to a person who is
 * about to let it onto their television.
 */
object ReceiverPackage {
    const val ASSET_NAME: String = "flint-receiver.apk"

    /**
     * Reads the bundled package, or `null` when this build has none.
     *
     * Suspending, and on [Dispatchers.IO], because the first call copies several megabytes out of
     * the assets. It used to be called from `onCreate` on the main thread, which is a visible pause
     * on the app's very first frame and a strict-mode violation on every phone that has it on.
     */
    suspend fun bundled(context: Context): BundledReceiver? = withContext(Dispatchers.IO) {
        val staged = stage(context) ?: return@withContext null
        val info = archiveInfo(context, staged) ?: return@withContext null
        // Typed as nullable on purpose. The SDK stubs declare both fields non-null and the compiler
        // believes them; a real package manager on a real phone hands back null for either, and a
        // field read carries no runtime check that would say so.
        val packageName: String? = info.packageName
        val versionName: String? = info.versionName
        if (packageName == null || versionName == null) return@withContext null
        runCatching {
            BundledReceiver(
                packageName = packageName,
                versionName = versionName,
                versionCode = longVersionCode(info),
                sizeBytes = staged.length(),
            )
        }.getOrNull()
    }

    /**
     * The file to hand to the installer, copied out of the assets on first use.
     *
     * Blocking, and deliberately not hidden behind a suspend wrapper: the callers that hand this to
     * a package installer are already on a background dispatcher, and one that is not should find
     * that out from a lint warning rather than from a dropped frame.
     *
     * The cache file's name carries this build's version code. Without it an update ships a new
     * receiver and the phone goes on offering the one it staged before the update, for the life of
     * the installation -- a stale APK is served forever, and it looks exactly like the bundled
     * receiver never changing. Files from earlier versions are removed as they are noticed rather
     * than accumulating.
     */
    fun stage(context: Context): File? {
        val target = File(context.cacheDir, cachedName())
        if (target.isFile && target.length() > 0) return target
        removeStaleStagedCopies(context, keep = target.name)
        return runCatching {
            context.assets.open(ASSET_NAME).use { input ->
                target.outputStream().use { output -> input.copyTo(output) }
            }
            target.takeIf { it.length() > 0 }
        }.getOrNull()
    }

    private fun cachedName(): String = "flint-receiver-${BuildConfig.VERSION_CODE}.apk"

    private fun removeStaleStagedCopies(context: Context, keep: String) {
        runCatching {
            context.cacheDir.listFiles()
                ?.filter { it.isFile && it.name.startsWith(STAGED_PREFIX) && it.name != keep }
                ?.forEach { it.delete() }
        }
    }

    private fun archiveInfo(context: Context, file: File): PackageInfo? = runCatching {
        context.packageManager.getPackageArchiveInfo(file.absolutePath, 0)
    }.getOrNull()

    @Suppress("DEPRECATION")
    private fun longVersionCode(info: PackageInfo): Long =
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
            info.longVersionCode
        } else {
            info.versionCode.toLong()
        }

    /** Whether the receiver package this build carries is already on the television. */
    fun isInstalledOn(installedPackages: Collection<String>, bundled: BundledReceiver?): Boolean =
        bundled != null && bundled.packageName in installedPackages

    private const val STAGED_PREFIX = "flint-receiver-"
}
