using Flint.Core.Settings;

namespace Flint.App.Services;

/// <summary>
/// Keeps settings in <c>settings.json</c> beside Flint's other files.
/// </summary>
/// <remarks>
/// <para>
/// Under <c>%LOCALAPPDATA%\REX Technologies\Flint</c>, like the onboarding marker and the remembered
/// TVs, so an update that replaces the program leaves them where they were.
/// </para>
/// <para>
/// Written to a temporary file and then moved over the real one, so a crash or a full disk part way
/// through a write leaves the previous settings rather than half of the new ones. Every storage
/// failure is swallowed: settings that could not be kept still hold for this run, and that is not
/// worth interrupting anyone over.
/// </para>
/// </remarks>
public sealed class FileAppSettingsStore : IAppSettingsStore
{
    /// <summary>The settings file's name.</summary>
    public const string FileName = "settings.json";

    private readonly string filePath;
    private readonly string temporaryPath;

    /// <summary>Creates the store under local application data.</summary>
    public FileAppSettingsStore()
        : this(FlintDataFolder.Path)
    {
    }

    /// <summary>Creates the store rooted at a specific directory.</summary>
    /// <param name="directory">Where the file lives. Created on demand.</param>
    public FileAppSettingsStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        filePath = System.IO.Path.Combine(directory, FileName);
        temporaryPath = filePath + ".tmp";
    }

    /// <summary>Where the settings are kept.</summary>
    internal string FilePath => filePath;

    /// <inheritdoc />
    public AppSettings Load()
    {
        try
        {
            return File.Exists(filePath) ? AppSettingsJson.Parse(File.ReadAllText(filePath)) : AppSettings.Default;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            return AppSettings.Default;
        }
    }

    /// <inheritdoc />
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        try
        {
            var directory = System.IO.Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(temporaryPath, AppSettingsJson.Serialize(settings));
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            TryDelete(temporaryPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
        }
    }

    private static bool IsStorageFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException;
}
