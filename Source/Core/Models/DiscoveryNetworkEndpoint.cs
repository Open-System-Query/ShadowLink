using System;

namespace ShadowLink.Core.Models;

public sealed class DiscoveryNetworkEndpoint
{
    public String Address { get; set; } = String.Empty;

    public String InterfaceName { get; set; } = String.Empty;

    public String InterfaceDescription { get; set; } = String.Empty;

    public Int64 LinkSpeedMbps { get; set; }

    public Boolean IsUsbTransport { get; set; }

    public Boolean IsThunderboltTransport { get; set; }
}
