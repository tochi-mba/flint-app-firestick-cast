using System.Text.Json;
using Shouldly;

namespace Flint.Core.Tests;

public sealed class RecentAddressTests
{
    [Fact]
    public void TheLabelShowsThePort_OnlyWhenOneWasGiven()
    {
        new RecentAddress("192.168.1.42", 8009).Label.ShouldBe("192.168.1.42:8009");
        new RecentAddress("192.168.1.42", null).Label.ShouldBe("192.168.1.42");
    }

    [Fact]
    public void TheLabelIsNotSaved()
    {
        // The saved history holds what was typed; how it is shown is worked out when it is shown.
        var text = JsonSerializer.Serialize(new RecentAddress("192.168.1.42", 8009));

        text.ShouldNotContain("Label");
        JsonSerializer.Deserialize<RecentAddress>(text).ShouldBe(new RecentAddress("192.168.1.42", 8009));
    }
}
