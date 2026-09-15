package com.rextechnologies.flint.protocol.http

import java.util.Locale

object MimeTypes {
    const val DEFAULT: String = "application/octet-stream"

    private val BY_EXTENSION = mapOf(
        "3gp" to "video/3gpp",
        "aac" to "audio/aac",
        "ass" to "text/x-ssa",
        "avi" to "video/x-msvideo",
        "avif" to "image/avif",
        "bmp" to "image/bmp",
        "flac" to "audio/flac",
        "gif" to "image/gif",
        "heic" to "image/heic",
        "heif" to "image/heif",
        "html" to "text/html; charset=utf-8",
        "jpeg" to "image/jpeg",
        "jpg" to "image/jpeg",
        "json" to "application/json",
        "m2ts" to "video/mp2t",
        "m3u8" to "application/vnd.apple.mpegurl",
        "m4a" to "audio/mp4",
        "m4v" to "video/mp4",
        "mka" to "audio/x-matroska",
        "mkv" to "video/x-matroska",
        "mov" to "video/quicktime",
        "mp3" to "audio/mpeg",
        "mp4" to "video/mp4",
        "mpd" to "application/dash+xml",
        "mpeg" to "video/mpeg",
        "mpg" to "video/mpeg",
        "oga" to "audio/ogg",
        "ogg" to "audio/ogg",
        "ogv" to "video/ogg",
        "opus" to "audio/opus",
        "png" to "image/png",
        "srt" to "application/x-subrip",
        "ssa" to "text/x-ssa",
        "svg" to "image/svg+xml",
        "ts" to "video/mp2t",
        "ttml" to "application/ttml+xml",
        "vtt" to "text/vtt",
        "wav" to "audio/wav",
        "webm" to "video/webm",
        "webp" to "image/webp",
        "xml" to "application/xml",
    )

    fun forFileName(fileNameOrPath: String): String {
        val withoutQuery = fileNameOrPath.substringBefore('?').substringBefore('#')
        val name = withoutQuery.substringAfterLast('/').substringAfterLast('\\')
        val dot = name.lastIndexOf('.')
        if (dot < 0 || dot == name.lastIndex) return DEFAULT
        val extension = name.substring(dot + 1).lowercase(Locale.ROOT)
        return BY_EXTENSION[extension] ?: DEFAULT
    }
}
