using System;

namespace ShadowLink.Core.Models;

public sealed class FirewallConfigurationStatus
{
    public static FirewallConfigurationStatus Hidden { get; } = new FirewallConfigurationStatus
    {
        IsVisible = false,
        IsSupported = false,
        IsReady = true,
        Title = String.Empty,
        Detail = String.Empty,
        ActionLabel = String.Empty
    };

    public Boolean IsVisible { get; init; }

    public Boolean IsSupported { get; init; }

    public Boolean IsReady { get; init; }

    public String Title { get; init; } = String.Empty;

    public String Detail { get; init; } = String.Empty;

    public String ActionLabel { get; init; } = String.Empty;
}
