using Avalonia.Platform.Storage;
using Flint.App.ViewModels.Settings;

namespace Flint.App.Services;

/// <summary>
/// Moves settings between the Privacy section and a file the person chose.
/// </summary>
/// <remarks>
/// The view supplies how a file is chosen and opened; everything that can go wrong after that is
/// handled here, where a test can reach it. A file that cannot be opened is reported on the page,
/// never thrown: the view's handlers are <c>async void</c>, and an exception escaping one ends Flint.
/// </remarks>
internal static class SettingsFileTransfer
{
    /// <summary>The file type both pickers offer.</summary>
    internal static FilePickerFileType JsonFiles { get; } = new("Flint settings")
    {
        Patterns = ["*.json"],
        MimeTypes = ["application/json"],
    };

    /// <summary>Writes the current settings to the file <paramref name="openDestination"/> opens.</summary>
    /// <param name="openDestination">Asks where to save and opens it, or returns null when the person cancelled.</param>
    /// <param name="section">Where the settings come from and the result is reported.</param>
    internal static async Task ExportAsync(Func<Task<Stream?>> openDestination, PrivacySettingsViewModel section)
    {
        ArgumentNullException.ThrowIfNull(openDestination);
        ArgumentNullException.ThrowIfNull(section);
        try
        {
            var stream = await openDestination().ConfigureAwait(true);
            if (stream is null)
            {
                return;
            }

            await using (stream.ConfigureAwait(true))
            {
                section.Export(stream);
            }
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            section.Status = "That file could not be opened for writing. Nothing was saved.";
        }
    }

    /// <summary>Reads settings from the file <paramref name="openSource"/> opens and uses them.</summary>
    /// <param name="openSource">Asks which file to read and opens it, or returns null when the person cancelled.</param>
    /// <param name="section">Where the settings go and the result is reported.</param>
    internal static async Task ImportAsync(Func<Task<Stream?>> openSource, PrivacySettingsViewModel section)
    {
        ArgumentNullException.ThrowIfNull(openSource);
        ArgumentNullException.ThrowIfNull(section);
        try
        {
            var stream = await openSource().ConfigureAwait(true);
            if (stream is null)
            {
                return;
            }

            await using (stream.ConfigureAwait(true))
            {
                section.Import(stream);
            }
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            section.Status = "That file could not be opened. Nothing was changed.";
        }
    }

    private static bool IsFileFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException;
}
