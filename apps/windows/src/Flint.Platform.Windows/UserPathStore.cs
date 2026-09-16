using Microsoft.Win32;

namespace Flint.Platform.Windows;

/// <summary>The user's <c>PATH</c>, read and written as stored.</summary>
/// <remarks>
/// An interface because the registry is the one part of <see cref="PathRegistration"/> that cannot be
/// exercised in a test without writing to the machine running it.
/// </remarks>
public interface IUserPathStore
{
    /// <summary>The raw value, or an empty string when the user has no PATH of their own.</summary>
    string Read();

    /// <summary>Replaces the value. An empty string removes the entry entirely.</summary>
    void Write(string value);
}

/// <summary>The real user PATH, under <c>HKEY_CURRENT_USER\Environment</c>.</summary>
public sealed class RegistryUserPathStore : IUserPathStore
{
    private const string ValueName = "Path";

    private readonly string keyPath;

    /// <summary>Creates a store over the user's environment key.</summary>
    public RegistryUserPathStore()
        : this("Environment")
    {
    }

    /// <summary>Creates a store over a key beneath <c>HKEY_CURRENT_USER</c>, for tests.</summary>
    /// <param name="keyPath">The key, relative to <c>HKEY_CURRENT_USER</c>.</param>
    public RegistryUserPathStore(string keyPath) => this.keyPath = keyPath;

    /// <inheritdoc />
    public string Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        // Never expanded. A user PATH commonly holds %USERPROFILE%, and writing back an expanded
        // copy would freeze it to this account's current home directory.
        return key?.GetValue(ValueName, string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames) as string
            ?? string.Empty;
    }

    /// <inheritdoc />
    public void Write(string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        if (value.Length == 0)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        // The kind is preserved rather than chosen: rewriting an expandable PATH as a plain string
        // turns every %VARIABLE% in it into a literal directory name that does not exist. GetValueKind
        // throws when there is no value yet, so the existing kind is only consulted when there is one.
        var expandable = value.Contains('%', StringComparison.Ordinal)
            || (key.GetValue(ValueName) is not null && key.GetValueKind(ValueName) is RegistryValueKind.ExpandString);
        key.SetValue(ValueName, value, expandable ? RegistryValueKind.ExpandString : RegistryValueKind.String);
    }
}
