namespace Flint.Core;

/// <summary>
/// Keeps a developer log file under a hard size cap by discarding the oldest bytes first.
/// </summary>
/// <remarks>
/// Used by the Windows shell log and any other session artifact that must stay bounded without
/// wiping the newest diagnostics. Trims to a retain size below the cap so the next few writes do
/// not immediately re-trigger a trim.
/// </remarks>
public static class DevLogRetention
{
    /// <summary>Default hard cap (8 MiB) before oldest content is discarded.</summary>
    public const long DefaultMaximumBytes = 8 * 1024 * 1024;

    /// <summary>
    /// Default size kept after a trim (6 MiB). Must stay strictly below
    /// <see cref="DefaultMaximumBytes"/>.
    /// </summary>
    public const long DefaultRetainBytes = 6 * 1024 * 1024;

    /// <summary>
    /// Returns the newest portion of <paramref name="content"/> sized for
    /// <paramref name="retainBytes"/>, aligned to the first newline after the cut when possible.
    /// </summary>
    /// <remarks>
    /// When the keep window starts mid-line, the partial first line is dropped so the retained
    /// file always begins on a complete log line.
    /// </remarks>
    public static byte[] KeepNewest(ReadOnlySpan<byte> content, long retainBytes)
    {
        if (retainBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retainBytes), "Retain size must be positive.");
        }

        if (content.Length == 0)
        {
            return [];
        }

        if (content.Length <= retainBytes)
        {
            return content.ToArray();
        }

        var start = content.Length - (int)Math.Min(retainBytes, content.Length);
        var newline = content[start..].IndexOf((byte)'\n');
        if (newline >= 0 && start + newline + 1 < content.Length)
        {
            start += newline + 1;
        }

        return content[start..].ToArray();
    }

    /// <summary>
    /// When <paramref name="path"/> exceeds <paramref name="maximumBytes"/>, rewrites it so only
    /// the newest <paramref name="retainBytes"/> (newline-aligned) remain.
    /// </summary>
    /// <returns><see langword="true"/> when the file was trimmed.</returns>
    public static bool TrimOldestIfNeeded(string path, long maximumBytes, long retainBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ValidateBounds(maximumBytes, retainBytes);

        if (!File.Exists(path))
        {
            return false;
        }

        var info = new FileInfo(path);
        if (info.Length <= maximumBytes)
        {
            return false;
        }

        byte[] original;
        using (var read = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            original = new byte[read.Length];
            var offset = 0;
            while (offset < original.Length)
            {
                var readCount = read.Read(original, offset, original.Length - offset);
                if (readCount == 0)
                {
                    break;
                }

                offset += readCount;
            }

            if (offset != original.Length)
            {
                Array.Resize(ref original, offset);
            }
        }

        var kept = KeepNewest(original, retainBytes);
        if (kept.Length == original.Length)
        {
            return false;
        }

        var temporary = path + ".trim.tmp";
        try
        {
            File.WriteAllBytes(temporary, kept);
            File.Copy(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        return true;
    }

    /// <summary>Rejects retain sizes that cannot leave headroom under the hard cap.</summary>
    public static void ValidateBounds(long maximumBytes, long retainBytes)
    {
        if (maximumBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes), "Maximum size must be positive.");
        }

        if (retainBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(retainBytes), "Retain size must be positive.");
        }

        if (retainBytes >= maximumBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retainBytes),
                "Retain size must be strictly below the maximum so a trim leaves headroom.");
        }
    }
}
