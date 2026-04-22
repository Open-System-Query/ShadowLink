using System;
using System.Linq;
using ShadowLink.Localization;
using ShadowLink.Core.Models;

namespace ShadowLink.Application.ViewModels;

public sealed class DeviceCardViewModel : ObservableObject
{
    private DiscoveryDevice _device;

    public DeviceCardViewModel(DiscoveryDevice device)
    {
        _device = device;
    }

    public DiscoveryDevice Device => _device;

    public String MachineId => _device.MachineId;

    public String DisplayName => _device.DisplayName;

    public String HostName => _device.HostName;

    public String Address => _device.NetworkAddress;

    public String PlatformLabel => _device.OperatingSystem;

    public String TransportLabel => _device.NetworkEndpoints.Any(item => item.IsThunderboltTransport)
        ? ShadowLinkText.Translate("device.transport.direct")
        : ShadowLinkText.Translate("device.transport.network_only");

    public String CapabilitiesLabel => BuildCapabilitiesLabel();

    public String PresenceLabel => ShadowLinkText.TranslateFormat("device.presence", _device.LastSeenUtc.ToLocalTime().ToString("HH:mm:ss"));

    public void Update(DiscoveryDevice device)
    {
        _device = device;
        OnPropertyChanged(nameof(Device));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(HostName));
        OnPropertyChanged(nameof(Address));
        OnPropertyChanged(nameof(PlatformLabel));
        OnPropertyChanged(nameof(TransportLabel));
        OnPropertyChanged(nameof(CapabilitiesLabel));
        OnPropertyChanged(nameof(PresenceLabel));
    }

    private String BuildCapabilitiesLabel()
    {
        String share = _device.AcceptsIncomingSessions
            ? ShadowLinkText.Translate("device.capability.ready")
            : _device.SupportsDesktopCapture
                ? ShadowLinkText.Translate("device.capability.share")
                : ShadowLinkText.Translate("device.capability.no_share");
        String input = _device.SupportsMouseRelay && _device.SupportsKeyboardRelay
            ? ShadowLinkText.Translate("device.capability.mouse_keyboard")
            : _device.SupportsMouseRelay
                ? ShadowLinkText.Translate("device.capability.mouse_only")
                : _device.SupportsKeyboardRelay
                    ? ShadowLinkText.Translate("device.capability.keyboard_only")
                    : ShadowLinkText.Translate("device.capability.no_input");
        return share + " / " + input;
    }
}
