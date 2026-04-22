using System;
using System.Collections.Generic;
using ShadowLink.Core.Models;

namespace ShadowLink.Infrastructure.Serialization;

internal sealed class SessionStreamMessage
{
    public SessionStreamMessageType MessageType { get; set; }

    public List<RemoteDisplayDescriptor>? Displays { get; set; }

    public String DisplayId { get; set; } = String.Empty;

    public String FrameData { get; set; } = String.Empty;

    public Boolean IsCompressed { get; set; }

    public RemoteInputEvent? InputEvent { get; set; }
}
