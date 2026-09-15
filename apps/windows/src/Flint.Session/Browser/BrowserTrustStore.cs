using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace Flint.Session.Browser;

/// <summary>A receiver identity and the exact full SPKI pin accepted for it.</summary>
public sealed record BrowserTrustedReceiver
{
    /// <summary>Creates a validated durable trust entry.</summary>
    public BrowserTrustedReceiver(string receiverIdentity, BrowserFingerprint fingerprint, DateTimeOffset pinnedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(receiverIdentity) || receiverIdentity.Length > 512)
        {
            throw new ArgumentException("Receiver identity must be a non-empty bounded value.", nameof(receiverIdentity));
        }

        ReceiverIdentity = receiverIdentity;
        Fingerprint = fingerprint ?? throw new ArgumentNullException(nameof(fingerprint));
        PinnedAtUtc = pinnedAtUtc.ToUniversalTime();
    }

    /// <summary>Stable discovery identity selected by the user; this is not an address or a secret.</summary>
    public string ReceiverIdentity { get; init; }

    /// <summary>The full, not human-shortened, pin.</summary>
    public BrowserFingerprint Fingerprint { get; init; }

    /// <summary>When explicit first-use confirmation completed.</summary>
    public DateTimeOffset PinnedAtUtc { get; init; }
}

/// <summary>Persists only full receiver pins; it never stores pairing codes, sessions, or page data.</summary>
public interface IBrowserTrustStore
{
    /// <summary>Looks up an exact receiver identity.</summary>
    ValueTask<BrowserTrustedReceiver?> FindAsync(string receiverIdentity, CancellationToken cancellationToken = default);

    /// <summary>Atomically replaces the pin for one explicitly selected receiver.</summary>
    ValueTask SaveAsync(BrowserTrustedReceiver receiver, CancellationToken cancellationToken = default);

    /// <summary>Forgets one explicitly selected receiver so first-use comparison is required again.</summary>
    ValueTask ForgetAsync(string receiverIdentity, CancellationToken cancellationToken = default);
}

/// <summary>Thread-safe non-persistent trust store for unit tests and explicitly ephemeral sessions.</summary>
public sealed class InMemoryBrowserTrustStore : IBrowserTrustStore
{
    private readonly ConcurrentDictionary<string, BrowserTrustedReceiver> entries = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask<BrowserTrustedReceiver?> FindAsync(string receiverIdentity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateIdentity(receiverIdentity);
        return ValueTask.FromResult(entries.TryGetValue(receiverIdentity, out var receiver) ? receiver : null);
    }

    /// <inheritdoc />
    public ValueTask SaveAsync(BrowserTrustedReceiver receiver, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(receiver);
        entries[receiver.ReceiverIdentity] = receiver;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask ForgetAsync(string receiverIdentity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateIdentity(receiverIdentity);
        entries.TryRemove(receiverIdentity, out _);
        return ValueTask.CompletedTask;
    }

    private static void ValidateIdentity(string receiverIdentity)
    {
        if (string.IsNullOrWhiteSpace(receiverIdentity))
        {
            throw new ArgumentException("Receiver identity is required.", nameof(receiverIdentity));
        }
    }
}

/// <summary>Raised when a locally protected trust file cannot be decrypted or parsed safely.</summary>
public sealed class BrowserTrustStoreCorruptException : BrowserTrustException
{
    /// <inheritdoc />
    public BrowserTrustStoreCorruptException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Windows-current-user encrypted, atomically replaced browser trust store.
/// </summary>
/// <remarks>
/// This is deliberately a narrow file store rather than a general secret vault: the only durable
/// content is a receiver identity and full public-key pin. Pairing codes, TLS session material,
/// URLs, browser text, cookies, and certificate bytes never enter this type.
/// </remarks>
public sealed class ProtectedFileBrowserTrustStore : IBrowserTrustStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>Creates a store at an explicit user-private file path.</summary>
    public ProtectedFileBrowserTrustStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = Path.GetFullPath(path);
    }

    /// <inheritdoc />
    public async ValueTask<BrowserTrustedReceiver?> FindAsync(
        string receiverIdentity,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(receiverIdentity);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return (await ReadEntriesAsync(cancellationToken).ConfigureAwait(false))
                .TryGetValue(receiverIdentity, out var receiver)
                ? receiver
                : null;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask SaveAsync(BrowserTrustedReceiver receiver, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receiver);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await ReadEntriesAsync(cancellationToken).ConfigureAwait(false);
            entries[receiver.ReceiverIdentity] = receiver;
            await WriteEntriesAsync(entries, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask ForgetAsync(string receiverIdentity, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(receiverIdentity);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await ReadEntriesAsync(cancellationToken).ConfigureAwait(false);
            if (!entries.Remove(receiverIdentity))
            {
                return;
            }

            if (entries.Count == 0)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                return;
            }

            await WriteEntriesAsync(entries, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<Dictionary<string, BrowserTrustedReceiver>> ReadEntriesAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, BrowserTrustedReceiver>(StringComparer.Ordinal);
        }

        try
        {
            var encrypted = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            var plain = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
            var document = JsonSerializer.Deserialize<TrustDocument>(plain, JsonOptions)
                ?? throw new JsonException("Trust document was empty.");
            var documentEntries = document.Entries;
            if (documentEntries is null || documentEntries.Length > 512)
            {
                throw new JsonException("Trust document entry count was invalid.");
            }

            var entries = new Dictionary<string, BrowserTrustedReceiver>(StringComparer.Ordinal);
            foreach (var entry in documentEntries)
            {
                var trusted = new BrowserTrustedReceiver(
                    entry.ReceiverIdentity,
                    BrowserFingerprint.ParseFullPin(entry.FullPin),
                    entry.PinnedAtUtc);
                if (!entries.TryAdd(trusted.ReceiverIdentity, trusted))
                {
                    throw new JsonException("Trust document contained a duplicate receiver identity.");
                }
            }

            return entries;
        }
        catch (BrowserTrustStoreCorruptException)
        {
            throw;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or BrowserTrustException or IOException)
        {
            throw new BrowserTrustStoreCorruptException(
                "The browser trust store is unavailable or corrupt. Forget the receiver before pairing again.",
                exception);
        }
    }

    private async Task WriteEntriesAsync(
        IReadOnlyDictionary<string, BrowserTrustedReceiver> entries,
        CancellationToken cancellationToken)
    {
        var document = new TrustDocument(entries.Values
            .OrderBy(entry => entry.ReceiverIdentity, StringComparer.Ordinal)
            .Select(entry => new TrustEntry(entry.ReceiverIdentity, entry.Fingerprint.FullPin, entry.PinnedAtUtc))
            .ToArray());
        var plain = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        var encrypted = ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.CurrentUser);
        var directory = Path.GetDirectoryName(path)
            ?? throw new BrowserTrustException("The browser trust-store path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, encrypted, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void ValidateIdentity(string receiverIdentity)
    {
        if (string.IsNullOrWhiteSpace(receiverIdentity) || receiverIdentity.Length > 512)
        {
            throw new ArgumentException("Receiver identity must be a non-empty bounded value.", nameof(receiverIdentity));
        }
    }

    private sealed record TrustDocument(TrustEntry[] Entries);
    private sealed record TrustEntry(string ReceiverIdentity, string FullPin, DateTimeOffset PinnedAtUtc);
}
