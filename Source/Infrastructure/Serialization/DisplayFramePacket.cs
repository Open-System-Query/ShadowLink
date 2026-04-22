using System;
using ShadowLink.Core.Models;

namespace ShadowLink.Infrastructure.Serialization;

internal sealed class DisplayFramePacket
{
    public String DisplayId { get; set; } = String.Empty;

    public Int32 FrameWidth { get; set; }

    public Int32 FrameHeight { get; set; }

    public Int32 TileSize { get; set; }

    public StreamColorMode ColorMode { get; set; }

    public Int32 DictionarySizeMb { get; set; }

    public Int32 StaticCodebookSharePercent { get; set; }

    public Boolean IsPayloadCompressed { get; set; }
}
