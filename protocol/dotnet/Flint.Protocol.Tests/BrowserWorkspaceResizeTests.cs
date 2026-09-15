using Shouldly;

namespace Flint.Protocol.Tests;

public sealed class BrowserWorkspaceResizeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ModeAndGeometryAgreeWithStrictVersionFourRoundtrip(byte mode)
    {
        WireMessage[] messages = [new BrowserWorkspaceResizeMessage(4, 150, 140, 7000, 3000, mode),
            new BrowserWorkspaceGeometryMessage(4, 141, 7000, 3000, mode)];
        foreach (var message in messages)
        {
            var frame = new WireFrame(4, message);
            WireCodec.Decode(WireCodec.Encode(frame)).ShouldBe(frame);
            BrowserWireRules.IsForbiddenOnOrdinaryChannel(message).ShouldBeTrue();
            Should.Throw<WireFormatException>(() => WireCodec.Encode(new WireFrame(3, message)));
        }
    }

    [Theory]
    [InlineData(1499, 5000, 0)]
    [InlineData(5000, 8501, 0)]
    [InlineData(5000, 5000, 3)]
    public void InvalidGeometryOrModeIsRejected(int column, int row, byte mode)
    {
        Should.Throw<WireFormatException>(() => WireCodec.Encode(new WireFrame(4,
            new BrowserWorkspaceResizeMessage(4, 1, 1, column, row, mode))));
    }
}
