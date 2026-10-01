using System.Text;
using Shouldly;

namespace Flint.Engine.Interop.Tests;

/// <summary>Reading the engine's version out of the buffer it fills.</summary>
public sealed class EngineVersionTests
{
    [Fact]
    public void AVersionEndsAtItsTerminator()
    {
        var buffer = new byte[32];
        Encoding.UTF8.GetBytes("0.1.0").CopyTo(buffer, 0);

        NativeEngineProbeApi.ToVersion(buffer).ShouldBe("0.1.0");
    }

    [Fact]
    public void AnEmptyVersion_IsNoVersion() =>
        NativeEngineProbeApi.ToVersion(new byte[32]).ShouldBeNull();

    [Fact]
    public void ABufferTheEngineNeverTerminated_IsNoVersion() =>
        NativeEngineProbeApi.ToVersion(Encoding.UTF8.GetBytes("0.1.0-and-more")).ShouldBeNull();

    [Fact]
    public void WithoutTheEngine_TheVersionIsUnknownRatherThanAnError()
    {
        // The test run loads no native engine on a machine without one; the answer is null, not a throw.
        var version = EngineVersion.Read();

        (version is null || version.Length > 0).ShouldBeTrue();
    }
}
