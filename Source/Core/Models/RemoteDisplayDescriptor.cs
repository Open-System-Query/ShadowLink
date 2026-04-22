using System;

namespace ShadowLink.Core.Models;

public sealed class RemoteDisplayDescriptor
{
    public String DisplayId { get; set; } = String.Empty;

    public String Name { get; set; } = String.Empty;

    public Int32 Left { get; set; }

    public Int32 Top { get; set; }

    public Int32 Width { get; set; }

    public Int32 Height { get; set; }
}
