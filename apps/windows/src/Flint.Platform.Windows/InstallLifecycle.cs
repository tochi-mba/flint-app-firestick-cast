namespace Flint.Platform.Windows;

/// <summary>What the installer asks this build to do when it is installed, updated or removed.</summary>
/// <remarks>
/// <para>
/// Velopack runs the new executable with a hook argument and expects it to do this one job and
/// exit. The work lives here rather than in <c>Program.Main</c> so it can be tested with a fake
/// PATH and a fake process sweep; <c>Main</c> only wires these methods to Velopack's callbacks.
/// </para>
/// <para>
/// Every hook stops other running copies first. Velopack already stops the processes under the
/// folder it replaces, but a copy extracted from a zip elsewhere would keep running beside the new
/// build, sharing the same television, logs and profile store.
/// </para>
/// </remarks>
/// <param name="pathStore">The user's PATH.</param>
/// <param name="installDirectory">The folder the installer put this build in.</param>
/// <param name="stopOtherCopies">Closes every other running Flint.</param>
/// <param name="announcePathChange">Tells the desktop the PATH changed, so a new terminal sees it.</param>
/// <param name="runKey">The user's list of programs started at sign-in.</param>
public sealed class InstallLifecycle(
    IUserPathStore pathStore,
    string installDirectory,
    Action stopOtherCopies,
    Action announcePathChange,
    IRunKey runKey)
{
    private readonly IUserPathStore _pathStore = pathStore ?? throw new ArgumentNullException(nameof(pathStore));
    private readonly string _installDirectory = string.IsNullOrWhiteSpace(installDirectory)
        ? throw new ArgumentException("The install directory is required.", nameof(installDirectory))
        : installDirectory;
    private readonly Action _stopOtherCopies = stopOtherCopies ?? throw new ArgumentNullException(nameof(stopOtherCopies));
    private readonly Action _announcePathChange = announcePathChange ?? throw new ArgumentNullException(nameof(announcePathChange));
    private readonly IRunKey _runKey = runKey ?? throw new ArgumentNullException(nameof(runKey));

    /// <summary>The lifecycle of the build that is running right now, against the real PATH and processes.</summary>
    public static InstallLifecycle ForThisInstall() => new(
        new RegistryUserPathStore(),
        AppContext.BaseDirectory,
        FlintProcessCleanup.StopOtherCopies,
        PathRegistration.AnnounceChange,
        new RegistryRunKey());

    /// <summary>A fresh install: one Flint running, and <c>flint</c> on the PATH.</summary>
    public void AfterInstall()
    {
        _stopOtherCopies();
        if (PathRegistration.Add(_pathStore, _installDirectory))
        {
            _announcePathChange();
        }
    }

    /// <summary>An update in place: the folder is the same, so the PATH already names it.</summary>
    public void AfterUpdate() => _stopOtherCopies();

    /// <summary>A removal: nothing left running, and only Flint's own PATH and sign-in entries taken away.</summary>
    public void BeforeUninstall()
    {
        _stopOtherCopies();
        if (PathRegistration.Remove(_pathStore, _installDirectory))
        {
            _announcePathChange();
        }

        RemoveSignInEntry();
    }

    /// <summary>
    /// Removes the entry that starts Flint at sign-in when it starts this install. One that starts a
    /// portable copy kept elsewhere is that copy's, and stays.
    /// </summary>
    private void RemoveSignInEntry()
    {
        var installRoot = Path.GetFullPath(Path.Combine(_installDirectory, ".."));
        if (_runKey.Read(RunAtSignIn.EntryName) is { } command
            && RunAtSignIn.ExecutableOf(command) is { } file
            && file.StartsWith(installRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            _runKey.Delete(RunAtSignIn.EntryName);
        }
    }
}
