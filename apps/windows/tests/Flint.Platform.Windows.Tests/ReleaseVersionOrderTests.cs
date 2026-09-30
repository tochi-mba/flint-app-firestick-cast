using Shouldly;
using Velopack;

namespace Flint.Platform.Windows.Tests;

/// <summary>
/// The package versions the release workflow stamps, ordered by the comparer that decides updates.
/// </summary>
/// <remarks>
/// tools/dev/release.ps1 writes rolling builds as <c>MAJOR.MINOR.PATCH-rolling.RUN.gHASH</c>, and its
/// Pester tests pin that shape. Whether an installed copy ever updates depends on how Velopack
/// orders those strings, so they are compared here with Velopack's own version type rather than
/// any other implementation of the same rules.
/// </remarks>
public sealed class ReleaseVersionOrderTests
{
    [Theory]
    [InlineData("0.1.0-rolling.9.gfffffff", "0.1.0-rolling.10.g0000000")]
    [InlineData("0.1.0-rolling.99.gabcdef0", "0.1.0-rolling.100.g1234567")]
    [InlineData("0.1.0-rolling.41.g0000000", "0.1.0-rolling.42.gfffffff")]
    public void ALaterRun_IsALaterVersionWhateverItsHash(string older, string newer)
    {
        SemanticVersion.Parse(older).ShouldBeLessThan(SemanticVersion.Parse(newer));
    }

    [Theory]
    [InlineData("0.1.0-gfffffff")]
    [InlineData("0.1.0-g0123abc")]
    public void EveryBuildFromBeforeRunNumbers_StillUpdatesToTheFirstRollingBuild(string hashOnly)
    {
        SemanticVersion.Parse(hashOnly).ShouldBeLessThan(SemanticVersion.Parse("0.1.0-rolling.1.g0000000"));
    }

    [Fact]
    public void ARelease_IsLaterThanEveryRollingBuildOfItAndEarlierThanTheNextVersionsBuilds()
    {
        var release = SemanticVersion.Parse("0.1.0");

        SemanticVersion.Parse("0.1.0-rolling.500.gabcdef0").ShouldBeLessThan(release);
        release.ShouldBeLessThan(SemanticVersion.Parse("0.1.1-rolling.1.gabcdef0"));
    }
}
