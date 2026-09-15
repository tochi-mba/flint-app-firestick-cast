using System.Security.Cryptography;
using System.Text.Json;

namespace Flint.App.Services;

/// <summary>
/// Atomically persisted browser profiles encrypted to the current Windows user with DPAPI.
/// </summary>
/// <remarks>
/// A TV receives only a session projection of this data. This class never writes into receiver
/// storage, and a copied profile file cannot be decrypted by another Windows account.
/// </remarks>
public sealed class FileBrowserProfileLibraryStore : BrowserProfileLibraryStoreBase
{
    /// <summary>Suffix used for one opaque, encrypted profile document.</summary>
    public const string FileExtension = ".browser-profile";

    private const int FormatVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = false,
        MaxDepth = 16,
        ReadCommentHandling = JsonCommentHandling.Disallow,
    };
    private readonly string directory;

    /// <summary>Creates a current-user protected store rooted at an explicit directory.</summary>
    public FileBrowserProfileLibraryStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.directory = Path.GetFullPath(directory);
    }

    /// <inheritdoc />
    protected override BrowserProfileLibraryState Load(BrowserProfileId profile)
    {
        var path = PathFor(profile);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is <= 0 or > BrowserProfileLibraryLimits.MaxFileBytes)
            {
                return BrowserProfileLibraryState.Empty;
            }

            var encrypted = File.ReadAllBytes(path);
            byte[]? plain = null;
            try
            {
                plain = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
                var document = JsonSerializer.Deserialize<ProfileDocument>(plain, JsonOptions);
                if (document is null || document.Version != FormatVersion)
                {
                    return BrowserProfileLibraryState.Empty;
                }

                return new BrowserProfileLibraryState(
                    Normalize(document.Bookmarks, BrowserProfileLibraryLimits.MaxBookmarks),
                    Normalize(document.History, BrowserProfileLibraryLimits.MaxHistoryEntries),
                    document.Session?.Normalize(), NormalizeProfileNames(document.DeviceProfiles), document.SelectedDeviceProfile);
            }
            finally
            {
                if (plain is not null)
                {
                    CryptographicOperations.ZeroMemory(plain);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           CryptographicException or JsonException or NotSupportedException)
        {
            return BrowserProfileLibraryState.Empty;
        }
    }

    /// <inheritdoc />
    protected override bool TryPersist(BrowserProfileId profile, BrowserProfileLibraryState state)
    {
        // Windows AV / indexer scanners briefly lock newly written profile files. A single share
        // violation used to surface as ClearBookmarks/ClearHistory returning false under parallel
        // test load even though the in-memory mutation was valid.
        for (var attempt = 0; ; attempt++)
        {
            string? temporary = null;
            byte[]? plain = null;
            try
            {
                Directory.CreateDirectory(directory);
                var path = PathFor(profile);
                var document = new ProfileDocument(
                    FormatVersion,
                    state.Bookmarks.Select(ProfileEntry.From).ToArray(),
                    state.History.Select(ProfileEntry.From).ToArray(), state.Session?.Normalize(), state.DeviceProfiles, state.SelectedDeviceProfile);
                plain = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
                var encrypted = ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.CurrentUser);
                if (encrypted.Length > BrowserProfileLibraryLimits.MaxFileBytes)
                {
                    return false;
                }

                temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var stream = new FileStream(
                           temporary,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           bufferSize: 16 * 1024,
                           FileOptions.WriteThrough))
                {
                    stream.Write(encrypted);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporary, path, overwrite: true);
                temporary = null;
                return true;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(15 * (attempt + 1));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                               CryptographicException or NotSupportedException)
            {
                return false;
            }
            finally
            {
                if (plain is not null)
                {
                    CryptographicOperations.ZeroMemory(plain);
                }
                if (temporary is not null)
                {
                    try
                    {
                        File.Delete(temporary);
                    }
                    catch (IOException)
                    {
                        // The committed profile remains intact; a uniquely named orphan is harmless.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Best-effort cleanup cannot turn a safe failed mutation into an exception.
                    }
                }
            }
        }
    }

    private string PathFor(BrowserProfileId profile) =>
        Path.Combine(directory, profile.Value.ToString("N") + FileExtension);

    private static BrowserProfileLibraryEntry[] Normalize(ProfileEntry?[]? persisted, int maximum)
    {
        if (persisted is null)
        {
            return [];
        }

        var candidates = persisted
            .Where(entry => entry is not null)
            .Select(entry => BrowserProfileLibraryPolicy.CreateEntry(
                entry!.Url,
                entry.Title,
                entry.FaviconId,
                FromUnixMilliseconds(entry.LastVisitedMilliseconds)))
            .Where(entry => entry is not null)
            .Select(entry => entry!)
            .OrderByDescending(entry => entry.LastVisitedMilliseconds);
        var result = new List<BrowserProfileLibraryEntry>(Math.Min(persisted.Length, maximum));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in candidates)
        {
            if (seen.Add(entry.Url))
            {
                result.Add(entry);
                if (result.Count == maximum)
                {
                    break;
                }
            }
        }

        return [.. result];
    }

    private static DateTimeOffset FromUnixMilliseconds(long milliseconds)
    {
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(0, milliseconds));
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTimeOffset.UnixEpoch;
        }
    }

    private sealed record ProfileDocument(int Version, ProfileEntry?[]? Bookmarks, ProfileEntry?[]? History, BrowserSavedSession? Session = null,
        Dictionary<Guid, string>? DeviceProfiles = null, Guid? SelectedDeviceProfile = null);

    private sealed record ProfileEntry(
        string? Url,
        string? Title,
        long FaviconId,
        long LastVisitedMilliseconds)
    {
        public static ProfileEntry From(BrowserProfileLibraryEntry entry) => new(
            entry.Url,
            entry.Title,
            entry.FaviconId,
            entry.LastVisitedMilliseconds);
    }
}
