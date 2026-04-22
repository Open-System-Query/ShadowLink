using System;
using ShadowLink.Core.Models;

namespace ShadowLink.Infrastructure.Serialization;

internal sealed class SessionHelloResponse
{
    public Boolean Accepted { get; set; }

    public String Message { get; set; } = String.Empty;

    public String ResponderDisplayName { get; set; } = String.Empty;

    public String ResponderOperatingSystem { get; set; } = String.Empty;

    public PlatformFamily ResponderPlatformFamily { get; set; }

    public Boolean RequiresPassphrase { get; set; }
}
