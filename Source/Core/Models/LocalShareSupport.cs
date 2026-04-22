using System;

namespace ShadowLink.Core.Models;

public sealed class LocalShareSupport
{
    public Boolean IsSupported { get; init; }

    public Boolean SupportsRemoteInputInjection { get; init; }

    public PlatformFamily PlatformFamily { get; init; }

    public String Detail { get; init; } = String.Empty;
}
