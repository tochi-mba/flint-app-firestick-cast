using Microsoft.Win32;
using Shouldly;

namespace Flint.Platform.Windows.Tests;

/// <summary>Starting Flint at sign-in, through the person's own Run key.</summary>
public sealed class RunAtSignInTests
{
    private const string Installed = @"C:\Users\Ada Lovelace\AppData\Local\Flint\Flint.exe";

    private readonly FakeRunKey key = new();
    private readonly HashSet<string> files = new(StringComparer.OrdinalIgnoreCase) { Installed };

    [Fact]
    public void Enabling_StoresTheFileQuoted_AndInTheTrayAddsMinimized()
    {
        var signIn = new RunAtSignIn(key, Installed, files.Contains);
        signIn.Read().ShouldBe(SignInStart.Off);

        signIn.Enable(inTray: false);
        key.Values[RunAtSignIn.EntryName].ShouldBe("\"C:\\Users\\Ada Lovelace\\AppData\\Local\\Flint\\Flint.exe\"");
        signIn.Read().ShouldBe(SignInStart.On);

        signIn.Enable(inTray: true);
        key.Values[RunAtSignIn.EntryName].ShouldBe("\"C:\\Users\\Ada Lovelace\\AppData\\Local\\Flint\\Flint.exe\" --minimized");
        signIn.Read().ShouldBe(SignInStart.InTray);

        signIn.Disable();
        key.Values.ShouldNotContainKey(RunAtSignIn.EntryName);
        signIn.Read().ShouldBe(SignInStart.Off);
        signIn.Executable.ShouldBe(Installed);
    }

    [Theory]
    [InlineData("\"D:\\Moved away\\Flint.exe\"")]
    [InlineData("\"\"")]
    [InlineData("D:\\Old\\Flint.exe --minimized")]
    public void AnEntryForACopyThatIsGone_IsStale(string stored)
    {
        key.Values[RunAtSignIn.EntryName] = stored;

        new RunAtSignIn(key, Installed, files.Contains).Read().ShouldBe(SignInStart.Stale);
    }

    [Theory]
    [InlineData("\"C:\\A B\\Flint.exe\" --minimized", "C:\\A B\\Flint.exe")]
    [InlineData("  \"C:\\A B\\Flint.exe\"  ", "C:\\A B\\Flint.exe")]
    [InlineData("C:\\Flint\\Flint.exe --minimized", "C:\\Flint\\Flint.exe")]
    [InlineData("C:\\Flint\\Flint.exe", "C:\\Flint\\Flint.exe")]
    [InlineData("\"C:\\unterminated", null)]
    public void TheFileAStoredCommandStarts_IsRead(string command, string? file)
    {
        RunAtSignIn.ExecutableOf(command).ShouldBe(file);
    }

    [Fact]
    public void AnInstalledCopy_StartsThroughItsStableLauncher_AndAPortableOneThroughItself()
    {
        var running = @"C:\Users\Ada\AppData\Local\Flint\current\Flint.exe";
        var launcher = @"C:\Users\Ada\AppData\Local\Flint\Flint.exe";

        RunAtSignIn.LauncherFor(running, path => path == launcher).ShouldBe(launcher);
        RunAtSignIn.LauncherFor(running, _ => false).ShouldBe(running, "no launcher beside it, so not an installed copy");
        RunAtSignIn.LauncherFor(@"D:\Tools\Flint\Flint.exe", _ => true).ShouldBe(@"D:\Tools\Flint\Flint.exe");
        RunAtSignIn.LauncherFor("Flint.exe", _ => true).ShouldBe("Flint.exe");
        RunAtSignIn.ForThisCopy().ShouldNotBeNull().Executable.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ACopyWhoseFileWindowsCannotName_HasNoEntry_RatherThanFailingToOpen(string? processPath) =>
        RunAtSignIn.ForProcess(processPath).ShouldBeNull();

    [Fact]
    public void ACopyWithAFile_HasAnEntryThatStartsIt() =>
        RunAtSignIn.ForProcess(@"D:\Tools\Flint\Flint.exe").ShouldNotBeNull().Executable.ShouldBe(@"D:\Tools\Flint\Flint.exe");

    [Fact]
    public void WhatItNeeds_IsRequired()
    {
        Should.Throw<ArgumentNullException>(() => new RunAtSignIn(null!, Installed, files.Contains));
        Should.Throw<ArgumentNullException>(() => new RunAtSignIn(key, Installed, null!));
        Should.Throw<ArgumentException>(() => new RunAtSignIn(key, " ", files.Contains));
    }

    [Fact]
    public void TheRealRegistry_IsWrittenReadAndCleared_UnderAKeyOfItsOwn()
    {
        var path = @"Software\REX Technologies\Flint tests\Run " + Guid.NewGuid().ToString("N");
        var real = new RegistryRunKey(path);
        try
        {
            real.Read("Flint").ShouldBeNull("the key does not exist yet");
            real.Delete("Flint");

            real.Write("Flint", "\"C:\\Flint.exe\"");
            real.Read("Flint").ShouldBe("\"C:\\Flint.exe\"");
            real.Delete("Flint");
            real.Read("Flint").ShouldBeNull();
            real.Delete("Flint");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }

        new RegistryRunKey().Read("an entry nobody has").ShouldBeNull();
    }

    private sealed class FakeRunKey : IRunKey
    {
        public Dictionary<string, string> Values { get; } = [];

        public string? Read(string name) => Values.GetValueOrDefault(name);

        public void Write(string name, string command) => Values[name] = command;

        public void Delete(string name) => Values.Remove(name);
    }
}
