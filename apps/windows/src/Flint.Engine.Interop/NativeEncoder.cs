using System.Runtime.InteropServices;
using Flint.Core;

namespace Flint.Engine.Interop;

/// <summary>Blittable mirror of <c>FlintEncoder</c> in the Rust ABI.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeEncoder
{
    internal byte Vendor;

    private byte _reserved0;
    private byte _reserved1;
    private byte _reserved2;
    private byte _reserved3;
    private byte _reserved4;
    private byte _reserved5;
    private byte _reserved6;

    internal long AdapterLuid;
    internal uint CodecMask;

    private uint _reserved7;

    internal HostVideoEncoder? ToModel()
    {
        var codecs = new HashSet<VideoCodec>();
        if ((CodecMask & NativeEngineProbeApi.CodecMaskH264) != 0)
        {
            codecs.Add(VideoCodec.H264);
        }

        if ((CodecMask & NativeEngineProbeApi.CodecMaskH265) != 0)
        {
            codecs.Add(VideoCodec.H265);
        }

        if ((CodecMask & NativeEngineProbeApi.CodecMaskAv1) != 0)
        {
            codecs.Add(VideoCodec.Av1);
        }

        if (codecs.Count == 0)
        {
            return null;
        }

        var vendor = Vendor switch
        {
            0 => EncoderVendor.Nvenc,
            1 => EncoderVendor.Amf,
            2 => EncoderVendor.QuickSync,
            _ => EncoderVendor.Unknown,
        };
        return new HostVideoEncoder(vendor, AdapterLuid, codecs);
    }
}
