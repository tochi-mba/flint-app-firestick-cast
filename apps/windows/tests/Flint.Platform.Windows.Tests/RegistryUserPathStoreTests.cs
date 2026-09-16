using Microsoft.Win32;
using Shouldly;

namespace Flint.Platform.Windows.Tests;

/// <summary>
/// The registry half, against a scratch key rather than the real user environment.
/// </summary>
/// <remarks>
/// A test that edited <c>HKCU\Environment</c> would change the PATH of whoever ran it, which is not
/// a thing a test suite may do. The store takes the key it works on so this one can point it at a
/// key of its own and delete it afterwards.
/// </remarks>
public sealed class RegistryUserPathStoreTests : IDisposable
{
    private readonly string keyPath =
        $@"Software\REX Technologies\Flint\tests\{Guid.NewGuid():N}";

    [Fact]
    public void ReadsAnEmptyStringWhenTheUserHasNoPathOfTheirOwn()
    {
        new RegistryUserPathStore(keyPath).Read().ShouldBe(string.Empty);
    }

    [Fact]
    public void RoundTripsAValue()
    {
        var store = new RegistryUserPathStore(keyPath);

        store.Write(@"C:\tools");

        store.Read().ShouldBe(@"C:\tools");
    }

    [Fact]
    public void KeepsAnUnexpandedVariableUnexpanded()
    {
        var store = new RegistryUserPathStore(keyPath);

        store.Write(@"%USERPROFILE%\.dotnet\tools;C:\tools");

        // Read back as stored, and stored as expandable, so the entry still follows the account it
        // belongs to rather than being frozen to this machine's home directory.
        store.Read().ShouldBe(@"%USERPROFILE%\.dotnet\tools;C:\tools");
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        key!.GetValueKind("Path").ShouldBe(RegistryValueKind.ExpandString);
    }

    [Fact]
    public void RemovesTheValueEntirelyWhenNothingIsLeftOnThePath()
    {
        var store = new RegistryUserPathStore(keyPath);
        store.Write(@"C:\tools");

        store.Write(string.Empty);

        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        key?.GetValue("Path").ShouldBeNull();
    }

    public void Dispose() =>
        Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
}
