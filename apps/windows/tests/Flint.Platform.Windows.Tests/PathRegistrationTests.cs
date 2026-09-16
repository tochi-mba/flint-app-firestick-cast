using Shouldly;

namespace Flint.Platform.Windows.Tests;

/// <summary>What the installer does to the PATH, and what it must leave alone.</summary>
public sealed class PathRegistrationTests
{
    private const string InstallFolder = @"C:\Users\someone\AppData\Local\Flint\current";

    [Fact]
    public void AddsTheFolderToTheEndOfAnExistingPath()
    {
        var store = new FakePathStore(@"C:\tools;%USERPROFILE%\.cargo\bin");

        PathRegistration.Add(store, InstallFolder).ShouldBeTrue();

        store.Value.ShouldBe($@"C:\tools;%USERPROFILE%\.cargo\bin;{InstallFolder}");
    }

    [Fact]
    public void WritesNothingWhenTheFolderIsAlreadyListed()
    {
        var store = new FakePathStore($@"C:\tools;{InstallFolder}");

        PathRegistration.Add(store, InstallFolder).ShouldBeFalse();

        store.Writes.ShouldBe(0);
    }

    [Theory]
    [InlineData(@"c:\users\someone\appdata\local\flint\current")]
    [InlineData(@"C:\Users\someone\AppData\Local\Flint\current\")]
    [InlineData(@"""C:\Users\someone\AppData\Local\Flint\current""")]
    public void RecognisesTheFolderWhateverItsCasingQuotingOrTrailingSlash(string listed)
    {
        var store = new FakePathStore($@"C:\tools;{listed}");

        PathRegistration.Add(store, InstallFolder).ShouldBeFalse();

        PathRegistration.Contains(store.Value, InstallFolder).ShouldBeTrue();
    }

    [Fact]
    public void AddsToAnEmptyPathWithoutLeavingASeparator()
    {
        var store = new FakePathStore(string.Empty);

        PathRegistration.Add(store, InstallFolder).ShouldBeTrue();

        store.Value.ShouldBe(InstallFolder);
    }

    [Fact]
    public void RemovesOnlyItsOwnEntry()
    {
        var store = new FakePathStore($@"C:\tools;{InstallFolder};%USERPROFILE%\.dotnet\tools");

        PathRegistration.Remove(store, InstallFolder).ShouldBeTrue();

        store.Value.ShouldBe(@"C:\tools;%USERPROFILE%\.dotnet\tools");
    }

    [Fact]
    public void LeavesAPathThatNeverHadTheEntryUntouched()
    {
        var store = new FakePathStore(@"C:\tools;%USERPROFILE%\.dotnet\tools");

        PathRegistration.Remove(store, InstallFolder).ShouldBeFalse();

        store.Writes.ShouldBe(0);
    }

    [Fact]
    public void RemovesEveryCopyWhenAnInstallWasRepaired()
    {
        var store = new FakePathStore($@"{InstallFolder};C:\tools;{InstallFolder}\");

        PathRegistration.Remove(store, InstallFolder).ShouldBeTrue();

        store.Value.ShouldBe(@"C:\tools");
    }

    private sealed class FakePathStore(string value) : IUserPathStore
    {
        public string Value { get; private set; } = value;

        public int Writes { get; private set; }

        public string Read() => Value;

        public void Write(string updated)
        {
            Value = updated;
            Writes++;
        }
    }
}
