using ShadowLink.Core.Models;

namespace ShadowLink.Infrastructure.Serialization;

internal sealed class InputEventPacket
{
    public RemoteInputEvent InputEvent { get; set; } = new RemoteInputEvent();
}
