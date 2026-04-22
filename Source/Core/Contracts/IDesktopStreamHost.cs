using System;
using System.Collections.Generic;
using ShadowLink.Core.Models;

namespace ShadowLink.Core.Contracts;

public interface IDesktopStreamHost
{
    Boolean IsSupported { get; }

    void UpdateSettings(AppSettings settings);

    IReadOnlyList<RemoteDisplayDescriptor> GetDisplays();

    CapturedDisplayFrame CaptureDisplayFrame(String displayId);

    void ApplyInput(RemoteInputEvent inputEvent);
}
