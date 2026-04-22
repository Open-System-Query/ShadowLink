using System;
using ShadowLink.Core.Models;

namespace ShadowLink.Infrastructure.Serialization;

internal sealed class SessionHelloMessage
{
    public String MachineId { get; set; } = String.Empty;

    public String DisplayName { get; set; } = String.Empty;

    public String OperatingSystem { get; set; } = String.Empty;

    public PlatformFamily PlatformFamily { get; set; }

    public ConnectionDirection Direction { get; set; }

    public String SessionPassphrase { get; set; } = String.Empty;

    public Boolean SupportsKeyboardRelay { get; set; }

    public Boolean SupportsMouseRelay { get; set; }

    public Int32 RequestedStreamWidth { get; set; }

    public Int32 RequestedStreamHeight { get; set; }

    public Int32 RequestedStreamFrameRate { get; set; }

    public StreamColorMode RequestedStreamColorMode { get; set; }

    public Int32 RequestedStreamTileSize { get; set; }

    public Int32 RequestedStreamDictionarySizeMb { get; set; }

    public Int32 RequestedStreamStaticCodebookSharePercent { get; set; }
}
