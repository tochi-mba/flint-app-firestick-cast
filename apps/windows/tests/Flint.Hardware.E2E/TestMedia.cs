using System.Diagnostics;

namespace Flint.Hardware.E2E;

/// <summary>
/// One small file of every kind Flint offers to play, made with ffmpeg the first time it is
/// needed and kept under artifacts/hardware-media.
/// </summary>
/// <remarks>
/// Videos are a minute of a moving test pattern with a tone, long enough to pause, seek and resume
/// in; music is twenty seconds of tone; pictures are one frame of the pattern. Nothing is
/// downloaded and nothing personal is sent to the TV.
/// </remarks>
internal static class TestMedia
{
    /// <summary>How long each test video is.</summary>
    internal const int VideoSeconds = 60;

    private static readonly string[] Video =
        ["-f", "lavfi", "-i", $"testsrc2=size=640x360:rate=30:duration={VideoSeconds}", "-f", "lavfi", "-i", $"sine=frequency=440:duration={VideoSeconds}"];

    private static readonly string[] Sound = ["-f", "lavfi", "-i", "sine=frequency=440:duration=20"];

    private static readonly string[] Picture = ["-f", "lavfi", "-i", "testsrc2=size=1280x720", "-frames:v", "1"];

    private static readonly string[] H264 = ["-c:v", "libx264", "-preset", "veryfast", "-crf", "30", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "96k"];

    private static readonly Dictionary<string, string[]> Recipes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = [.. Video, .. H264, "-movflags", "+faststart"],
        [".m4v"] = [.. Video, .. H264, "-movflags", "+faststart", "-f", "mp4"],
        [".mov"] = [.. Video, .. H264, "-movflags", "+faststart"],
        [".mkv"] = [.. Video, .. H264],
        [".ts"] = [.. Video, .. H264, "-f", "mpegts"],
        [".webm"] = [.. Video, "-c:v", "libvpx-vp9", "-b:v", "400k", "-deadline", "realtime", "-cpu-used", "8", "-c:a", "libopus", "-b:a", "96k"],
        [".mp3"] = [.. Sound, "-c:a", "libmp3lame", "-b:a", "128k"],
        [".m4a"] = [.. Sound, "-c:a", "aac", "-b:a", "128k"],
        [".aac"] = [.. Sound, "-c:a", "aac", "-b:a", "128k", "-f", "adts"],
        [".flac"] = [.. Sound, "-c:a", "flac"],
        [".wav"] = [.. Sound, "-c:a", "pcm_s16le"],
        [".ogg"] = [.. Sound, "-c:a", "libvorbis"],
        [".opus"] = [.. Sound, "-c:a", "libopus", "-b:a", "96k"],
        [".jpg"] = [.. Picture, "-q:v", "3"],
        [".jpeg"] = [.. Picture, "-q:v", "3", "-f", "mjpeg"],
        [".png"] = Picture,
        [".webp"] = [.. Picture, "-c:v", "libwebp"],
        [".bmp"] = Picture,
    };

    /// <summary>Every extension a file can be made for.</summary>
    internal static IReadOnlyCollection<string> Extensions => Recipes.Keys;

    /// <summary>The test file for <paramref name="extension"/>, made now if it is not there yet.</summary>
    internal static async Task<string> PathForAsync(string extension, CancellationToken cancellationToken)
    {
        var recipe = Recipes.TryGetValue(extension, out var found)
            ? found
            : throw new ArgumentException($"There is no test file recipe for {extension}.", nameof(extension));
        var folder = Path.Combine(CastPairHardwareFlowTests.FindProjectRoot(), "artifacts", "hardware-media");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "flint-test" + extension.ToLowerInvariant());
        if (File.Exists(path))
        {
            return path;
        }

        var start = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in (string[])["-hide_banner", "-loglevel", "error", "-y", .. recipe, path])
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("ffmpeg could not be started. Install it (winget install ffmpeg) to make the test media.");
        var errors = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            File.Delete(path);
            throw new InvalidOperationException($"ffmpeg could not make the {extension} test file: {errors}");
        }

        return path;
    }
}
