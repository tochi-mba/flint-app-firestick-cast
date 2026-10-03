using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flint.Core;

namespace Flint.App.ViewModels.Settings;

/// <summary>The About section: which Flint this is, and a way to say so in a bug report.</summary>
public sealed partial class AboutSettingsViewModel : SettingsSectionViewModel
{
    private const string ClipboardRefused = "Windows would not let Flint use the clipboard. Try again in a moment.";

    private readonly CastPageViewModel cast;
    private readonly string windowsVersion;
    private readonly WhatsNewViewModel? whatsNew;

    /// <summary>Creates the section.</summary>
    /// <param name="cast">The TV this PC last looked at, for the report.</param>
    /// <param name="appVersion">This build's version.</param>
    /// <param name="engineVersion">The native engine's version, or null when it could not be loaded.</param>
    /// <param name="windowsVersion">Which Windows this is.</param>
    /// <param name="whatsNew">The "What's new" walkthrough, to open again from here.</param>
    public AboutSettingsViewModel(
        CastPageViewModel cast,
        string appVersion,
        string? engineVersion,
        string windowsVersion,
        WhatsNewViewModel? whatsNew = null)
        : base("About")
    {
        this.whatsNew = whatsNew;
        this.cast = cast ?? throw new ArgumentNullException(nameof(cast));
        ArgumentException.ThrowIfNullOrWhiteSpace(appVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(windowsVersion);
        AppVersion = appVersion;
        EngineVersion = string.IsNullOrWhiteSpace(engineVersion) ? "Not loaded" : engineVersion;
        this.windowsVersion = windowsVersion;
    }

    /// <summary>The version.</summary>
    public SettingText VersionText { get; } = new(
        "Version",
        "Which Flint this is, and which capture engine it loaded.");

    /// <summary>Copying details for a bug report.</summary>
    public SettingText ReportText { get; } = new(
        "Copy details for a bug report",
        "Copies the versions and the TV model, so a report says exactly what was running.");

    /// <summary>Seeing what has changed.</summary>
    public SettingText WhatsNewText { get; } = new(
        "See what's new",
        "Shows the recent changes to Flint again, the same walkthrough that appears after an update.");

    /// <summary>Whether "What's new" can be opened from here.</summary>
    public bool HasWhatsNew => whatsNew is not null;

    /// <summary>The licence.</summary>
    public SettingText LicenceText { get; } = new(
        "Licence",
        "MIT. Copyright (c) 2026 REX Technologies.");

    /// <inheritdoc />
    public override IReadOnlyList<SettingText> Settings => [VersionText, WhatsNewText, ReportText, LicenceText];

    /// <summary>This build's version.</summary>
    public string AppVersion { get; }

    /// <summary>The native engine's version.</summary>
    public string EngineVersion { get; }

    /// <summary>Whether the last copy worked, once one was attempted.</summary>
    [ObservableProperty]
    private string? _status;

    /// <summary>Raised when the person asks to copy; the view calls <see cref="CopyReportWithAsync"/> with its clipboard.</summary>
    public event EventHandler? CopyRequested;

    /// <summary>
    /// The details a bug report needs, and nothing else.
    /// </summary>
    /// <remarks>
    /// No address, name or code: the TV is described by its model and platform, which is what
    /// decides its behaviour, rather than by anything that identifies the person's home.
    /// </remarks>
    public string ReportDetails
    {
        get
        {
            var details = new StringBuilder()
                .AppendLine($"Flint {AppVersion}")
                .AppendLine($"Engine {EngineVersion}")
                .AppendLine($"Windows {windowsVersion}");
            if (cast.Report?.Device is { } device)
            {
                details.AppendLine($"TV {device.Model ?? "model not reported"}, {device.Platform.ToDisplayLabel()}");
            }

            return details.ToString().TrimEnd();
        }
    }

    /// <summary>Puts <see cref="ReportDetails"/> on the clipboard through <paramref name="setText"/>.</summary>
    /// <param name="setText">Writes text to the clipboard, or null when this window has none.</param>
    /// <remarks>
    /// Never throws. Windows refuses the clipboard while another program holds it, and a copy that
    /// failed is something to say on the page, not a reason for Flint to stop.
    /// </remarks>
    public async Task CopyReportWithAsync(Func<string, Task>? setText)
    {
        if (setText is null)
        {
            Status = ClipboardRefused;
            return;
        }

        try
        {
            await setText(ReportDetails).ConfigureAwait(true);
            Status = "Copied. Paste it into your report.";
        }
#pragma warning disable CA1031 // Whatever the clipboard throws, the answer on the page is the same.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            Flint.Core.FlintDiag.Warn("FlintUi", $"bug report copy failed: {exception.GetType().Name}");
            Status = ClipboardRefused;
        }
    }

    /// <summary>Opens "What's new" again.</summary>
    [RelayCommand]
    private void ShowWhatsNew() => whatsNew?.ShowAll();

    [RelayCommand]
    private void CopyReport() => CopyRequested?.Invoke(this, EventArgs.Empty);
}
