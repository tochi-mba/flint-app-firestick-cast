using Shouldly;

namespace Flint.Platform.Windows.Tests;

/// <summary>The installer and every portable extraction share one running-instance identity.</summary>
public sealed class FlintInstanceTests
{
    [Fact]
    public async Task ASecondThreadCannotAcquireAnOwnedIdentity()
    {
        var name = $@"Local\Flint-test-{Guid.NewGuid():N}";
        using var first = FlintSingleInstance.TryAcquire(name);

        var second = await Task.Run(() => FlintSingleInstance.TryAcquire(name));

        first.ShouldNotBeNull();
        second.ShouldBeNull();
    }

    [Fact]
    public async Task AReleasedIdentityCanBeAcquiredAgain()
    {
        var name = $@"Local\Flint-test-{Guid.NewGuid():N}";
        FlintSingleInstance.TryAcquire(name).ShouldNotBeNull().Dispose();

        var reacquired = await Task.Run(() =>
        {
            using var next = FlintSingleInstance.TryAcquire(name);
            return next is not null;
        });

        reacquired.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Flint", "REX Technologies", true)]
    [InlineData("Flint", "Somebody else", false)]
    [InlineData("Another product", "REX Technologies", false)]
    [InlineData(null, "REX Technologies", false)]
    public void CleanupRequiresBothProductAndPublisher(
        string? product,
        string? company,
        bool expected)
    {
        FlintProcessCleanup.HasFlintIdentity(product, company).ShouldBe(expected);
    }
}
