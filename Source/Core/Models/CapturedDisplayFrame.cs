using System;

namespace ShadowLink.Core.Models;

public sealed class CapturedDisplayFrame
{
    public Int32 Width { get; set; }

    public Int32 Height { get; set; }

    public Int32 Stride { get; set; }

    public Byte[] Pixels { get; set; } = Array.Empty<Byte>();
}
