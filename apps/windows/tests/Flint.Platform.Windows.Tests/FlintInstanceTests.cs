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

    [Fact]
    public void DisposingTwice_IsHarmless()
    {
        var instance = FlintSingleInstance.TryAcquire($@"Local\Flint-test-{Guid.NewGuid():N}").ShouldNotBeNull();

        instance.Dispose();

        Should.NotThrow(instance.Dispose);
    }

    [Fact]
    public void ABlankName_IsRefused()
    {
        Should.Throw<ArgumentException>(() => FlintSingleInstance.TryAcquire(" "));
    }

    [Fact]
    public void TheRealIdentity_IsExclusiveWhileHeld()
    {
        // The shell's own name. A Flint already running on this machine holds it, in which case both
        // attempts are refused; otherwise the first is granted and the second refused. Either way a
        // second copy never gets it.
        using var first = FlintSingleInstance.TryAcquire();

        using var second = FlintSingleInstance.TryAcquire();

        second.ShouldBeNull();
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
