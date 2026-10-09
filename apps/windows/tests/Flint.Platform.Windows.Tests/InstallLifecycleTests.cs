using Shouldly;

namespace Flint.Platform.Windows.Tests;

/// <summary>What each installer hook does, in the order it has to happen.</summary>
public sealed class InstallLifecycleTests
{
    private const string Folder = @"C:\Users\someone\AppData\Local\Flint\current";

    [Fact]
    public void AfterInstall_StopsOtherCopiesThenPutsFlintOnThePath()
    {
        var steps = new List<string>();
        var store = new FakePathStore(@"C:\Tools");
        var lifecycle = Lifecycle(store, steps);

        lifecycle.AfterInstall();

        steps.ShouldBe(["stop", "announce"]);
        store.Value.ShouldBe($@"C:\Tools;{Folder}");
    }

    [Fact]
    public void AfterInstall_WhenAlreadyOnThePath_DoesNotAnnounceAChange()
    {
        var steps = new List<string>();
        var lifecycle = Lifecycle(new FakePathStore(Folder), steps);

        lifecycle.AfterInstall();

        steps.ShouldBe(["stop"]);
    }

    [Fact]
    public void AfterUpdate_OnlyStopsOtherCopies()
    {
        var steps = new List<string>();
        var store = new FakePathStore(Folder);
        var lifecycle = Lifecycle(store, steps);

        lifecycle.AfterUpdate();

        steps.ShouldBe(["stop"]);
        store.Writes.ShouldBe(0, "an update keeps the same folder, so the PATH is left alone");
    }

    [Fact]
    public void BeforeUninstall_StopsOtherCopiesThenRemovesOnlyFlintsEntry()
    {
        var steps = new List<string>();
        var store = new FakePathStore($@"C:\Tools;{Folder};C:\Other");
        var lifecycle = Lifecycle(store, steps);

        lifecycle.BeforeUninstall();

        steps.ShouldBe(["stop", "announce"]);
        store.Value.ShouldBe(@"C:\Tools;C:\Other");
    }

    [Fact]
    public void BeforeUninstall_WhenNotOnThePath_DoesNotAnnounceAChange()
    {
        var steps = new List<string>();
        var lifecycle = Lifecycle(new FakePathStore(@"C:\Tools"), steps);

        lifecycle.BeforeUninstall();

        steps.ShouldBe(["stop"]);
    }

    [Theory]
    [InlineData(@"""C:\Users\someone\AppData\Local\Flint\Flint.exe"" --minimized", false)]
    [InlineData(@"""D:\Portable\Flint\Flint.exe""", true)]
    [InlineData(@"""C:\Users\someone\AppData\Local\FlintOther\Flint.exe""", true)]
    public void BeforeUninstall_RemovesTheSignInEntry_OnlyWhenItStartsThisInstall(string command, bool kept)
    {
        var runKey = new InMemoryRunKey();
        runKey.Write(RunAtSignIn.EntryName, command);

        Lifecycle(new FakePathStore(@"C:\Tools"), [], runKey).BeforeUninstall();

        (runKey.Read(RunAtSignIn.EntryName) is not null).ShouldBe(kept);
    }

    [Fact]
    public void BeforeUninstall_WithNoSignInEntry_LeavesTheListAlone()
    {
        var runKey = new InMemoryRunKey();
        Lifecycle(new FakePathStore(Folder), [], runKey).BeforeUninstall();

        runKey.Read(RunAtSignIn.EntryName).ShouldBeNull();
    }

    [Fact]
    public void ForThisInstall_IsTheRunningBuildsFolder()
    {
        // Built, never invoked: invoking it would sweep the real processes and edit the real PATH.
        InstallLifecycle.ForThisInstall().ShouldNotBeNull();
    }

    [Fact]
    public void EveryDependencyIsRequired()
    {
        var store = new FakePathStore(string.Empty);
        Should.Throw<ArgumentNullException>(() => new InstallLifecycle(null!, Folder, () => { }, () => { }, new InMemoryRunKey()));
        Should.Throw<ArgumentException>(() => new InstallLifecycle(store, " ", () => { }, () => { }, new InMemoryRunKey()));
        Should.Throw<ArgumentNullException>(() => new InstallLifecycle(store, Folder, null!, () => { }, new InMemoryRunKey()));
        Should.Throw<ArgumentNullException>(() => new InstallLifecycle(store, Folder, () => { }, null!, new InMemoryRunKey()));
        Should.Throw<ArgumentNullException>(() => new InstallLifecycle(store, Folder, () => { }, () => { }, null!));
    }

    private static InstallLifecycle Lifecycle(FakePathStore store, List<string> steps, IRunKey? runKey = null) =>
        new(store, Folder, () => steps.Add("stop"), () => steps.Add("announce"), runKey ?? new InMemoryRunKey());

    private sealed class FakePathStore(string value) : IUserPathStore
    {
        public string Value { get; private set; } = value;

        public int Writes { get; private set; }

        public string Read() => Value;

        public void Write(string value)
        {
            Writes++;
            Value = value;
        }
    }
}
