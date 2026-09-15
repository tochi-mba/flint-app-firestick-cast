using Shouldly;
using Xunit;

namespace Flint.Hardware.E2E;

public sealed class HardwareGateTests
{
    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("https://example.com")]
    public void DefaultFixtureHasAnExplicitExpectation(string url) =>
        HardwareGate.ResolveExpectedPageText(url, null).ShouldBe("Example Domain");

    [Fact]
    public void CustomFixtureCannotInheritTheWrongAssertion() =>
        Should.Throw<InvalidOperationException>(() => HardwareGate.ResolveExpectedPageText("https://fixture.test", null));

    [Fact]
    public void CustomFixtureUsesItsOwnExpectation() =>
        HardwareGate.ResolveExpectedPageText("https://fixture.test", " Fixture ready ").ShouldBe("Fixture ready");

    [Fact]
    public void OversizedExpectationIsRejected() =>
        Should.Throw<ArgumentException>(() => HardwareGate.ResolveExpectedPageText("https://fixture.test", new string('a', 1025)));
}
