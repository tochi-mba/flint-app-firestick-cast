using Shouldly;

namespace Flint.Core.Tests;

/// <summary>The rules for remembered TVs, and when to try reaching one again.</summary>
public sealed class KnownTvsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);

    private static KnownTv Tv(string name, int minutesAgo = 0, string? token = "t") =>
        new(name, "10.0.0.5", 47855, token, Now.AddMinutes(-minutesAgo));

    [Fact]
    public void TheList_IsNewestFirst_OneEntryAName_AndAtMostEight()
    {
        IReadOnlyList<KnownTv> tvs = [];
        for (var index = 0; index < 10; index++)
        {
            tvs = KnownTvList.With(tvs, Tv($"TV {index}", minutesAgo: 100 - index));
        }

        tvs.Count.ShouldBe(KnownTvList.MaxEntries);
        tvs[0].Name.ShouldBe("TV 9");
        tvs.ShouldNotContain(tv => tv.Name == "TV 0" || tv.Name == "TV 1", "the oldest go first");

        var moved = KnownTvList.With(tvs, Tv("TV 3") with { Address = "10.0.0.99" });
        moved.Count(tv => tv.Name == "TV 3").ShouldBe(1);
        moved[0].Address.ShouldBe("10.0.0.99");
    }

    [Fact]
    public void TheMemoryStore_KeepsForgetsAndOrders()
    {
        var store = new InMemoryKnownTvStore();
        store.Save(Tv("Bedroom", minutesAgo: 5));
        store.Save(Tv("Living Room"));

        store.Load().Select(tv => tv.Name).ShouldBe(["Living Room", "Bedroom"]);
        store.Forget("Bedroom");
        store.Load().ShouldHaveSingleItem().Name.ShouldBe("Living Room");
        store.ForgetAll();
        store.Load().ShouldBeEmpty();
        Should.Throw<ArgumentNullException>(() => store.Save(null!));
    }

    [Fact]
    public void ATv_HasALogin_OnlyWithAToken_AndNeverShowsIt()
    {
        Tv("Living Room").HasLogin.ShouldBeTrue();
        Tv("Living Room", token: null).HasLogin.ShouldBeFalse();
        Tv("Living Room", token: "secret-login").ToString().ShouldBe("Living Room at 10.0.0.5:47855");
    }

    [Fact]
    public void Retries_WaitOneTwoFourEight_ThenFifteenSeconds()
    {
        Enumerable.Range(1, 7).Select(attempt => ReconnectSchedule.DelayBefore(attempt).TotalSeconds)
            .ShouldBe([1, 2, 4, 8, 15, 15, 15]);
        Should.Throw<ArgumentOutOfRangeException>(() => ReconnectSchedule.DelayBefore(0));
    }
}
