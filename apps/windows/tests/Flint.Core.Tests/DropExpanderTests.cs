using Flint.Core.Media;
using Flint.Core.Settings;
using Shouldly;

namespace Flint.Core.Tests;

/// <summary>Turning dropped files and folders into the files to queue.</summary>
public sealed class DropExpanderTests
{
    [Fact]
    public void DroppedFiles_AreQueuedByName_OrAsDropped()
    {
        var disk = new FakeDisk().File(@"D:\b 10.mp4").File(@"D:\b 2.mp4").File(@"D:\a.mp3");
        string[] dropped = [@"D:\b 10.mp4", @"D:\b 2.mp4", @"D:\a.mp3"];

        DropExpander.Expand(dropped, disk, false, DropOrder.ByName).Files
            .ShouldBe([@"D:\a.mp3", @"D:\b 2.mp4", @"D:\b 10.mp4"]);
        DropExpander.Expand(dropped, disk, false, DropOrder.AsDropped).Files
            .ShouldBe(dropped);
    }

    [Fact]
    public void AFolder_GivesItsPlayableFiles_ByName_AndNotItsSubfolders()
    {
        var disk = new FakeDisk()
            .Folder(@"D:\Show", @"D:\Show\Episode 10.mkv", @"D:\Show\Episode 2.mkv", @"D:\Show\notes.txt", @"D:\Show\Extras")
            .Folder(@"D:\Show\Extras", @"D:\Show\Extras\Trailer.mp4");

        var contents = DropExpander.Expand([@"D:\Show"], disk, includeSubfolders: false, DropOrder.AsDropped);

        contents.Files.ShouldBe([@"D:\Show\Episode 2.mkv", @"D:\Show\Episode 10.mkv"]);
        contents.Capped.ShouldBeFalse();
        contents.Skipped.ShouldBe(0);
    }

    [Fact]
    public void WithSubfolders_AFoldersOwnFilesComeFirst_ThenEachSubfolderByName()
    {
        var disk = new FakeDisk()
            .Folder(@"D:\Show", @"D:\Show\b.mp4", @"D:\Show\Season 10", @"D:\Show\Season 2")
            .Folder(@"D:\Show\Season 2", @"D:\Show\Season 2\s2.mp4")
            .Folder(@"D:\Show\Season 10", @"D:\Show\Season 10\s10.mp4");

        DropExpander.Expand([@"D:\Show"], disk, includeSubfolders: true, DropOrder.ByName).Files
            .ShouldBe([@"D:\Show\b.mp4", @"D:\Show\Season 2\s2.mp4", @"D:\Show\Season 10\s10.mp4"]);
    }

    [Fact]
    public void HiddenAndSystemFilesAndFolders_AreLeftOut()
    {
        var disk = new FakeDisk()
            .Folder(@"D:\Show", @"D:\Show\a.mp4", @"D:\Show\.hidden.mp4", @"D:\Show\Secret")
            .Folder(@"D:\Show\Secret", @"D:\Show\Secret\x.mp4")
            .Hidden(@"D:\Show\.hidden.mp4")
            .Hidden(@"D:\Show\Secret")
            .File(@"D:\thumbs.jpg")
            .Hidden(@"D:\thumbs.jpg");

        DropExpander.Expand([@"D:\Show", @"D:\thumbs.jpg"], disk, includeSubfolders: true, DropOrder.AsDropped).Files
            .ShouldBe([@"D:\Show\a.mp4"]);
    }

    [Fact]
    public void AFolderThatCannotBeRead_IsCountedAndPassedOver()
    {
        var disk = new FakeDisk()
            .Folder(@"D:\Show", @"D:\Show\a.mp4", @"D:\Show\Locked")
            .Folder(@"D:\Show\Locked")
            .Unreadable(@"D:\Show\Locked")
            .Folder(@"D:\Gone")
            .Unreadable(@"D:\Gone", new IOException("gone"));

        var contents = DropExpander.Expand([@"D:\Show", @"D:\Gone"], disk, includeSubfolders: true, DropOrder.AsDropped);

        contents.Files.ShouldBe([@"D:\Show\a.mp4"]);
        contents.Skipped.ShouldBe(2);
    }

    [Fact]
    public void UnplayableAndShortcutFiles_AreLeftOut()
    {
        var disk = new FakeDisk().File(@"D:\a.mp4").File(@"D:\Film.lnk").File(@"D:\notes.txt");

        DropExpander.Expand([@"D:\a.mp4", @"D:\Film.lnk", @"D:\notes.txt"], disk, false, DropOrder.AsDropped).Files
            .ShouldBe([@"D:\a.mp4"]);
    }

    [Fact]
    public void ADropIsCapped_InAFolder_AtTheTopLevel_AndInASubfolder()
    {
        var many = Enumerable.Range(0, DropExpander.MaximumFiles + 5).Select(i => $@"D:\Big\{i:D5}.mp3").ToArray();
        var disk = new FakeDisk().Folder(@"D:\Big", many);
        var inFolder = DropExpander.Expand([@"D:\Big"], disk, false, DropOrder.AsDropped);
        inFolder.Files.Count.ShouldBe(DropExpander.MaximumFiles);
        inFolder.Capped.ShouldBeTrue();

        var loose = new FakeDisk();
        foreach (var file in many)
        {
            loose.File(file);
        }

        var atTop = DropExpander.Expand([.. many, @"D:\after.mp4"], loose, false, DropOrder.AsDropped);
        atTop.Files.Count.ShouldBe(DropExpander.MaximumFiles);
        atTop.Capped.ShouldBeTrue();
        atTop.Files.ShouldNotContain(@"D:\after.mp4");

        var nested = new FakeDisk()
            .Folder(@"D:\Top", @"D:\Top\Sub", @"D:\Top\Zed")
            .Folder(@"D:\Top\Sub", many)
            .Folder(@"D:\Top\Zed", @"D:\Top\Zed\z.mp4");
        var deep = DropExpander.Expand([@"D:\Top", @"D:\other.mp4"], nested.File(@"D:\other.mp4"), true, DropOrder.AsDropped);
        deep.Files.Count.ShouldBe(DropExpander.MaximumFiles);
        deep.Capped.ShouldBeTrue();
        deep.Files.ShouldNotContain(@"D:\Top\Zed\z.mp4");
        deep.Files.ShouldNotContain(@"D:\other.mp4");
    }

    [Fact]
    public void NothingDropped_IsNothing_AndMissingArgumentsAreRefused()
    {
        var disk = new FakeDisk();

        DropExpander.Expand([], disk, true, DropOrder.ByName).Files.ShouldBeEmpty();
        Should.Throw<ArgumentNullException>(() => DropExpander.Expand(null!, disk, true, DropOrder.ByName));
        Should.Throw<ArgumentNullException>(() => DropExpander.Expand([], null!, true, DropOrder.ByName));
    }

    [Fact]
    public void TwoFilesWithTheSameName_InDifferentFolders_StayInAFixedOrder()
    {
        var disk = new FakeDisk().File(@"E:\clip.mp4").File(@"D:\clip.mp4");

        DropExpander.Expand([@"E:\clip.mp4", @"D:\clip.mp4"], disk, false, DropOrder.ByName).Files
            .ShouldBe([@"D:\clip.mp4", @"E:\clip.mp4"]);
    }

    /// <summary>A disk made of names, with folders, hidden items and folders that refuse to open.</summary>
    private sealed class FakeDisk : IMediaFileSystem
    {
        private readonly Dictionary<string, List<string>> folders = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> filesOnDisk = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> hidden = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Exception> unreadable = new(StringComparer.OrdinalIgnoreCase);

        public FakeDisk File(string path)
        {
            filesOnDisk.Add(path);
            return this;
        }

        public FakeDisk Folder(string path, params string[] children)
        {
            folders[path] = [.. children];
            foreach (var child in children.Where(child => Path.HasExtension(child)))
            {
                filesOnDisk.Add(child);
            }

            return this;
        }

        public FakeDisk Hidden(string path)
        {
            hidden.Add(path);
            return this;
        }

        public FakeDisk Unreadable(string path, Exception? failure = null)
        {
            unreadable[path] = failure ?? new UnauthorizedAccessException("no");
            return this;
        }

        public bool IsFolder(string path) => folders.ContainsKey(path);

        public MediaFileFacts? Facts(string path) =>
            filesOnDisk.Contains(path) ? new MediaFileFacts(1, DateTimeOffset.UnixEpoch) : null;

        public bool IsHiddenOrSystem(string path) => hidden.Contains(path);

        public IEnumerable<string> FilesIn(string folder) =>
            Children(folder).Where(child => !folders.ContainsKey(child));

        public IEnumerable<string> FoldersIn(string folder) =>
            Children(folder).Where(folders.ContainsKey);

        private List<string> Children(string folder) =>
            unreadable.TryGetValue(folder, out var failure) ? throw failure : folders[folder];
    }
}
