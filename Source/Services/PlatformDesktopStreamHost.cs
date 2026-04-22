using System;
using System.Collections.Generic;
using ShadowLink.Core.Contracts;
using ShadowLink.Core.Models;

namespace ShadowLink.Services;

public sealed class PlatformDesktopStreamHost : IDesktopStreamHost
{
    private readonly IDesktopStreamHost _implementation;

    public PlatformDesktopStreamHost()
    {
        if (OperatingSystem.IsWindows())
        {
            _implementation = new WindowsDesktopStreamHost();
        }
        else if (OperatingSystem.IsMacOS())
        {
            _implementation = new MacDesktopStreamHost();
        }
        else if (OperatingSystem.IsLinux())
        {
            _implementation = new LinuxDesktopStreamHost();
        }
        else
        {
            _implementation = new UnsupportedDesktopStreamHost();
        }
    }

    public Boolean IsSupported => _implementation.IsSupported;

    public void UpdateSettings(AppSettings settings)
    {
        _implementation.UpdateSettings(settings);
    }

    public IReadOnlyList<RemoteDisplayDescriptor> GetDisplays()
    {
        return _implementation.GetDisplays();
    }

    public CapturedDisplayFrame CaptureDisplayFrame(String displayId)
    {
        return _implementation.CaptureDisplayFrame(displayId);
    }

    public void ApplyInput(RemoteInputEvent inputEvent)
    {
        _implementation.ApplyInput(inputEvent);
    }

    private sealed class UnsupportedDesktopStreamHost : IDesktopStreamHost
    {
        public Boolean IsSupported => false;

        public void UpdateSettings(AppSettings settings)
        {
        }

        public IReadOnlyList<RemoteDisplayDescriptor> GetDisplays()
        {
            return Array.Empty<RemoteDisplayDescriptor>();
        }

        public CapturedDisplayFrame CaptureDisplayFrame(String displayId)
        {
            return new CapturedDisplayFrame();
        }

        public void ApplyInput(RemoteInputEvent inputEvent)
        {
        }
    }
}
