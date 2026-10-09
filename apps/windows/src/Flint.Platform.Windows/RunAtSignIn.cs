using Microsoft.Win32;

namespace Flint.Platform.Windows;

/// <summary>The per-user list of programs Windows starts at sign-in, read and written as stored.</summary>
/// <remarks>
/// An interface because the registry is the one part of <see cref="RunAtSignIn"/> that cannot be
/// exercised in a test without writing to the machine running it.
/// </remarks>
public interface IRunKey
{
    /// <summary>The command stored under <paramref name="name"/>, or null when there is none.</summary>
    string? Read(string name);

    /// <summary>Stores <paramref name="command"/> under <paramref name="name"/>.</summary>
    void Write(string name, string command);

    /// <summary>Removes <paramref name="name"/>, if it is there.</summary>
    void Delete(string name);
}

/// <summary>The real list, under <c>HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run</c>.</summary>
public sealed class RegistryRunKey : IRunKey
{
    private readonly string keyPath;

    /// <summary>Creates the list over the user's own Run key.</summary>
    public RegistryRunKey()
        : this(@"Software\Microsoft\Windows\CurrentVersion\Run")
    {
    }

    /// <summary>Creates the list over a key beneath <c>HKEY_CURRENT_USER</c>, for tests.</summary>
    /// <param name="keyPath">The key, relative to <c>HKEY_CURRENT_USER</c>.</param>
    public RegistryRunKey(string keyPath) => this.keyPath = keyPath;

    /// <inheritdoc />
    public string? Read(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(name) as string;
    }

    /// <inheritdoc />
    public void Write(string name, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        key.SetValue(name, command, RegistryValueKind.String);
    }

    /// <inheritdoc />
    public void Delete(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}

/// <summary>A sign-in list kept only in memory, for tests and design-time shells.</summary>
public sealed class InMemoryRunKey : IRunKey
{
    private readonly Dictionary<string, string> entries = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public string? Read(string name) => entries.GetValueOrDefault(name);

    /// <inheritdoc />
    public void Write(string name, string command) => entries[name] = command;

    /// <inheritdoc />
    public void Delete(string name) => entries.Remove(name);
}

/// <summary>Whether Flint starts when the person signs in to Windows.</summary>
public enum SignInStart
{
    /// <summary>It does not.</summary>
    Off = 0,

    /// <summary>It starts, with its window open.</summary>
    On = 1,

    /// <summary>It starts in the tray.</summary>
    InTray = 2,

    /// <summary>An entry is there but names a copy of Flint that no longer exists, as when a portable folder moved.</summary>
    Stale = 3,
}

/// <summary>Starting Flint when the person signs in to Windows, through their own Run key.</summary>
/// <remarks>
/// The entry names the stable launcher of an installed copy, which survives updates, and the
/// running file of a portable one. Uninstalling removes it, as the repository's rule asks of
/// anything an install leaves behind.
/// </remarks>
/// <param name="key">The Run key.</param>
/// <param name="executable">The file Windows should start.</param>
/// <param name="fileExists">Whether a file exists, for telling a moved copy from a present one.</param>
public sealed class RunAtSignIn(IRunKey key, string executable, Func<string, bool> fileExists)
{
    /// <summary>The name Flint's entry is stored under.</summary>
    public const string EntryName = "REX Technologies Flint";

    /// <summary>The argument that starts Flint in the tray.</summary>
    public const string MinimizedArgument = "--minimized";

    private readonly IRunKey key = key ?? throw new ArgumentNullException(nameof(key));
    private readonly Func<string, bool> fileExists = fileExists ?? throw new ArgumentNullException(nameof(fileExists));

    /// <summary>The file Windows starts, shown in Settings so the person can see exactly what runs.</summary>
    public string Executable { get; } = string.IsNullOrWhiteSpace(executable)
        ? throw new ArgumentException("The file to start is required.", nameof(executable))
        : executable;

    /// <summary>Flint's entry for this copy of Flint, or null when Windows cannot say which file is running.</summary>
    public static RunAtSignIn? ForThisCopy() => ForProcess(Environment.ProcessPath);

    /// <summary>The entry for the copy running from <paramref name="processPath"/>, or null when there is no path.</summary>
    /// <remarks>Null rather than an exception: starting with Windows is not worth Flint failing to open.</remarks>
    internal static RunAtSignIn? ForProcess(string? processPath) =>
        string.IsNullOrWhiteSpace(processPath)
            ? null
            : new(new RegistryRunKey(), LauncherFor(processPath, File.Exists), File.Exists);

    /// <summary>Whether, and how, Flint starts at sign-in now.</summary>
    public SignInStart Read()
    {
        if (key.Read(EntryName) is not { Length: > 0 } command)
        {
            return SignInStart.Off;
        }

        if (ExecutableOf(command) is not { } named || !fileExists(named))
        {
            return SignInStart.Stale;
        }

        return command.EndsWith(" " + MinimizedArgument, StringComparison.Ordinal) ? SignInStart.InTray : SignInStart.On;
    }

    /// <summary>Starts this copy of Flint at sign-in, in the tray when <paramref name="inTray"/> is set.</summary>
    public void Enable(bool inTray) => key.Write(EntryName, CommandFor(Executable, inTray));

    /// <summary>Stops Flint starting at sign-in.</summary>
    public void Disable() => key.Delete(EntryName);

    /// <summary>The command Windows runs: the file quoted, so a path with spaces stays one path.</summary>
    internal static string CommandFor(string executable, bool inTray) =>
        inTray ? $"\"{executable}\" {MinimizedArgument}" : $"\"{executable}\"";

    /// <summary>The file a stored command starts, or null when it does not read as one.</summary>
    internal static string? ExecutableOf(string command)
    {
        var trimmed = command.Trim();
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            return end > 1 ? trimmed[1..end] : null;
        }

        var space = trimmed.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? trimmed : trimmed[..space];
    }

    /// <summary>
    /// The file to start: an installed copy's stable launcher, one folder above the version that is
    /// running, or the running file itself for a portable copy.
    /// </summary>
    internal static string LauncherFor(string processPath, Func<string, bool> fileExists)
    {
        var folder = Path.GetDirectoryName(processPath);
        if (folder is not null
            && string.Equals(Path.GetFileName(folder), "current", StringComparison.OrdinalIgnoreCase)
            && Path.GetDirectoryName(folder) is { } root)
        {
            var launcher = Path.Combine(root, Path.GetFileName(processPath));
            if (fileExists(launcher))
            {
                return launcher;
            }
        }

        return processPath;
    }
}
