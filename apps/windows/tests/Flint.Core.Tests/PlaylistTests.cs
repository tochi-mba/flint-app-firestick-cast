using Flint.Core.Media;
using Flint.Core.Settings;
using Shouldly;

namespace Flint.Core.Tests;

/// <summary>The queue on the Media page: what is in it, what plays next, and what went before.</summary>
public sealed class PlaylistTests
{
    private const RepeatMode Off = RepeatMode.Off;

    [Fact]
    public void Adding_KeepsPlayableFiles_InTheOrderGiven_AndLeavesTheRestOut()
    {
        var playlist = new Playlist();

        var added = playlist.Add(["a.mp4", "notes.txt", "", "b.mp3", "  ", "c.jpg"]);

        Names(added).ShouldBe(["a.mp4", "b.mp3", "c.jpg"]);
        Names(playlist.Items).ShouldBe(["a.mp4", "b.mp3", "c.jpg"]);
        playlist.Current.ShouldBeNull();
    }

    [Fact]
    public void TheSameFileTwice_IsTwoItems()
    {
        var playlist = new Playlist();

        var added = playlist.Add(["a.mp4", "a.mp4"]);

        added[0].Id.ShouldNotBe(added[1].Id);
        playlist.Items.Count.ShouldBe(2);
    }

    [Fact]
    public void AnItem_KnowsItsNameAndKind()
    {
        var item = new Playlist().Add([@"C:\videos\Holiday.mkv"])[0];

        item.Name.ShouldBe("Holiday.mkv");
        item.Type.Kind.ShouldBe(MediaKind.Video);
        item.Path.ShouldBe(@"C:\videos\Holiday.mkv");
    }

    [Fact]
    public void Advancing_PlaysTheListInOrder_ThenEnds()
    {
        var playlist = Of("a", "b", "c");

        Name(playlist.Advance(Off)).ShouldBe("a.mp4");
        Name(playlist.Advance(Off)).ShouldBe("b.mp4");
        Name(playlist.Advance(Off)).ShouldBe("c.mp4");
        playlist.Advance(Off).ShouldBeNull();
        Name(playlist.Current).ShouldBe("c.mp4", "the last item stays current when the queue ends");
    }

    [Fact]
    public void RepeatAll_GoesBackToTheFirst()
    {
        var playlist = Of("a", "b");
        playlist.Advance(Off);
        playlist.Advance(Off);

        Name(playlist.PeekNext(RepeatMode.All)).ShouldBe("a.mp4");
        Name(playlist.Advance(RepeatMode.All)).ShouldBe("a.mp4");
    }

    [Fact]
    public void RepeatOne_KeepsTheSameItem_AndStartsFromTheFirstWhenNothingPlays()
    {
        var playlist = Of("a", "b");

        Name(playlist.Advance(RepeatMode.One)).ShouldBe("a.mp4");
        Name(playlist.Advance(RepeatMode.One)).ShouldBe("a.mp4");
        Name(playlist.PeekNext(RepeatMode.One)).ShouldBe("a.mp4");
    }

    [Fact]
    public void AnEmptyQueue_AnswersEverythingWithoutThrowing()
    {
        var playlist = new Playlist();

        playlist.Advance(RepeatMode.All).ShouldBeNull();
        playlist.PeekNext(RepeatMode.One).ShouldBeNull();
        playlist.GoBack().ShouldBeNull();
        playlist.CanGoBack.ShouldBeFalse();
        playlist.Remove(1).ShouldBeFalse();
        playlist.Move(1, 0).ShouldBeFalse();
        playlist.Select(1).ShouldBeNull();
        playlist.Clear();
        playlist.Items.ShouldBeEmpty();
    }

    [Fact]
    public void PlayNext_GoesStraightAfterTheCurrentItem_OrToTheFrontWhenNothingPlays()
    {
        var playlist = Of("a", "b", "c");
        Names(playlist.AddNext(["x.mp4"])).ShouldBe(["x.mp4"]);
        Names(playlist.Items).ShouldBe(["x.mp4", "a.mp4", "b.mp4", "c.mp4"]);

        playlist.Select(playlist.Items[1].Id);
        playlist.AddNext(["y.mp4", "z.mp4"]);

        Names(playlist.Items).ShouldBe(["x.mp4", "a.mp4", "y.mp4", "z.mp4", "b.mp4", "c.mp4"]);
        Name(playlist.Advance(Off)).ShouldBe("y.mp4");
    }

    [Fact]
    public void PlayNow_JumpsToTheItem_AndTheListCarriesOnFromThere()
    {
        var playlist = Of("a", "b", "c", "d");
        playlist.Advance(Off);

        Name(playlist.Select(playlist.Items[2].Id)).ShouldBe("c.mp4");
        Name(playlist.Advance(Off)).ShouldBe("d.mp4");
    }

    [Fact]
    public void RemovingAnItemBeforeOrAfterTheCurrentOne_LeavesWhatPlaysNextAlone()
    {
        var playlist = Of("a", "b", "c", "d");
        playlist.Advance(Off);
        playlist.Advance(Off);

        playlist.Remove(playlist.Items[0].Id).ShouldBeTrue();
        playlist.Remove(playlist.Items[^1].Id).ShouldBeTrue();

        Name(playlist.Current).ShouldBe("b.mp4");
        Name(playlist.Advance(Off)).ShouldBe("c.mp4");
    }

    [Fact]
    public void RemovingThePlayingItem_PlaysTheOneAfterIt_WithoutSkipping()
    {
        var playlist = Of("a", "b", "c");
        playlist.Advance(Off);
        playlist.Advance(Off);

        playlist.Remove(playlist.Current!.Id);

        playlist.Current.ShouldBeNull();
        Name(playlist.PeekNext(Off)).ShouldBe("c.mp4");
        Name(playlist.Advance(Off)).ShouldBe("c.mp4");
    }

    [Fact]
    public void RemovingThePlayingItem_ThenTheOneAfterIt_PlaysTheOneAfterThat()
    {
        var playlist = Of("a", "b", "c", "d");
        playlist.Advance(Off);
        playlist.Remove(playlist.Current!.Id);

        playlist.Remove(playlist.Items[0].Id);

        Name(playlist.Advance(Off)).ShouldBe("c.mp4");
    }

    [Fact]
    public void RemovingTheLastItemWhilePlaying_EndsTheQueue_UnlessItRepeats()
    {
        var playlist = Of("a", "b");
        playlist.Advance(Off);
        playlist.Advance(Off);

        playlist.Remove(playlist.Current!.Id);

        playlist.PeekNext(Off).ShouldBeNull();
        Name(playlist.PeekNext(RepeatMode.All)).ShouldBe("a.mp4");
    }

    [Fact]
    public void AfterRemovingThePlayingItem_MovingOthersKeepsThePlace()
    {
        var playlist = Of("a", "b", "c", "d");
        playlist.Advance(Off);
        playlist.Advance(Off);
        playlist.Remove(playlist.Current!.Id);

        playlist.Move(playlist.Items[0].Id, 99);

        Names(playlist.Items).ShouldBe(["c.mp4", "d.mp4", "a.mp4"]);
        Name(playlist.Advance(Off)).ShouldBe("c.mp4");
    }

    [Fact]
    public void AfterRemovingThePlayingItem_PlayNextGoesWhereItWas()
    {
        var playlist = Of("a", "b", "c");
        playlist.Advance(Off);
        playlist.Advance(Off);
        playlist.Remove(playlist.Current!.Id);

        playlist.AddNext(["x.mp4"]);

        Names(playlist.Items).ShouldBe(["a.mp4", "x.mp4", "c.mp4"]);
        Name(playlist.Advance(Off)).ShouldBe("x.mp4");
    }

    [Fact]
    public void AfterRemovingThePlayingItem_BackGoesToTheOneBeforeIt()
    {
        var playlist = Of("a", "b", "c");
        playlist.Advance(Off);
        playlist.Advance(Off);
        playlist.Remove(playlist.Current!.Id);

        playlist.CanGoBack.ShouldBeTrue();
        Name(playlist.GoBack()).ShouldBe("a.mp4");
    }

    [Theory]
    [InlineData(0, 2, "b,c,a")]
    [InlineData(2, 0, "c,a,b")]
    [InlineData(1, 1, "a,b,c")]
    [InlineData(0, -5, "a,b,c")]
    [InlineData(0, 99, "b,c,a")]
    public void Moving_PutsTheItemWhereItWasDropped_ClampedToTheEnds(int from, int to, string expected)
    {
        var playlist = Of("a", "b", "c");

        playlist.Move(playlist.Items[from].Id, to).ShouldBeTrue();

        string.Join(',', playlist.Items.Select(item => Path.GetFileNameWithoutExtension(item.Path))).ShouldBe(expected);
    }

    [Fact]
    public void Moving_ChangesWhatPlaysNext_WithoutShuffle()
    {
        var playlist = Of("a", "b", "c");
        playlist.Advance(Off);

        playlist.Move(playlist.Items[2].Id, 1);

        Name(playlist.Advance(Off)).ShouldBe("c.mp4");
    }

    [Fact]
    public void Back_GoesUpTheList_AndNotPastTheTop()
    {
        var playlist = Of("a", "b", "c");
        playlist.CanGoBack.ShouldBeFalse();
        playlist.Advance(Off);
        playlist.CanGoBack.ShouldBeFalse("the first item has nothing before it");
        playlist.Advance(Off);
        playlist.Advance(Off);

        Name(playlist.GoBack()).ShouldBe("b.mp4");
        Name(playlist.GoBack()).ShouldBe("a.mp4");
        playlist.GoBack().ShouldBeNull();
        Name(playlist.Current).ShouldBe("a.mp4");
    }

    [Fact]
    public void Clearing_EmptiesTheQueue_AndForgetsWhatPlayed()
    {
        var playlist = Of("a", "b");
        playlist.Advance(Off);
        playlist.Advance(Off);

        playlist.Clear();

        playlist.Items.ShouldBeEmpty();
        playlist.Current.ShouldBeNull();
        playlist.CanGoBack.ShouldBeFalse();
        Name(playlist.Add(["c.mp4"])[0]).ShouldBe("c.mp4");
        Name(playlist.Advance(Off)).ShouldBe("c.mp4");
    }

    [Fact]
    public void Shuffle_PlaysEveryItemOnce_BeforeTheQueueEnds()
    {
        var playlist = Of(Seeded(1), "a", "b", "c", "d", "e");
        playlist.Shuffle = true;

        var played = Drain(playlist, Off);

        played.Count.ShouldBe(5);
        played.ShouldBe(["a.mp4", "b.mp4", "c.mp4", "d.mp4", "e.mp4"], ignoreOrder: true);
        playlist.Advance(Off).ShouldBeNull();
    }

    [Fact]
    public void Shuffle_WithAFixedSeed_GivesAFixedOrder_ThatIsNotTheListOrder()
    {
        string[] Run()
        {
            var playlist = Of(Seeded(7), "a", "b", "c", "d", "e", "f");
            playlist.Shuffle = true;
            return [.. Drain(playlist, Off)];
        }

        var first = Run();
        Run().ShouldBe(first);
        first.ShouldNotBe(["a.mp4", "b.mp4", "c.mp4", "d.mp4", "e.mp4", "f.mp4"]);
    }

    [Fact]
    public void Shuffle_LeavesTheListAsArranged()
    {
        var playlist = Of(Seeded(3), "a", "b", "c");

        playlist.Shuffle = true;
        playlist.Advance(Off);

        Names(playlist.Items).ShouldBe(["a.mp4", "b.mp4", "c.mp4"]);
    }

    [Fact]
    public void Shuffle_TurnedOn_KeepsThePlayingItem_AndDoesNotPlayItAgainThisCycle()
    {
        var playlist = Of(Seeded(5), "a", "b", "c", "d");
        playlist.Advance(Off);

        playlist.Shuffle = true;
        playlist.Shuffle = true;

        Name(playlist.Current).ShouldBe("a.mp4");
        var rest = Drain(playlist, Off);
        rest.ShouldBe(["b.mp4", "c.mp4", "d.mp4"], ignoreOrder: true);
    }

    [Fact]
    public void Shuffle_TurnedOff_CarriesOnInListOrderFromThePlayingItem()
    {
        var playlist = Of(Seeded(2), "a", "b", "c", "d");
        playlist.Shuffle = true;
        playlist.Select(playlist.Items[1].Id);

        playlist.Shuffle = false;

        playlist.Shuffle.ShouldBeFalse();
        Name(playlist.Advance(Off)).ShouldBe("c.mp4");
    }

    [Fact]
    public void Shuffle_WithRepeatAll_StartsANewCycle_AndShowsTheItemThatThenPlays()
    {
        var playlist = Of(Seeded(11), "a", "b", "c");
        playlist.Shuffle = true;
        Drain(playlist, Off);

        var shown = playlist.PeekNext(RepeatMode.All);
        playlist.PeekNext(RepeatMode.All).ShouldBe(shown, "looking twice does not change the answer");
        var played = playlist.Advance(RepeatMode.All);

        played.ShouldBe(shown);
        played.ShouldNotBe(null);
        var cycle = new List<string> { played!.Name };
        cycle.AddRange(Drain(playlist, Off));
        cycle.ShouldBe(["a.mp4", "b.mp4", "c.mp4"], ignoreOrder: true);
    }

    [Fact]
    public void Shuffle_WithRepeatAll_NeverPlaysTheSameItemTwiceInARow()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var playlist = Of(Seeded(seed), "a", "b", "c");
            playlist.Shuffle = true;
            var last = Drain(playlist, Off)[^1];

            playlist.Advance(RepeatMode.All)!.Name.ShouldNotBe(last, $"seed {seed}");
        }
    }

    [Fact]
    public void Shuffle_WithRepeatAll_AndOneItem_PlaysItAgain()
    {
        var playlist = Of(Seeded(1), "a");
        playlist.Shuffle = true;
        playlist.Advance(Off);

        Name(playlist.PeekNext(RepeatMode.All)).ShouldBe("a.mp4");
        Name(playlist.Advance(RepeatMode.All)).ShouldBe("a.mp4");
    }

    [Fact]
    public void Shuffle_WithRepeatAll_OnAnEmptyQueue_HasNothing()
    {
        var playlist = new Playlist(Seeded(1)) { Shuffle = true };

        playlist.PeekNext(RepeatMode.All).ShouldBeNull();
        playlist.Advance(RepeatMode.All).ShouldBeNull();
    }

    [Fact]
    public void Shuffle_AddingDuringACycle_GivesTheNewItemsATurnInIt()
    {
        var playlist = Of(Seeded(4), "a", "b");
        playlist.Shuffle = true;
        playlist.Advance(Off);

        playlist.Add(["x.mp4", "y.mp4"]);

        var rest = new List<string> { playlist.Current!.Name };
        rest.AddRange(Drain(playlist, Off));
        rest.ShouldBe(["a.mp4", "b.mp4", "x.mp4", "y.mp4"], ignoreOrder: true);
    }

    [Fact]
    public void Shuffle_PlayNext_StillPlaysNext()
    {
        var playlist = Of(Seeded(9), "a", "b", "c");
        playlist.Shuffle = true;
        playlist.Advance(Off);

        playlist.AddNext(["x.mp4"]);

        Name(playlist.Advance(Off)).ShouldBe("x.mp4");
    }

    [Fact]
    public void Shuffle_Back_GoesThroughWhatActuallyPlayed_AndForwardAgainReplaysIt()
    {
        var playlist = Of(Seeded(6), "a", "b", "c", "d");
        playlist.Shuffle = true;
        var first = playlist.Advance(Off)!;
        var second = playlist.Advance(Off)!;
        var third = playlist.Advance(Off)!;

        playlist.GoBack().ShouldBe(second);
        playlist.GoBack().ShouldBe(first);
        playlist.CanGoBack.ShouldBeFalse();
        playlist.Advance(Off).ShouldBe(second, "the item left by going back is next again");
        playlist.Advance(Off).ShouldBe(third);
    }

    [Fact]
    public void Shuffle_RemovingAnItem_TakesItOutOfTheCycleAndTheHistory()
    {
        var playlist = Of(Seeded(8), "a", "b", "c", "d");
        playlist.Shuffle = true;
        var first = playlist.Advance(Off)!;
        playlist.Advance(Off);

        playlist.Remove(first.Id);

        playlist.CanGoBack.ShouldBeFalse("the only item it could go back to is gone");
        Drain(playlist, Off).ShouldNotContain(first.Name);
    }

    [Fact]
    public void Shuffle_RemovingThePlayingItem_CarriesOnWithTheCycle()
    {
        var playlist = Of(Seeded(10), "a", "b", "c");
        playlist.Shuffle = true;
        var playing = playlist.Advance(Off)!;

        playlist.Remove(playing.Id);

        Drain(playlist, Off).Count.ShouldBe(2);
    }

    [Fact]
    public void Shuffle_BackAfterRemovingThePlayingItem_GoesToTheOneBefore()
    {
        var playlist = Of(Seeded(12), "a", "b", "c");
        playlist.Shuffle = true;
        var first = playlist.Advance(Off)!;
        var second = playlist.Advance(Off)!;

        playlist.Remove(second.Id);

        playlist.GoBack().ShouldBe(first);
    }

    [Fact]
    public void Shuffle_Clearing_KeepsShuffleOn()
    {
        var playlist = Of(Seeded(1), "a", "b");
        playlist.Shuffle = true;

        playlist.Clear();
        playlist.Add(["c.mp4"]);

        playlist.Shuffle.ShouldBeTrue();
        Name(playlist.Advance(Off)).ShouldBe("c.mp4");
    }

    [Fact]
    public void Shuffle_ADecidedNextCycle_IsThrownAwayWhenTheQueueChanges()
    {
        var playlist = Of(Seeded(13), "a", "b");
        playlist.Shuffle = true;
        Drain(playlist, Off);
        playlist.PeekNext(RepeatMode.All);

        playlist.Add(["c.mp4"]);

        var cycle = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            cycle.Add(playlist.Advance(RepeatMode.All)!.Name);
        }

        cycle.ShouldContain("c.mp4");
    }

    [Fact]
    public void NoFiles_IsRefused()
    {
        var playlist = new Playlist();

        Should.Throw<ArgumentNullException>(() => playlist.Add(null!));
        Should.Throw<ArgumentNullException>(() => playlist.AddNext(null!));
    }

    private static Random Seeded(int seed) => new(seed);

    private static Playlist Of(params string[] names) => Of(Seeded(0), names);

    private static Playlist Of(Random random, params string[] names)
    {
        var playlist = new Playlist(random);
        playlist.Add(names.Select(name => name + ".mp4"));
        return playlist;
    }

    /// <summary>Plays to the end of the queue, returning the names in the order they played.</summary>
    private static List<string> Drain(Playlist playlist, RepeatMode repeat)
    {
        var played = new List<string>();
        while (playlist.Advance(repeat) is { } item)
        {
            played.Add(item.Name);
            played.Count.ShouldBeLessThan(100, "the queue should have ended");
        }

        return played;
    }

    private static string? Name(PlaylistItem? item) => item?.Name;

    private static List<string> Names(IEnumerable<PlaylistItem> items) => [.. items.Select(item => item.Name)];
}
