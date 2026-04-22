using System.Collections.Generic;
using ShadowLink.Core.Models;

namespace ShadowLink.Infrastructure.Serialization;

internal sealed class DisplayManifestPacket
{
    public List<RemoteDisplayDescriptor> Displays { get; set; } = new List<RemoteDisplayDescriptor>();
}
