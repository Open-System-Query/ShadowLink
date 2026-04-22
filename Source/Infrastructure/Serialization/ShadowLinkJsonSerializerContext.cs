using System.Text.Json.Serialization;
using ShadowLink.Core.Models;

namespace ShadowLink.Infrastructure.Serialization;

[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(DiscoveryAnnouncement))]
[JsonSerializable(typeof(DiscoveryNetworkEndpoint))]
[JsonSerializable(typeof(PlatformFamily))]
[JsonSerializable(typeof(RemoteDisplayDescriptor))]
[JsonSerializable(typeof(RemoteInputEvent))]
[JsonSerializable(typeof(DisplayManifestPacket))]
[JsonSerializable(typeof(DisplayFramePacket))]
[JsonSerializable(typeof(InputEventPacket))]
[JsonSerializable(typeof(ClipboardPacket))]
[JsonSerializable(typeof(FileTransferPacket))]
[JsonSerializable(typeof(FileTransferPacketKind))]
[JsonSerializable(typeof(StreamColorMode))]
[JsonSerializable(typeof(RemoteDisplayScaleMode))]
[JsonSerializable(typeof(SessionHelloMessage))]
[JsonSerializable(typeof(SessionHelloResponse))]
[JsonSerializable(typeof(SessionStreamMessage))]
internal sealed partial class ShadowLinkJsonSerializerContext : JsonSerializerContext
{
}
