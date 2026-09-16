using System.Runtime.Versioning;

namespace Flint.App.Services;

/// <summary>Whether Flint may look for updates on its own.</summary>
public interface IUpdatePreference
{
    /// <summary>Whether a check runs by itself at launch.</summary>
    bool ChecksAutomatically { get; set; }
}

/// <summary>
/// Remembers the update preference as a marker file beside the onboarding one.
/// </summary>
/// <remarks>
/// The default is to check, and the file records the one answer worth storing: that somebody turned
/// it off. A network request the person did not ask for is the kind of thing they are entitled to
/// stop, and it has to stay stopped across restarts.
/// <para>
/// This lives under <c>REX Technologies\Flint</c> rather than in the install folder, so an update
/// that replaces the program leaves the answer where it was, as
/// <see cref="FileOnboardingState"/> does.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class FileUpdatePreference : IUpdatePreference
{
    /// <summary>The marker file name, present only when checks are turned off.</summary>
    public const string MarkerFileName = "updates-manual";

    private readonly string markerPath;

    /// <summary>Creates the store under local application data.</summary>
    public FileUpdatePreference()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            FileOnboardingState.VendorFolder,
            FileOnboardingState.ProductFolder))
    {
    }

    /// <summary>Creates the store rooted at a specific directory.</summary>
    /// <param name="directory">Where the marker lives. Created on demand.</param>
    public FileUpdatePreference(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        markerPath = Path.Combine(directory, MarkerFileName);
    }

    /// <inheritdoc />
    public bool ChecksAutomatically
    {
        get
        {
            try
            {
                return !File.Exists(markerPath);
            }
            catch (Exception exception) when (IsIoFailure(exception))
            {
                // Unreadable means unknown, and unknown means the default: check.
                return true;
            }
        }

        set
        {
            try
            {
                if (value)
                {
                    File.Delete(markerPath);
                    return;
                }

                var directory = Path.GetDirectoryName(markerPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(markerPath, DateTimeOffset.UtcNow.ToString("O"));
            }
            catch (Exception exception) when (IsIoFailure(exception))
            {
                // Failing to remember is not worth interrupting anyone over. The preference holds
                // for this run either way, because the view model keeps its own copy.
            }
        }
    }

    private static bool IsIoFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException
            or ArgumentException;
}
