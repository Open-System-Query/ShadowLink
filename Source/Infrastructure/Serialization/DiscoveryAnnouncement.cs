using System;
using System.Collections.Generic;
using ShadowLink.Core.Models;

namespace ShadowLink.Infrastructure.Serialization;

internal sealed class DiscoveryAnnouncement
{
    public String MachineId { get; set; } = String.Empty;

    public String DisplayName { get; set; } = String.Empty;

    public String HostName { get; set; } = String.Empty;

    public String OperatingSystem { get; set; } = String.Empty;

    public PlatformFamily PlatformFamily { get; set; }

    public Int32 DiscoveryPort { get; set; }

    public Int32 ControlPort { get; set; }

    public Boolean SupportsKeyboardRelay { get; set; }

    public Boolean SupportsMouseRelay { get; set; }

    public Boolean SupportsDesktopCapture { get; set; }

    public Boolean SupportsUsbNetworking { get; set; }

    public Boolean AcceptsIncomingSessions { get; set; }

    public TransportPreference PreferredTransport { get; set; }

    public List<DiscoveryNetworkEndpoint> NetworkEndpoints { get; set; } = new List<DiscoveryNetworkEndpoint>();
}
