using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using ShadowLink.Localization;
using ShadowLink.Core.Models;

namespace ShadowLink.Services;

internal static class PlatformEnvironment
{
    private static readonly String[] ThunderboltKeywords =
    {
        "thunderbolt",
        "thunderbolt bridge",
        "thunderbolt(tm)",
        "thunderbolt(tm) networking",
        "usb4",
        "usb 4"
    };

    private static readonly String[] UsbKeywords =
    {
        "usb",
        "type-c",
        "ncm",
        "cdc_ether",
        "cdc ncm",
        "gadget",
        "tether"
    };

    public static PlatformFamily DetectPlatformFamily()
    {
        if (OperatingSystem.IsWindows())
        {
            return PlatformFamily.Windows;
        }

        if (OperatingSystem.IsMacOS())
        {
            return PlatformFamily.MacOS;
        }

        if (OperatingSystem.IsLinux())
        {
            return PlatformFamily.Linux;
        }

        return PlatformFamily.Unknown;
    }

    public static LocalShareSupport GetLocalShareSupport()
    {
        if (OperatingSystem.IsWindows())
        {
            return new LocalShareSupport
            {
                IsSupported = true,
                SupportsRemoteInputInjection = true,
                PlatformFamily = PlatformFamily.Windows,
                Detail = ShadowLinkText.Translate("share_support.windows")
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            Boolean hasScreenRecording = HasMacScreenRecordingPermission();
            Boolean hasAccessibility = HasMacAccessibilityPermission();
            String detail = hasScreenRecording && hasAccessibility
                ? ShadowLinkText.Translate("share_support.macos.ready")
                : hasScreenRecording
                    ? ShadowLinkText.Translate("share_support.macos.accessibility_needed")
                    : hasAccessibility
                        ? ShadowLinkText.Translate("share_support.macos.screen_recording_needed")
                        : ShadowLinkText.Translate("share_support.macos.permissions_needed");
            return new LocalShareSupport
            {
                IsSupported = hasScreenRecording,
                SupportsRemoteInputInjection = hasAccessibility,
                PlatformFamily = PlatformFamily.MacOS,
                Detail = detail
            };
        }

        if (OperatingSystem.IsLinux())
        {
            String? x11Display = Environment.GetEnvironmentVariable("DISPLAY");
            String? waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
            if (!String.IsNullOrWhiteSpace(x11Display))
            {
                return new LocalShareSupport
                {
                    IsSupported = true,
                    SupportsRemoteInputInjection = true,
                    PlatformFamily = PlatformFamily.Linux,
                    Detail = ShadowLinkText.Translate("share_support.linux.x11")
                };
            }

            return new LocalShareSupport
            {
                IsSupported = false,
                SupportsRemoteInputInjection = false,
                PlatformFamily = PlatformFamily.Linux,
                Detail = !String.IsNullOrWhiteSpace(waylandDisplay)
                    ? ShadowLinkText.Translate("share_support.linux.wayland")
                    : ShadowLinkText.Translate("share_support.linux.no_x11")
            };
        }

        return new LocalShareSupport
        {
            IsSupported = false,
            SupportsRemoteInputInjection = false,
            PlatformFamily = PlatformFamily.Unknown,
            Detail = ShadowLinkText.Translate("share_support.unknown")
        };
    }

    public static IReadOnlyList<DiscoveryNetworkEndpoint> GetLocalNetworkEndpoints()
    {
        Dictionary<String, DiscoveryNetworkEndpoint> endpoints = new Dictionary<String, DiscoveryNetworkEndpoint>(StringComparer.OrdinalIgnoreCase);

        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            IPInterfaceProperties properties = networkInterface.GetIPProperties();
            foreach (UnicastIPAddressInformation unicastAddress in properties.UnicastAddresses)
            {
                if (unicastAddress.Address.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
                {
                    continue;
                }

                if (IPAddress.IsLoopback(unicastAddress.Address) ||
                    (unicastAddress.Address.AddressFamily == AddressFamily.InterNetworkV6 && unicastAddress.Address.IsIPv6Multicast))
                {
                    continue;
                }

                String address = FormatAddress(unicastAddress.Address);
                if (String.IsNullOrWhiteSpace(address))
                {
                    continue;
                }

                Boolean isUsbTransport = IsUsbTransport(networkInterface, properties, unicastAddress.Address);
                Boolean isThunderboltTransport = IsThunderboltTransport(networkInterface);
                Int64 linkSpeedMbps = Math.Max(1L, networkInterface.Speed / 1_000_000L);
                if (!endpoints.TryGetValue(address, out DiscoveryNetworkEndpoint? endpoint) ||
                    ScoreEndpoint(isThunderboltTransport, isUsbTransport, linkSpeedMbps) > ScoreEndpoint(endpoint.IsThunderboltTransport, endpoint.IsUsbTransport, endpoint.LinkSpeedMbps))
                {
                    endpoints[address] = new DiscoveryNetworkEndpoint
                    {
                        Address = address,
                        InterfaceName = networkInterface.Name,
                        InterfaceDescription = networkInterface.Description,
                        LinkSpeedMbps = linkSpeedMbps,
                        IsUsbTransport = isUsbTransport,
                        IsThunderboltTransport = isThunderboltTransport
                    };
                }
            }
        }

        return endpoints.Values
            .OrderByDescending(item => ScoreEndpoint(item.IsThunderboltTransport, item.IsUsbTransport, item.LinkSpeedMbps))
            .ThenBy(item => item.Address, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static DirectCableStatus GetThunderboltDirectStatus()
    {
        IReadOnlyList<DiscoveryNetworkEndpoint> endpoints = GetLocalNetworkEndpoints()
            .Where(item => item.IsThunderboltTransport)
            .OrderByDescending(item => item.LinkSpeedMbps)
            .ThenBy(item => item.InterfaceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        String[] compatibleInterfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(IsThunderboltTransport)
            .Select(item => item.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Boolean hasLink = NetworkInterface.GetAllNetworkInterfaces()
            .Any(item => item.OperationalStatus == OperationalStatus.Up && IsThunderboltTransport(item));

        return new DirectCableStatus
        {
            HasCompatibleInterface = compatibleInterfaces.Length > 0,
            HasLink = hasLink,
            HasUsableNetworkPath = endpoints.Count > 0,
            InterfaceNames = compatibleInterfaces,
            Endpoints = endpoints
        };
    }

    public static String BuildTransportSummary(DiscoveryNetworkEndpoint endpoint)
    {
        String transport = endpoint.IsThunderboltTransport
            ? ShadowLinkText.Translate("transport.tb_usb4")
            : endpoint.IsUsbTransport
                ? ShadowLinkText.Translate("transport.usb_network")
                : ShadowLinkText.Translate("transport.network");
        return endpoint.LinkSpeedMbps > 0
            ? ShadowLinkText.TranslateFormat("transport.summary.speed", transport, endpoint.LinkSpeedMbps, endpoint.InterfaceName)
            : ShadowLinkText.TranslateFormat("transport.summary.interface", transport, endpoint.InterfaceName);
    }

    private static Int64 ScoreEndpoint(Boolean isThunderboltTransport, Boolean isUsbTransport, Int64 linkSpeedMbps)
    {
        return (isThunderboltTransport ? 2_000_000L : 0L) +
               (isUsbTransport ? 1L : 0L) +
               linkSpeedMbps;
    }

    private static Boolean IsUsbTransport(NetworkInterface networkInterface, IPInterfaceProperties properties, IPAddress address)
    {
        String value = (networkInterface.Name + " " + networkInterface.Description).ToLowerInvariant();
        return UsbKeywords.Any(value.Contains) || IsLikelyDirectLink(networkInterface, properties, address);
    }

    private static Boolean IsThunderboltTransport(NetworkInterface networkInterface)
    {
        String value = (networkInterface.Name + " " + networkInterface.Description).ToLowerInvariant();
        return ThunderboltKeywords.Any(value.Contains);
    }

    private static Boolean IsLikelyDirectLink(NetworkInterface networkInterface, IPInterfaceProperties properties, IPAddress address)
    {
        if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
        {
            return false;
        }

        Boolean hasGateway = properties.GatewayAddresses.Any(item =>
            item.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6 &&
            !IPAddress.Any.Equals(item.Address) &&
            !IPAddress.IPv6Any.Equals(item.Address) &&
            !IPAddress.None.Equals(item.Address) &&
            !IPAddress.IPv6None.Equals(item.Address));
        if (hasGateway)
        {
            return false;
        }

        Boolean isLinkLocal = address.AddressFamily switch
        {
            AddressFamily.InterNetwork => address.ToString().StartsWith("169.254.", StringComparison.OrdinalIgnoreCase),
            AddressFamily.InterNetworkV6 => address.IsIPv6LinkLocal,
            _ => false
        };

        return isLinkLocal && networkInterface.Speed >= 1_000_000_000L;
    }

    private static String FormatAddress(IPAddress address)
    {
        String formatted = address.ToString();
        if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0 && !formatted.Contains('%'))
        {
            return formatted + "%" + address.ScopeId;
        }

        return formatted;
    }

    private static Boolean HasMacScreenRecordingPermission()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        try
        {
            return CGPreflightScreenCaptureAccess();
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static Boolean HasMacAccessibilityPermission()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        try
        {
            return AXIsProcessTrusted();
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern Boolean CGPreflightScreenCaptureAccess();

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern Boolean AXIsProcessTrusted();
}
