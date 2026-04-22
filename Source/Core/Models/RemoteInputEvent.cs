using System;

namespace ShadowLink.Core.Models;

public sealed class RemoteInputEvent
{
    public RemoteInputEventKind Kind { get; set; }

    public String DisplayId { get; set; } = String.Empty;

    public Double X { get; set; }

    public Double Y { get; set; }

    public String Button { get; set; } = String.Empty;

    public Int32 WheelDeltaX { get; set; }

    public Int32 WheelDeltaY { get; set; }

    public String Key { get; set; } = String.Empty;
}
