using Flint.App.Services;
using Flint.Core;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>What Flint remembers about this PC's sound outputs, kept in a file.</summary>
public sealed class FileSoundMemoryTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "flint-sound-memory-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void NothingWritten_RemembersNothing()
    {
        var memory = new FileSoundMemory(folder);

        memory.PendingRestore.ShouldBeNull();
        memory.MuteSilences("usb").ShouldBeFalse();
        File.Exists(Path.Combine(folder, FileSoundMemory.FileName)).ShouldBeFalse("reading writes nothing");
    }

    [Fact]
    public void WhatIsWritten_IsReadByTheNextLaunch()
    {
        var first = new FileSoundMemory(folder);
        first.SetPendingRestore(new MuteToRestore("speakers", true));
        first.RememberMuteSilences("usb");
        first.RememberMuteSilences("USB");

        var next = new FileSoundMemory(folder);

        next.PendingRestore.ShouldBe(new MuteToRestore("speakers", true));
        next.MuteSilences("Usb").ShouldBeTrue("identities are matched without regard to case");
        next.MuteSilences("speakers").ShouldBeFalse();
        next.SetPendingRestore(null);
        new FileSoundMemory(folder).PendingRestore.ShouldBeNull();
        new FileSoundMemory(folder).MuteSilences("usb").ShouldBeTrue("clearing the mute keeps what was learned");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"pendingRestore":{"deviceId":"","wasMuted":true},"muteSilences":["", " ", "usb"]}""")]
    [InlineData("null")]
    [InlineData("""{"pendingRestore":{"wasMuted":true},"muteSilences":null}""")]
    public void AFileThatIsDamaged_IsReadForWhatIsSound(string text)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, FileSoundMemory.FileName), text);

        var memory = new FileSoundMemory(folder);

        memory.PendingRestore.ShouldBeNull();
        memory.MuteSilences(" ").ShouldBeFalse();
    }

    [Fact]
    public void AFolderThatCannotBeWritten_KeepsWhatWasLearnedForThisRun()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(folder)!);
        File.WriteAllText(folder, "a file where the folder should be");
        try
        {
            var memory = new FileSoundMemory(folder);

            memory.SetPendingRestore(new MuteToRestore("speakers", false));
            memory.RememberMuteSilences("usb");

            memory.PendingRestore.ShouldBe(new MuteToRestore("speakers", false));
            memory.MuteSilences("usb").ShouldBeTrue();
        }
        finally
        {
            File.Delete(folder);
        }
    }

    [Fact]
    public void ANamelessFolderOrOutput_IsRefused()
    {
        Should.Throw<ArgumentException>(() => new FileSoundMemory(" "));
        Should.Throw<ArgumentException>(() => new FileSoundMemory(folder).RememberMuteSilences(""));
        new FileSoundMemory().PendingRestore.ShouldBeNull("the real folder holds no mute on a test machine");
    }
}
