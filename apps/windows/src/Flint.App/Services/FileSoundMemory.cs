using System.Text.Json;
using Flint.Core;

namespace Flint.App.Services;

/// <summary>Keeps what Flint knows about this PC's sound outputs, in <c>sound-outputs.json</c>.</summary>
/// <remarks>
/// Every change is written at once, because the mute waiting to be put back must be on disk before
/// the output is muted: that is the whole point of keeping it. A file that cannot be read is
/// treated as empty, and one that cannot be written is kept for this run only.
/// </remarks>
public sealed class FileSoundMemory : ISoundMemory
{
    /// <summary>The file's name.</summary>
    public const string FileName = "sound-outputs.json";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private readonly string filePath;
    private Saved? saved;

    /// <summary>Creates the store under local application data.</summary>
    public FileSoundMemory()
        : this(FlintDataFolder.Path)
    {
    }

    /// <summary>Creates the store rooted at a specific directory.</summary>
    /// <param name="directory">Where the file lives. Created on demand.</param>
    public FileSoundMemory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        filePath = Path.Combine(directory, FileName);
    }

    /// <inheritdoc />
    public MuteToRestore? PendingRestore => Current.PendingRestore;

    /// <inheritdoc />
    public void SetPendingRestore(MuteToRestore? restore) => Save(Current with { PendingRestore = restore });

    /// <inheritdoc />
    public bool MuteSilences(string deviceId) =>
        Current.MuteSilences.Contains(deviceId, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void RememberMuteSilences(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        if (!MuteSilences(deviceId))
        {
            Save(Current with { MuteSilences = Current.MuteSilences.Append(deviceId).ToArray() });
        }
    }

    private Saved Current => saved ??= Load();

    private void Save(Saved next)
    {
        saved = next;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, JsonSerializer.Serialize(next, Json));
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            // Kept for this run. A mute left behind by a crash is then put back by hand, as it would
            // have been before Flint remembered anything.
        }
    }

    private Saved Load()
    {
        try
        {
            var read = File.Exists(filePath)
                ? JsonSerializer.Deserialize<Saved>(File.ReadAllText(filePath), Json)
                : null;
            return Clean(read);
        }
        catch (Exception exception) when (IsStorageFailure(exception) || exception is JsonException)
        {
            return new Saved();
        }
    }

    /// <summary>What was read, with anything malformed left out.</summary>
    private static Saved Clean(Saved? read) => new()
    {
        PendingRestore = read?.PendingRestore is { DeviceId: { Length: > 0 } } pending ? pending : null,
        MuteSilences = [.. (read?.MuteSilences ?? []).Where(id => !string.IsNullOrWhiteSpace(id))],
    };

    private static bool IsStorageFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException;

    /// <summary>The file's shape.</summary>
    internal sealed record Saved
    {
        public MuteToRestore? PendingRestore { get; init; }

        public IReadOnlyList<string> MuteSilences { get; init; } = [];
    }
}
