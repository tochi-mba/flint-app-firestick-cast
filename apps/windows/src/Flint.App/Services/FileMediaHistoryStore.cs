using System.Text.Json;
using Flint.Core.Media;

namespace Flint.App.Services;

/// <summary>
/// Keeps where played files stopped, in <c>media-history.json</c> beside Flint's other files.
/// </summary>
/// <remarks>
/// At most <see cref="MaxEntries"/>, the least recently played dropped first. Files are named only
/// by <see cref="ResumePolicy.KeyFor"/>'s hash, so the file does not list what was watched. Every
/// storage failure is swallowed: a position that could not be kept costs a resume, not a crash.
/// </remarks>
public sealed class FileMediaHistoryStore : IMediaHistoryStore
{
    /// <summary>The file's name.</summary>
    public const string FileName = "media-history.json";

    /// <summary>The most positions kept.</summary>
    public const int MaxEntries = 200;

    private readonly string filePath;

    /// <summary>The entries, read from disk the first time they are needed and kept from then on.</summary>
    private List<MediaHistoryEntry>? entries;

    /// <summary>Creates the store under local application data.</summary>
    public FileMediaHistoryStore()
        : this(FlintDataFolder.Path)
    {
    }

    /// <summary>Creates the store rooted at a specific directory.</summary>
    /// <param name="directory">Where the file lives. Created on demand.</param>
    public FileMediaHistoryStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        filePath = System.IO.Path.Combine(directory, FileName);
    }

    /// <inheritdoc />
    public MediaHistoryEntry? Find(string key) => Load().FirstOrDefault(entry => entry.Key == key);

    /// <inheritdoc />
    public void Save(MediaHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Write(
        [
            .. Load()
                .Where(existing => existing.Key != entry.Key)
                .Append(entry)
                .OrderByDescending(existing => existing.LastPlayed)
                .Take(MaxEntries),
        ]);
    }

    /// <inheritdoc />
    public void Forget(string key)
    {
        var entries = Load();
        if (entries.Any(entry => entry.Key == key))
        {
            Write([.. entries.Where(entry => entry.Key != key)]);
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        entries = [];
        try
        {
            File.Delete(filePath);
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
        }
    }

    private List<MediaHistoryEntry> Load() => entries ??= Read();

    private List<MediaHistoryEntry> Read()
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return [];
            }

            var entries = JsonSerializer.Deserialize<List<MediaHistoryEntry>>(File.ReadAllText(filePath)) ?? [];
            return [.. entries.Where(entry => entry is { Key.Length: 64, PositionMs: >= 0 })];
        }
        catch (Exception exception) when (IsStorageFailure(exception) || exception is JsonException)
        {
            return [];
        }
    }

    private void Write(List<MediaHistoryEntry> next)
    {
        entries = next;
        try
        {
            var directory = System.IO.Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, JsonSerializer.Serialize(next));
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
        }
    }

    private static bool IsStorageFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException;
}
