using System.Runtime.InteropServices;

namespace Flint.Platform.Windows;

/// <summary>
/// Puts the installed folder on the user's <c>PATH</c>, and takes it off again.
/// </summary>
/// <remarks>
/// <para>
/// This is what makes <c>flint</c> work in a terminal that was never opened from the install folder.
/// The user's PATH is edited, never the machine's: the install is per-user and needs no
/// administrator, and a per-user program has no business in a setting that changes every account.
/// </para>
/// <para>
/// Only the exact directory Flint added is removed on uninstall. A PATH is shared with everything
/// else the person has installed, and an uninstaller that rewrites more of it than it contributed is
/// the kind of thing people never find out about until something else stops working.
/// </para>
/// </remarks>
public static partial class PathRegistration
{
    /// <summary>Adds <paramref name="directory"/> if it is not already listed.</summary>
    /// <returns><see langword="true"/> when the stored PATH was changed.</returns>
    public static bool Add(IUserPathStore store, string directory)
    {
        ArgumentNullException.ThrowIfNull(store);
        var entry = Normalize(directory);
        if (entry.Length == 0)
        {
            return false;
        }

        var current = store.Read();
        if (Contains(current, entry))
        {
            return false;
        }

        // Appended, not prepended. Flint's own command is not more important than whatever the
        // person has already put first, and a program that jumps the queue on PATH can shadow a
        // tool of the same name that they chose deliberately.
        var updated = current.Length == 0 ? entry : $"{current.TrimEnd(';')};{entry}";
        store.Write(updated);
        return true;
    }

    /// <summary>Removes every listing of <paramref name="directory"/>.</summary>
    /// <returns><see langword="true"/> when the stored PATH was changed.</returns>
    public static bool Remove(IUserPathStore store, string directory)
    {
        ArgumentNullException.ThrowIfNull(store);
        var entry = Normalize(directory);
        var current = store.Read();
        if (entry.Length == 0 || current.Length == 0)
        {
            return false;
        }

        var kept = current
            .Split(';')
            .Where(part => !SameDirectory(part, entry))
            .ToArray();
        if (kept.Length == current.Split(';').Length)
        {
            return false;
        }

        // Empty entries in the original are kept as they were found: an empty entry means "the
        // current directory" to some programs, and dropping it is a change nobody asked for.
        store.Write(string.Join(';', kept));
        return true;
    }

    /// <summary>Whether <paramref name="path"/> already lists <paramref name="directory"/>.</summary>
    public static bool Contains(string path, string directory)
    {
        var entry = Normalize(directory);
        return entry.Length != 0
            && (path ?? string.Empty).Split(';').Any(part => SameDirectory(part, entry));
    }

    /// <summary>
    /// Tells the desktop that the environment changed, so a new terminal sees the edit.
    /// </summary>
    /// <remarks>
    /// Without this, the registry holds the new PATH but every process started from Explorer keeps
    /// the copy it inherited until the next sign-in — which reads exactly like the installer having
    /// done nothing at all.
    /// </remarks>
    public static void AnnounceChange()
    {
        const int HwndBroadcast = 0xffff;
        const int WmSettingChange = 0x001a;
        const int SmtoAbortIfHung = 0x0002;

        // Best effort by design: the PATH is already stored, and a desktop that does not answer in
        // time is a reason to open a new terminal, not a reason to fail an install.
        _ = SendMessageTimeout(
            new IntPtr(HwndBroadcast),
            WmSettingChange,
            IntPtr.Zero,
            "Environment",
            SmtoAbortIfHung,
            1000,
            out _);
    }

    private static bool SameDirectory(string part, string entry) =>
        string.Equals(Normalize(part), entry, StringComparison.OrdinalIgnoreCase);

    /// <summary>Trims the spacing, quoting and trailing separators a hand-edited PATH collects.</summary>
    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().Trim('"').TrimEnd('\\', '/');

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr SendMessageTimeout(
        IntPtr windowHandle,
        int message,
        IntPtr wordParameter,
        string longParameter,
        int flags,
        int timeoutMilliseconds,
        out IntPtr result);
}
