using System;
using System.Collections.Generic;
using ShadowLink.Localization;

namespace ShadowLink.Core.Models;

public sealed class SessionStateSnapshot
{
    public String StatusTitle { get; init; } = ShadowLinkText.Translate("status.ready");

    public String StatusDetail { get; init; } = ShadowLinkText.Translate("status.scanning_peers");

    public String ListenerSummary { get; init; } = ShadowLinkText.Translate("status.offline");

    public String ActivePeerDisplayName { get; init; } = ShadowLinkText.Translate("status.no_active_peer");

    public String ActivePeerAddress { get; init; } = ShadowLinkText.Translate("status.select_device");

    public String ActiveTransportSummary { get; init; } = ShadowLinkText.Translate("status.auto");

    public PlatformFamily ActivePeerPlatformFamily { get; init; }

    public String ActivePeerOperatingSystem { get; init; } = String.Empty;

    public ConnectionDirection ActiveDirection { get; init; } = ConnectionDirection.Receive;

    public Boolean IsListening { get; init; }

    public Boolean IsConnected { get; init; }

    public Boolean RequiresPassphrase { get; init; }

    public Boolean CanShareLocalDesktop { get; init; }

    public String ShareSupportDetail { get; init; } = String.Empty;

    public IReadOnlyList<ActivityEntry> ActivityEntries { get; init; } = Array.Empty<ActivityEntry>();
}
