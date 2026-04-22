using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ShadowLink.Core.Models;

namespace ShadowLink.Core.Contracts;

public interface IRemoteSessionWindowManager
{
    event EventHandler? AllDisplaysClosed;

    void ShowDisplays(IReadOnlyList<RemoteDisplayDescriptor> displays, String releaseGesture, Func<RemoteInputEvent, Task> inputSink, ConnectionDirection direction, RemoteDisplayScaleMode displayScaleMode);

    void UpdateFrame(String displayId, Byte[] framePixels, Int32 frameWidth, Int32 frameHeight, Int32 frameStride);

    void CloseAll();
}
