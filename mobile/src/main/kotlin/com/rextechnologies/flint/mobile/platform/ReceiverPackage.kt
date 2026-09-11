package com.rextechnologies.flint.mobile.platform

import android.content.Context
import android.content.pm.PackageInfo
import android.os.Build
import com.rextechnologies.flint.castcore.setup.BundledReceiver
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

    private const val CACHED_NAME = "flint-receiver.apk"

    /** Reads the bundled package, or `null` when this build has none. */
    fun bundled(context: Context): BundledReceiver? {
        val staged = stage(context) ?: return null
        val info = archiveInfo(context, staged) ?: return null
        val packageName = info.packageName ?: return null
        val versionName = info.versionName ?: return null
        return runCatching {
            BundledReceiver(
                packageName = packageName,
                versionName = versionName,
                versionCode = longVersionCode(info),
                sizeBytes = staged.length(),
            )
        }.getOrNull()
    }

    /** The file to hand to the installer, copied out of the assets on first use. */
    fun stage(context: Context): File? {
        val target = File(context.cacheDir, CACHED_NAME)
        if (target.isFile && target.length() > 0) return target
        return runCatching {
            context.assets.open(ASSET_NAME).use { input ->
                target.outputStream().use { output -> input.copyTo(output) }
            }
            target.takeIf { it.length() > 0 }
        }.getOrNull()
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
}
