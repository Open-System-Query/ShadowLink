using System;
using System.Collections.Generic;

namespace ShadowLink.Core.Models;

public sealed class DirectCableStatus
{
    public Boolean HasCompatibleInterface { get; init; }

    public Boolean HasLink { get; init; }

    public Boolean HasUsableNetworkPath { get; init; }

    public IReadOnlyList<String> InterfaceNames { get; init; } = Array.Empty<String>();

    public IReadOnlyList<DiscoveryNetworkEndpoint> Endpoints { get; init; } = Array.Empty<DiscoveryNetworkEndpoint>();
}
