using Flint.App.Services;
using Flint.Core.Media;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>Where played files' positions are kept, and how the queue reads the disk.</summary>
public sealed class MediaStorageTests : IDisposable
{
    private static readonly DateTimeOffset Played = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private readonly string folder = Path.Combine(Path.GetTempPath(), $"flint-media-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }

            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AKeptPosition_IsFoundAgain_ReplacedByALaterOne_AndForgotten()
    {
        var store = new FileMediaHistoryStore(folder);
        store.Find(Key(1)).ShouldBeNull();

        store.Save(new MediaHistoryEntry(Key(1), 60_000, 3_600_000, Played));
        store.Save(new MediaHistoryEntry(Key(1), 90_000, 3_600_000, Played.AddMinutes(1)));

        new FileMediaHistoryStore(folder).Find(Key(1))!.PositionMs.ShouldBe(90_000);
        store.Forget(Key(1));
        store.Forget(Key(2));
        store.Find(Key(1)).ShouldBeNull();
    }

    [Fact]
    public void AtMostTwoHundred_AreKept_TheLeastRecentlyPlayedGoingFirst()
    {
        var store = new FileMediaHistoryStore(folder);
        for (var i = 0; i < FileMediaHistoryStore.MaxEntries + 3; i++)
        {
            store.Save(new MediaHistoryEntry(Key(i), 60_000, 3_600_000, Played.AddMinutes(i)));
        }

        store.Find(Key(0)).ShouldBeNull();
        store.Find(Key(2)).ShouldBeNull();
        store.Find(Key(3)).ShouldNotBeNull();
        store.Find(Key(FileMediaHistoryStore.MaxEntries + 2)).ShouldNotBeNull();
    }

    [Fact]
    public void TheFile_NamesNoFile()
    {
        var store = new FileMediaHistoryStore(folder);
        var key = ResumePolicy.KeyFor(@"C:\Films\Holiday in Lisbon.mp4", 1, Played);

        store.Save(new MediaHistoryEntry(key, 60_000, 3_600_000, Played));

        File.ReadAllText(Path.Combine(folder, FileMediaHistoryStore.FileName)).ShouldNotContain("Lisbon");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"an\":\"object\"}")]
    [InlineData("null")]
    public void ADamagedFile_IsReadAsEmpty_AndWrittenOverCleanly(string text)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, FileMediaHistoryStore.FileName), text);
        var store = new FileMediaHistoryStore(folder);

        store.Find(Key(1)).ShouldBeNull();
        store.Save(new MediaHistoryEntry(Key(1), 60_000, 0, Played));
        store.Find(Key(1)).ShouldNotBeNull();
    }

    [Fact]
    public void EntriesThatCannotBeRight_AreLeftOutWhenRead()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, FileMediaHistoryStore.FileName),
            $"[{{\"Key\":\"short\",\"PositionMs\":1}},{{\"Key\":\"{Key(1)}\",\"PositionMs\":-5}},{{\"Key\":\"{Key(2)}\",\"PositionMs\":60000,\"DurationMs\":0,\"LastPlayed\":\"2026-10-02T12:00:00+00:00\"}}]");

        var store = new FileMediaHistoryStore(folder);

        store.Find("short").ShouldBeNull();
        store.Find(Key(1)).ShouldBeNull();
        store.Find(Key(2))!.PositionMs.ShouldBe(60_000);
    }

    [Fact]
    public void Clearing_DeletesTheFile_AndIsHarmlessWhenThereIsNone()
    {
        var store = new FileMediaHistoryStore(folder);
        store.Clear();
        store.Save(new MediaHistoryEntry(Key(1), 60_000, 0, Played));

        store.Clear();

        File.Exists(Path.Combine(folder, FileMediaHistoryStore.FileName)).ShouldBeFalse();
        store.Find(Key(1)).ShouldBeNull();
    }

    [Fact]
    public void AFolderThatCannotBeWritten_CostsTheHistory_NotACrash()
    {
        Directory.CreateDirectory(folder);
        var blocked = Path.Combine(folder, "blocked");
        File.WriteAllText(blocked, "a file where the folder should be");
        var store = new FileMediaHistoryStore(blocked);

        Should.NotThrow(() => store.Save(new MediaHistoryEntry(Key(1), 60_000, 0, Played)));
        store.Find(Key(1)).ShouldNotBeNull("kept for this run, though it could not reach the disk");
        new FileMediaHistoryStore(blocked).Find(Key(1)).ShouldBeNull("nothing reached the disk");
        Should.NotThrow(store.Clear);
        store.Find(Key(1)).ShouldBeNull();
    }

    [Fact]
    public void ALockedFile_CannotBeReadOrCleared_AndNothingThrows()
    {
        var store = new FileMediaHistoryStore(folder);
        store.Save(new MediaHistoryEntry(Key(1), 60_000, 0, Played));
        using var held = File.Open(Path.Combine(folder, FileMediaHistoryStore.FileName), FileMode.Open, FileAccess.Read, FileShare.None);

        new FileMediaHistoryStore(folder).Find(Key(1)).ShouldBeNull("the file cannot be read while another program holds it");
        store.Find(Key(1)).ShouldNotBeNull("the store that wrote it read it once and keeps it");
        Should.NotThrow(store.Clear);
        store.Find(Key(1)).ShouldBeNull("cleared here even though the file could not be deleted");
    }

    [Fact]
    public void TheStore_NeedsAFolder_AndAnEntry()
    {
        Should.Throw<ArgumentException>(() => new FileMediaHistoryStore(" "));
        Should.Throw<ArgumentNullException>(() => new FileMediaHistoryStore(folder).Save(null!));
        new FileMediaHistoryStore().ShouldNotBeNull();
    }

    [Fact]
    public void TheMemoryStore_KeepsFindsForgetsAndClears()
    {
        var store = new InMemoryMediaHistoryStore();
        store.Save(new MediaHistoryEntry("k", 1, 2, Played));

        store.Find("k")!.PositionMs.ShouldBe(1);
        store.Entries.Count.ShouldBe(1);
        store.Forget("k");
        store.Find("k").ShouldBeNull();
        store.Save(new MediaHistoryEntry("j", 1, 2, Played));
        store.Clear();
        store.Entries.ShouldBeEmpty();
        Should.Throw<ArgumentNullException>(() => store.Save(null!));
    }

    [Fact]
    public void TheDisk_SaysWhatIsAFolder_WhatAFileIs_AndWhatIsHidden()
    {
        var disk = new LocalMediaFileSystem();
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        var file = Path.Combine(folder, "a.mp4");
        File.WriteAllBytes(file, new byte[10]);
        var hidden = Path.Combine(folder, "h.mp4");
        File.WriteAllBytes(hidden, [1]);
        File.SetAttributes(hidden, FileAttributes.Hidden);

        disk.IsFolder(folder).ShouldBeTrue();
        disk.IsFolder(file).ShouldBeFalse();
        disk.Facts(file)!.SizeBytes.ShouldBe(10);
        disk.Facts(Path.Combine(folder, "gone.mp4")).ShouldBeNull();
        disk.Facts("\0bad").ShouldBeNull();
        disk.IsHiddenOrSystem(file).ShouldBeFalse();
        disk.IsHiddenOrSystem(hidden).ShouldBeTrue();
        disk.IsHiddenOrSystem(Path.Combine(folder, "gone.mp4")).ShouldBeTrue("what is not there is not queued");
        disk.FilesIn(folder).ShouldBe([file, hidden], ignoreOrder: true);
        disk.FoldersIn(folder).ShouldBe([Path.Combine(folder, "sub")]);
    }

    private static string Key(int i) => ResumePolicy.KeyFor($@"C:\files\{i}.mp4", i, Played);
}
