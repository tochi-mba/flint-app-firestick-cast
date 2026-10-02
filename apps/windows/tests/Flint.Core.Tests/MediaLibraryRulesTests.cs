using Flint.Core.Media;
using Flint.Core.Settings;
using Shouldly;

namespace Flint.Core.Tests;

/// <summary>Which files are offered, the order they are listed in, and what is kept to resume them.</summary>
public sealed class MediaLibraryRulesTests
{
    private static readonly DateTimeOffset Played = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("a.mp4", "video/mp4", MediaKind.Video, true)]
    [InlineData("a.mkv", "video/x-matroska", MediaKind.Video, true)]
    [InlineData("a.webm", "video/webm", MediaKind.Video, true)]
    [InlineData("a.mp3", "audio/mpeg", MediaKind.Audio, true)]
    [InlineData("a.m4a", "audio/mp4", MediaKind.Audio, true)]
    [InlineData("a.jpg", "image/jpeg", MediaKind.Picture, true)]
    [InlineData("a.jpeg", "image/jpeg", MediaKind.Picture, true)]
    [InlineData("a.png", "image/png", MediaKind.Picture, true)]
    [InlineData("a.m4v", "video/mp4", MediaKind.Video, false)]
    [InlineData("a.mov", "video/quicktime", MediaKind.Video, false)]
    [InlineData("a.ts", "video/mp2t", MediaKind.Video, false)]
    [InlineData("a.aac", "audio/aac", MediaKind.Audio, false)]
    [InlineData("a.flac", "audio/flac", MediaKind.Audio, false)]
    [InlineData("a.wav", "audio/wav", MediaKind.Audio, false)]
    [InlineData("a.ogg", "audio/ogg", MediaKind.Audio, false)]
    [InlineData("a.opus", "audio/ogg", MediaKind.Audio, false)]
    [InlineData("a.webp", "image/webp", MediaKind.Picture, false)]
    [InlineData("a.bmp", "image/bmp", MediaKind.Picture, false)]
    [InlineData("a.gif", "image/gif", MediaKind.Picture, false)]
    public void EachKnownExtension_HasItsTypeKindAndWhetherItWasTried(string path, string mime, MediaKind kind, bool tried)
    {
        var type = MediaFileTypes.For(path);

        type.MimeType.ShouldBe(mime);
        type.Kind.ShouldBe(kind);
        type.Tried.ShouldBe(tried);
        type.IsPicture.ShouldBe(kind is MediaKind.Picture);
        MediaFileTypes.IsPlayable(path).ShouldBeTrue();
    }

    [Theory]
    [InlineData(@"C:\Films\HOLIDAY.MP4")]
    [InlineData("holiday.Mp4")]
    [InlineData("my.holiday.backup.mp4")]
    public void Extensions_AreReadWithoutRegardToCase_AndOnlyTheLastCounts(string path) =>
        MediaFileTypes.For(path).MimeType.ShouldBe("video/mp4");

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("README")]
    [InlineData("archive.mp4.zip")]
    [InlineData("")]
    public void OtherFiles_AreNotOffered(string path)
    {
        MediaFileTypes.IsPlayable(path).ShouldBeFalse();
        MediaFileTypes.For(path).MimeType.ShouldBe(MediaFileTypes.UnknownMimeType);
    }

    [Fact]
    public void TheExtensionList_IsEveryKnownType_ForAPickersFilter()
    {
        MediaFileTypes.Extensions.Count.ShouldBe(19);
        MediaFileTypes.Extensions.ShouldAllBe(extension => extension.StartsWith('.'));
        Should.Throw<ArgumentNullException>(() => MediaFileTypes.For(null!));
    }

    [Fact]
    public void NaturalOrder_ReadsNumbersAsNumbers()
    {
        string[] names = ["Episode 10.mp4", "episode 2.mp4", "Episode 1.mp4", "Extras.mp4", "Episode 02.mp4"];

        names.Order(NaturalOrder.Instance).ShouldBe(
            ["Episode 1.mp4", "episode 2.mp4", "Episode 02.mp4", "Episode 10.mp4", "Extras.mp4"]);
    }

    [Theory]
    [InlineData("a", "a", 0)]
    [InlineData("a", "b", -1)]
    [InlineData("B", "a", 1)]
    [InlineData("a", "A", 1)]
    [InlineData("a2", "a10", -1)]
    [InlineData("a10", "a2", 1)]
    [InlineData("a", "a1", -1)]
    [InlineData("a1b", "a1", 1)]
    [InlineData("track 007", "track 7", 1)]
    [InlineData("99999999999999999999", "100000000000000000000", -1)]
    public void NaturalOrder_IsTotal(string x, string y, int expected)
    {
        Math.Sign(NaturalOrder.Instance.Compare(x, y)).ShouldBe(expected);
        Math.Sign(NaturalOrder.Instance.Compare(y, x)).ShouldBe(-expected);
    }

    [Fact]
    public void NaturalOrder_PutsNothingFirst()
    {
        NaturalOrder.Instance.Compare(null, "a").ShouldBeLessThan(0);
        NaturalOrder.Instance.Compare("a", null).ShouldBeGreaterThan(0);
        NaturalOrder.Instance.Compare(null, null).ShouldBe(0);
    }

    [Theory]
    [InlineData(29_999, 3_600_000, false)]
    [InlineData(30_000, 3_600_000, true)]
    [InlineData(3_419_999, 3_600_000, true)]
    [InlineData(3_420_000, 3_600_000, false)]
    [InlineData(45_000, -1, true)]
    [InlineData(45_000, 0, true)]
    [InlineData(10_000, -1, false)]
    public void APositionIsWorthKeeping_PastTheFirstHalfMinute_AndBeforeTheCredits(long position, long duration, bool keep) =>
        ResumePolicy.IsWorthKeeping(position, duration).ShouldBe(keep);

    [Theory]
    [InlineData(ResumeMode.Resume, ResumeDecision.Resume)]
    [InlineData(ResumeMode.Ask, ResumeDecision.Ask)]
    [InlineData(ResumeMode.StartOver, ResumeDecision.FromStart)]
    public void AKeptPosition_IsUsedAsTheSettingSays(ResumeMode mode, ResumeDecision expected) =>
        ResumePolicy.Decide(mode, new MediaHistoryEntry("k", 600_000, 3_600_000, Played)).ShouldBe(expected);

    [Theory]
    [InlineData(ResumeMode.Resume)]
    [InlineData(ResumeMode.Ask)]
    public void NothingKept_OrNothingWorthKeeping_StartsFromTheStart(ResumeMode mode)
    {
        ResumePolicy.Decide(mode, null).ShouldBe(ResumeDecision.FromStart);
        ResumePolicy.Decide(mode, new MediaHistoryEntry("k", 3_500_000, 3_600_000, Played)).ShouldBe(ResumeDecision.FromStart);
    }

    [Fact]
    public void AFilesKey_ChangesWithItsPathSizeOrTime_AndNamesNothing()
    {
        var key = ResumePolicy.KeyFor(@"C:\Films\Holiday.mp4", 1_000, Played);

        key.Length.ShouldBe(64);
        key.ShouldNotContain("Holiday", Case.Insensitive);
        ResumePolicy.KeyFor(@"c:\films\HOLIDAY.MP4", 1_000, Played).ShouldBe(key, "Windows paths are not case-sensitive");
        ResumePolicy.KeyFor(@"C:\Films\Holiday.mp4", 1_001, Played).ShouldNotBe(key);
        ResumePolicy.KeyFor(@"C:\Films\Holiday.mp4", 1_000, Played.AddSeconds(1)).ShouldNotBe(key);
        ResumePolicy.KeyFor(@"C:\Films\Other.mp4", 1_000, Played).ShouldNotBe(key);
        ResumePolicy.KeyFor(@"C:\Films\Holiday.mp4", 1_000, Played.ToOffset(TimeSpan.FromHours(2))).ShouldBe(key, "the same moment in another zone");
        Should.Throw<ArgumentException>(() => ResumePolicy.KeyFor(" ", 1, Played));
    }
}
