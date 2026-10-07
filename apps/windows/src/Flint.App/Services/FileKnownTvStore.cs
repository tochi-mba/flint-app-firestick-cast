using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flint.Core;

namespace Flint.App.Services;

/// <summary>Known TVs in <c>known-tvs.json</c>, each login readable only by this Windows account.</summary>
/// <remarks>
/// <para>
/// A login is protected with Windows data protection for the current user before it is written, so
/// the file copied to another account, or another PC, gives the TVs without their logins: Flint
/// asks for a code once there, rather than failing. A file that cannot be read is an empty list.
/// </para>
/// <para>
/// Storage failures are swallowed, as every Flint store does: a login that could not be written is
/// a code asked for once more, not an error in the person's way.
/// </para>
/// </remarks>
public sealed class FileKnownTvStore : IKnownTvStore
{
    /// <summary>The file's name.</summary>
    public const string FileName = "known-tvs.json";

    /// <summary>Ties the protected login to Flint, so another program's protected data is never taken for it.</summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("flint-known-tv-v1");

    private readonly string filePath;
    private List<KnownTv>? tvs;

    /// <summary>Creates the store under local application data.</summary>
    public FileKnownTvStore()
        : this(FlintDataFolder.Path)
    {
    }

    /// <summary>Creates the store rooted at a specific directory.</summary>
    /// <param name="directory">Where the file lives. Created on demand.</param>
    public FileKnownTvStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        filePath = Path.Combine(directory, FileName);
    }

    /// <inheritdoc />
    public IReadOnlyList<KnownTv> Load() => KnownTvList.Ordered(Cached());

    /// <inheritdoc />
    public void Save(KnownTv tv)
    {
        ArgumentNullException.ThrowIfNull(tv);
        Write([.. KnownTvList.With(Cached(), tv)]);
    }

    /// <inheritdoc />
    public void Forget(string name) => Write([.. Cached().Where(tv => tv.Name != name)]);

    /// <inheritdoc />
    public void ForgetAll() => Write([]);

    private List<KnownTv> Cached() => tvs ??= Read();

    private List<KnownTv> Read()
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return [];
            }

            var stored = JsonSerializer.Deserialize<List<StoredTv>>(File.ReadAllText(filePath)) ?? [];
            return
            [
                .. stored
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Name) && !string.IsNullOrWhiteSpace(entry.Address)
                        && entry.ReceiverPort is >= 1 and <= 65_535)
                    .Select(entry => new KnownTv(entry.Name, entry.Address, entry.ReceiverPort, Unprotect(entry.Login), entry.LastConnected)),
            ];
        }
        catch (Exception exception) when (IsStorageFailure(exception) || exception is JsonException)
        {
            return [];
        }
    }

    private void Write(List<KnownTv> next)
    {
        tvs = next;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            var stored = next.Select(tv => new StoredTv(tv.Name, tv.Address, tv.ReceiverPort, Protect(tv.Token), tv.LastConnected));
            File.WriteAllText(filePath, JsonSerializer.Serialize(stored));
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
        }
    }

    private static string? Protect(string? token) => token is null
        ? null
        : Convert.ToBase64String(ProtectedData.Protect(Encoding.ASCII.GetBytes(token), Entropy, DataProtectionScope.CurrentUser));

    private static string? Unprotect(string? login)
    {
        if (login is null)
        {
            return null;
        }

        try
        {
            var token = Encoding.ASCII.GetString(ProtectedData.Unprotect(Convert.FromBase64String(login), Entropy, DataProtectionScope.CurrentUser));
            return Flint.Session.CastSession.IsWellFormedToken(token) ? token : null;
        }
        catch (CryptographicException)
        {
            // Another account's, or not Flint's: the TV is kept, and asks for a code once.
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool IsStorageFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException;

    /// <summary>One TV as written: the login protected, never in clear text.</summary>
    private sealed record StoredTv(string Name, string Address, int ReceiverPort, string? Login, DateTimeOffset LastConnected);
}
