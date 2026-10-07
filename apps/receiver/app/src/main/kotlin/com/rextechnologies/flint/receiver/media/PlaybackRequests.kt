package com.rextechnologies.flint.receiver.media

/** How long a picture stays up when its sender does not say: the player's own default. */
internal const val DEFAULT_PICTURE_MILLIS = 6_000L

/** Whether a file of [mimeType] is a still picture rather than something that plays. */
internal fun isPicture(mimeType: String): Boolean = mimeType.startsWith("image/")

/**
 * How long a picture stays up: as long as its sender asked, or six seconds when it did not.
 *
 * Six seconds is the player's own default, and what older senders get. After it the player ends
 * the item and this TV draws its Finished bar over the picture, so a sender that moves on by
 * itself, or waits for the viewer, asks for longer.
 */
internal fun pictureDurationMillis(requestedMs: Long): Long =
    if (requestedMs > 0) requestedMs else DEFAULT_PICTURE_MILLIS

/**
 * A volume control's level as the player's own volume, from silent to as loud as the TV is set.
 *
 * The player's volume rather than the TV's: Fire OS ignored an app setting the TV's media volume
 * on a Fire TV Stick 4K playing through Bluetooth, while the player's volume always applies. The
 * TV's own volume, set with its remote, still applies on top.
 */
internal fun playerVolume(level: Float): Float = if (level.isNaN()) 1f else level.coerceIn(0f, 1f)
