using System.Text.Json;
using Flint.Core;

namespace Flint.App.Services;

/// <summary>Keeps the "What's new" highlights already seen, in <c>whats-new-seen.json</c>.</summary>
/// <remarks>
/// A missing file is "nothing recorded", which is what an update from a copy older than this store
/// should read as. A file that cannot be read is treated the same way: showing a highlight twice is
/// a small annoyance, while a crash at launch is not.
/// </remarks>
public sealed class FileWhatsNewState : IWhatsNewState
{
    /// <summary>The file's name.</summary>
    public const string FileName = "whats-new-seen.json";

    private readonly string filePath;
    private HashSet<string>? seen;
    private bool loaded;

    /// <summary>Creates the store under local application data.</summary>
    public FileWhatsNewState()
        : this(FlintDataFolder.Path)
    {
    }

    /// <summary>Creates the store rooted at a specific directory.</summary>
    /// <param name="directory">Where the file lives. Created on demand.</param>
    public FileWhatsNewState(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        filePath = Path.Combine(directory, FileName);
    }

    /// <inheritdoc />
    public IReadOnlySet<string>? Seen
    {
        get
        {
            if (!loaded)
            {
                seen = Load();
                loaded = true;
            }

            return seen;
        }
    }

    /// <inheritdoc />
    public void MarkSeen(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var next = new HashSet<string>(Seen ?? new HashSet<string>(), StringComparer.Ordinal);
        next.UnionWith(ids);
        seen = next;
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, JsonSerializer.Serialize(next.Order(StringComparer.Ordinal)));
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            // Remembered for this run; the highlights may show once more next time.
        }
    }

    private HashSet<string>? Load()
    {
        try
        {
            return File.Exists(filePath)
                ? new HashSet<string>(
                    JsonSerializer.Deserialize<string[]>(File.ReadAllText(filePath))?.Where(id => !string.IsNullOrWhiteSpace(id)) ?? [],
                    StringComparer.Ordinal)
                : null;
        }
        catch (Exception exception) when (IsStorageFailure(exception) || exception is JsonException)
        {
            return null;
        }
    }

    private static bool IsStorageFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException;
}
