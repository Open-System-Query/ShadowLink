using System;

namespace ShadowLink.Infrastructure.Session;

internal readonly struct TileFrameEncodeResult
{
    public TileFrameEncodeResult(Int32 changedTileCount, Byte[] payload)
    {
        ChangedTileCount = changedTileCount;
        Payload = payload;
    }

    public Int32 ChangedTileCount { get; }

    public Byte[] Payload { get; }
}
