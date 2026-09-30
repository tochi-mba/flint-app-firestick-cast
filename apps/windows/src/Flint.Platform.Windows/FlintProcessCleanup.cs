using System.Diagnostics;

namespace Flint.Platform.Windows;

/// <summary>Stops verified Flint shells that were launched from a different installation folder.</summary>
/// <remarks>
/// Velopack already stops processes beneath the installation it is replacing. This closes the one
/// gap it intentionally cannot: a portable extraction elsewhere on disk. Files are never deleted;
/// only running executables carrying Flint's product and publisher metadata are considered.
/// </remarks>
public static class FlintProcessCleanup
{
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(4);

    /// <summary>Closes other verified Flint shells, then terminates any that ignored the close.</summary>
    public static void StopOtherCopies()
    {
        var current = Environment.ProcessId;
        var candidates = Process.GetProcessesByName("Flint.App");

        try
        {
            var copies = candidates
                .Where(process => process.Id != current && IsFlint(process))
                .ToArray();
            foreach (var process in copies)
            {
                Try(() => process.CloseMainWindow());
            }

            var deadline = Stopwatch.StartNew();
            foreach (var process in copies)
            {
                var remaining = GracePeriod - deadline.Elapsed;
                if (remaining > TimeSpan.Zero && Try(() => process.WaitForExit(remaining)))
                {
                    continue;
                }

                Try(() => process.Kill(entireProcessTree: true));
            }
        }
        finally
        {
            foreach (var process in candidates)
            {
                process.Dispose();
            }
        }
    }

    internal static bool HasFlintIdentity(string? productName, string? companyName) =>
        string.Equals(productName, "Flint", StringComparison.Ordinal)
        && string.Equals(companyName, "REX Technologies", StringComparison.Ordinal);

    private static bool IsFlint(Process process) => Try(() =>
    {
        var path = process.MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var identity = FileVersionInfo.GetVersionInfo(path);
        return HasFlintIdentity(identity.ProductName, identity.CompanyName);
    });

    private static bool Try(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static T Try<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (InvalidOperationException)
        {
            return default!;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return default!;
        }
        catch (NotSupportedException)
        {
            return default!;
        }
    }
}
