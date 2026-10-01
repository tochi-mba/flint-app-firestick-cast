using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flint.App.Services;
using Flint.Core.Settings;

namespace Flint.App.ViewModels.Settings;

/// <summary>
/// The Privacy and data section: what Flint keeps, where, and how to take it with you or start over.
/// </summary>
public sealed partial class PrivacySettingsViewModel : SettingsSectionViewModel
{
    /// <summary>The largest settings file an import will read.</summary>
    /// <remarks>A real one is well under a kilobyte; anything this size is not a Flint settings file.</remarks>
    internal const int MaxImportBytes = 256 * 1024;

    private readonly ISettingsService settings;
    private readonly IFolderOpener folders;
    private readonly string dataFolder;
    private readonly string logsFolder;

    /// <summary>Creates the section.</summary>
    /// <param name="settings">The live settings, for export, import and reset.</param>
    /// <param name="folders">Shows a folder in File Explorer.</param>
    /// <param name="dataFolder">Where Flint keeps what it remembers.</param>
    /// <param name="logsFolder">Where Flint writes its log.</param>
    public PrivacySettingsViewModel(ISettingsService settings, IFolderOpener folders, string dataFolder, string logsFolder)
        : base("Privacy and data")
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.folders = folders ?? throw new ArgumentNullException(nameof(folders));
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(logsFolder);
        this.dataFolder = dataFolder;
        this.logsFolder = logsFolder;
        ResetAll = new ConfirmableAction(
            "RESET ALL SETTINGS",
            "Put every setting back to how Flint came? Remembered TVs and browser profiles are kept.",
            "RESET EVERYTHING",
            () =>
            {
                settings.Update(_ => AppSettings.Default);
                Status = "Every setting is back to how Flint came.";
            });
    }

    /// <summary>What Flint keeps on this PC.</summary>
    public SettingText KeptText { get; } = new(
        "What Flint keeps",
        "Everything stays on this PC. Nothing is sent anywhere except to your TV.");

    /// <summary>Opening the data folder.</summary>
    public SettingText DataFolderText { get; } = new(
        "Open the data folder",
        "Where your settings, remembered TVs and browser profiles are kept.");

    /// <summary>Opening the logs folder.</summary>
    public SettingText LogsFolderText { get; } = new(
        "Open the logs folder",
        "Flint's record of what it did, kept under 8 MB. It never holds pairing codes or page contents.");

    /// <summary>Exporting and importing.</summary>
    public SettingText TransferText { get; } = new(
        "Export or import settings",
        "Save your settings to a file, or load them on this or another PC.");

    /// <summary>Resetting everything.</summary>
    public SettingText ResetText { get; } = new(
        "Reset all settings",
        "Puts every setting back to how Flint came.");

    /// <inheritdoc />
    public override IReadOnlyList<SettingText> Settings => [KeptText, DataFolderText, LogsFolderText, TransferText, ResetText];

    /// <summary>What is kept, one line each, in plain words.</summary>
    public IReadOnlyList<string> KeptItems { get; } =
    [
        "Your settings.",
        "The TV addresses you typed.",
        "TVs you verified for the browser, and your browser profiles, protected so only your Windows account can read them.",
        "The key that lets Flint talk to your TV over ADB.",
        "Whether you have seen the introduction and the Web help.",
    ];

    /// <summary>The data folder, for display.</summary>
    public string DataFolder => dataFolder;

    /// <summary>The logs folder, for display.</summary>
    public string LogsFolder => logsFolder;

    /// <summary>The result of the last export, import or folder request.</summary>
    [ObservableProperty]
    private string? _status;

    /// <summary>Raised when the person asks to export; the view picks the file and calls <see cref="Export"/>.</summary>
    public event EventHandler? ExportRequested;

    /// <summary>Raised when the person asks to import; the view picks the file and calls <see cref="Import"/>.</summary>
    public event EventHandler? ImportRequested;

    /// <summary>Puts every setting back to how Flint came.</summary>
    public ConfirmableAction ResetAll { get; }

    /// <summary>Writes the current settings to <paramref name="destination"/>.</summary>
    public void Export(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        try
        {
            var bytes = Encoding.UTF8.GetBytes(AppSettingsJson.Serialize(settings.Current));
            destination.Write(bytes);
            destination.Flush();
            Status = "Settings exported.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Status = "The settings could not be written to that file.";
        }
    }

    /// <summary>Reads settings from <paramref name="source"/> and uses them.</summary>
    /// <remarks>
    /// A file that is not a Flint settings file changes nothing. One that is, but has a section
    /// that cannot be read, is used with that section at its defaults, as it would be at launch.
    /// </remarks>
    public void Import(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        string text;
        try
        {
            using var reader = new StreamReader(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var buffer = new char[MaxImportBytes + 1];
            var read = reader.ReadBlock(buffer, 0, buffer.Length);
            if (read > MaxImportBytes)
            {
                Status = "That file is too large to be Flint settings. Nothing was changed.";
                return;
            }

            text = new string(buffer, 0, read);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or DecoderFallbackException)
        {
            Status = "That file could not be read. Nothing was changed.";
            return;
        }

        if (!AppSettingsJson.LooksLikeSettings(text))
        {
            Status = "That file is not Flint settings. Nothing was changed.";
            return;
        }

        settings.Update(_ => AppSettingsJson.Parse(text));
        Status = "Settings imported.";
    }

    [RelayCommand]
    private void RequestExport() => ExportRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void RequestImport() => ImportRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenDataFolder() => Open(dataFolder);

    [RelayCommand]
    private void OpenLogsFolder() => Open(logsFolder);

    private void Open(string path)
    {
        if (!folders.Open(path))
        {
            Status = $"Windows could not open {path}.";
        }
    }
}
