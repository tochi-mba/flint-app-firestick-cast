using System.Runtime.Versioning;
using Flint.Core;

namespace Flint.App.Services;

/// <summary>
/// Persists onboarding completion as a marker file under the user's local application data.
/// </summary>
/// <remarks>
/// <para>
/// A file rather than the registry, and local application data rather than roaming: this is a
/// per-machine convenience, not a setting worth syncing, and Flint's whole answer depends on the
/// hardware in front of it.
/// </para>
/// <para>
/// Every operation swallows I/O failure by design. A read-only profile or a locked directory must
/// not stop the application launching; the cost of failing to persist is seeing the introduction
/// again, which is recoverable, unlike a crash on startup.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class FileOnboardingState : IOnboardingState
{
    /// <summary>The vendor folder, shared by REX products on this machine.</summary>
    public const string VendorFolder = "REX Technologies";

    /// <summary>The product folder.</summary>
    public const string ProductFolder = "Flint";

    /// <summary>The marker file name.</summary>
    public const string MarkerFileName = "onboarding-complete";

    private readonly string _markerPath;

    /// <summary>Creates the store, resolving its path under local application data.</summary>
    public FileOnboardingState()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            VendorFolder,
            ProductFolder))
    {
    }

    /// <summary>Creates the store rooted at a specific directory.</summary>
    /// <param name="directory">Where the marker lives. Created on demand.</param>
    public FileOnboardingState(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _markerPath = Path.Combine(directory, MarkerFileName);
    }

    /// <inheritdoc />
    public bool HasCompleted
    {
        get
        {
            try
            {
                return File.Exists(_markerPath);
            }
            catch (Exception exception) when (IsIoFailure(exception))
            {
                // Unreadable means unknown, and unknown means show the introduction.
                return false;
            }
        }
    }

    /// <inheritdoc />
    public void MarkCompleted()
    {
        try
        {
            var directory = Path.GetDirectoryName(_markerPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_markerPath, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch (Exception exception) when (IsIoFailure(exception))
        {
            // Failing to remember is not worth interrupting the user over.
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        try
        {
            File.Delete(_markerPath);
        }
        catch (Exception exception) when (IsIoFailure(exception))
        {
            // Nothing to do: the marker is either already gone or unreachable.
        }
    }

    private static bool IsIoFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException
            or ArgumentException;
}
