namespace Flint.Protocol;

/// <summary>Sets column and row dividers in ten-thousandths; requires protocol four.</summary>
public sealed record BrowserWorkspaceResizeMessage(long Epoch, long CommandId, long ExpectedRevision,
    int Column, int Row, byte Mode = 0) : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.BrowserWorkspaceResize;
}

/// <summary>Authoritative dividers paired with the workspace snapshot revision.</summary>
public sealed record BrowserWorkspaceGeometryMessage(long Epoch, long Revision, int Column, int Row, byte Mode = 0) : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.BrowserWorkspaceGeometry;
}
