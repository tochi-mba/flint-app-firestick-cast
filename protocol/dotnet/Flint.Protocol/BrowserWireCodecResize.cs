namespace Flint.Protocol;

internal static partial class BrowserWireCodec
{
    private static void ValidateGeometry(long epoch, long revision, int column, int row)
    {
        if (epoch <= 0 || revision <= 0 || column is < 1500 or > 8500 || row is < 1500 or > 8500)
            throw new WireFormatException("Invalid workspace geometry.");
    }

    private static BrowserWorkspaceResizeMessage DecodeResize(ref WireCodec.PayloadReader reader)
    {
        var message = new BrowserWorkspaceResizeMessage(reader.Int64("epoch"), reader.Int64("command ID"),
            reader.Int64("revision"), reader.UInt16("column split"), reader.UInt16("row split"), (byte)reader.UInt8("mode"));
        if (message.Mode > 2) throw new WireFormatException("Invalid workspace mode.");
        ValidateGeometry(message.Epoch, message.ExpectedRevision, message.Column, message.Row);
        if (message.CommandId <= 0) throw new WireFormatException("Resize command ID must be positive.");
        return message;
    }

    private static BrowserWorkspaceGeometryMessage DecodeGeometry(ref WireCodec.PayloadReader reader)
    {
        var message = new BrowserWorkspaceGeometryMessage(reader.Int64("epoch"), reader.Int64("revision"),
            reader.UInt16("column split"), reader.UInt16("row split"), (byte)reader.UInt8("mode"));
        if (message.Mode > 2) throw new WireFormatException("Invalid workspace mode.");
        ValidateGeometry(message.Epoch, message.Revision, message.Column, message.Row);
        return message;
    }
}
