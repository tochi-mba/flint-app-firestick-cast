using System.ComponentModel;
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
    /// <summary>How long a shell asked to close is given before it is ended.</summary>
    public static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(4);

    /// <summary>The executable name every Flint shell runs as, installed or extracted.</summary>
    public const string ProcessName = "Flint.App";

    /// <summary>Closes other verified Flint shells, then terminates any that ignored the close.</summary>
    public static void StopOtherCopies() => StopOtherCopies(ProcessName);

    /// <summary>
    /// The sweep over processes named <paramref name="processName"/>. A parameter so tests can run
    /// the real sweep over a harmless process rather than over the Flint the developer is using.
    /// </summary>
    internal static void StopOtherCopies(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            StopOtherCopies(
                [.. processes.Select(static process => new RunningProcess(process))],
                Environment.ProcessId,
                GracePeriod);
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    /// <summary>
    /// Asks every other Flint in <paramref name="candidates"/> to close, then ends any still running
    /// once <paramref name="grace"/> has passed.
    /// </summary>
    /// <remarks>
    /// Every close is sent before any wait starts, and the grace period is one budget for all of
    /// them rather than one each: three stuck copies must not stretch the installer's hook past the
    /// time Velopack allows it.
    /// </remarks>
    internal static void StopOtherCopies(IReadOnlyList<IRunningProcess> candidates, int currentProcessId, TimeSpan grace)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var copies = candidates
            .Where(process => process.Id != currentProcessId && process.IsFlint)
            .ToArray();
        foreach (var process in copies)
        {
            process.RequestClose();
        }

        var deadline = Stopwatch.StartNew();
        foreach (var process in copies)
        {
            var remaining = grace - deadline.Elapsed;
            if (remaining > TimeSpan.Zero && process.WaitForExit(remaining))
            {
                continue;
            }

            process.Kill();
        }
    }

    internal static bool HasFlintIdentity(string? productName, string? companyName) =>
        string.Equals(productName, "Flint", StringComparison.Ordinal)
        && string.Equals(companyName, "REX Technologies", StringComparison.Ordinal);

    /// <summary>One running process, as the cleanup needs to see it.</summary>
    internal interface IRunningProcess
    {
        int Id { get; }

        /// <summary>Whether its executable carries Flint's product and publisher metadata.</summary>
        bool IsFlint { get; }

        /// <summary>Asks it to close its window, the way a person would.</summary>
        void RequestClose();

        /// <summary>Waits for it to exit; false when it is still running when the wait ends.</summary>
        bool WaitForExit(TimeSpan timeout);

        /// <summary>Ends it and anything it started.</summary>
        void Kill();
    }

    /// <summary>
    /// A real process. Every call tolerates the process exiting or refusing access mid-sweep,
    /// because another copy closing on its own while the installer runs is the ordinary case.
    /// </summary>
    internal sealed class RunningProcess(Process process) : IRunningProcess
    {
        public int Id => process.Id;

        public bool IsFlint => Try(() =>
        {
            var path = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var identity = FileVersionInfo.GetVersionInfo(path);
            return HasFlintIdentity(identity.ProductName, identity.CompanyName);
        });

        public void RequestClose() => Try(process.CloseMainWindow);

        public bool WaitForExit(TimeSpan timeout) => Try(() => process.WaitForExit(timeout));

        public void Kill() => Try(() =>
        {
            process.Kill(entireProcessTree: true);
            return true;
        });

        /// <summary>
        /// The two ways a process call fails when the process is not ours to ask about: it has
        /// already exited (<see cref="InvalidOperationException"/>) or access is denied
        /// (<see cref="Win32Exception"/>). Either means "not a copy this sweep can act on".
        /// </summary>
        private static bool Try(Func<bool> action)
        {
            try
            {
                return action();
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (Win32Exception)
            {
                return false;
            }
        }
    }
}
