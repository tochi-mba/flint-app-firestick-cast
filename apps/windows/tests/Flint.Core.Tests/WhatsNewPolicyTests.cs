using Shouldly;

namespace Flint.Core.Tests;

/// <summary>When "What's new" has something to show, and what.</summary>
public sealed class WhatsNewPolicyTests
{
    private static readonly string[] Catalogue = ["a", "b", "c"];

    [Fact]
    public void AFirstLaunch_ShowsNothing_WhateverIsRecorded()
    {
        WhatsNewPolicy.Unseen(Catalogue, null, introductionDone: false).ShouldBeEmpty();
        WhatsNewPolicy.Unseen(Catalogue, new HashSet<string>(), introductionDone: false).ShouldBeEmpty();
    }

    [Fact]
    public void AnUpdateFromBeforeWhatsNewExisted_ShowsEverything()
    {
        WhatsNewPolicy.Unseen(Catalogue, null, introductionDone: true).ShouldBe(Catalogue);
    }

    [Fact]
    public void AfterThat_OnlyWhatHasNotBeenSeen_InCatalogueOrder()
    {
        WhatsNewPolicy.Unseen(Catalogue, new HashSet<string> { "b" }, introductionDone: true).ShouldBe(["a", "c"]);
        WhatsNewPolicy.Unseen(Catalogue, new HashSet<string>(Catalogue), introductionDone: true).ShouldBeEmpty();
        WhatsNewPolicy.Unseen(Catalogue, new HashSet<string> { "retired" }, introductionDone: true).ShouldBe(Catalogue);
    }

    [Fact]
    public void NoCatalogue_IsRefused() =>
        Should.Throw<ArgumentNullException>(() => WhatsNewPolicy.Unseen(null!, null, true));

    [Fact]
    public void TheMemoryStore_StartsUnrecorded_OrWithWhatItIsGiven_AndAddsToIt()
    {
        var empty = new InMemoryWhatsNewState();
        empty.Seen.ShouldBeNull();
        empty.MarkSeen(["a"]);
        empty.Seen.ShouldBe(["a"], ignoreOrder: true);

        var given = new InMemoryWhatsNewState(["a"]);
        given.MarkSeen(["b", "a"]);
        given.Seen.ShouldBe(["a", "b"], ignoreOrder: true);
        Should.Throw<ArgumentNullException>(() => given.MarkSeen(null!));
    }
}
