using Flint.Platform.Windows;

namespace Flint.Cli;

/// <summary>`flint update`: fetch a newer Flint and install it.</summary>
/// <remarks>
/// The same client the Settings page uses, so the command line and the window can never disagree
/// about which build is current or where updates come from.
/// </remarks>
internal static class UpdateRunner
{
    internal static async Task<int> RunAsync(IUpdateSource source, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);

        if (!source.IsInstalled)
        {
            output.WriteLine();
            output.WriteLine("  This is the portable build, so it does not update itself.");
            output.WriteLine("  Download a newer zip, or install Flint with the installer to get updates.");
            output.WriteLine();
            return FlintExitCode.Success;
        }

        output.WriteLine();
        output.WriteLine("  Looking for a newer build.");
        var version = await source.CheckForNewVersionAsync(cancellationToken).ConfigureAwait(false);
        if (version is null)
        {
            output.WriteLine("  Flint is up to date.");
            output.WriteLine();
            return FlintExitCode.Success;
        }

        output.WriteLine($"  Downloading version {version}.");
        await source.DownloadAsync(null, cancellationToken).ConfigureAwait(false);

        // Applying restarts the process, so nothing after this line runs. Said before it happens,
        // because a command that vanishes mid-sentence looks like a crash.
        output.WriteLine($"  Version {version} is ready. Flint will restart into it now.");
        output.WriteLine();
        source.ApplyAndRestart();
        return FlintExitCode.Success;
    }
}
