using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ShadowLink.Core.Contracts;
using ShadowLink.Core.Models;
using ShadowLink.Infrastructure.Serialization;
using ShadowLink.Services;

namespace ShadowLink.Infrastructure.Discovery;

public sealed class LanDeviceDiscoveryService : IDeviceDiscoveryService
{
    private static readonly IPAddress DiscoveryIpv6MulticastAddress = IPAddress.Parse("ff02::1");
    private readonly ISessionCoordinator _sessionCoordinator;
    private readonly Dictionary<String, DiscoveryDevice> _devices;
    private readonly SemaphoreSlim _lifecycleLock;
    private UdpClient? _ipv4Listener;
    private UdpClient? _ipv6Listener;
    private UdpClient? _ipv4Broadcaster;
    private UdpClient? _ipv6Broadcaster;
    private CancellationTokenSource? _lifecycleCts;
    private Task? _ipv4ReceiveLoopTask;
    private Task? _ipv6ReceiveLoopTask;
    private Task? _announceLoopTask;
    private AppSettings _settings;

    public LanDeviceDiscoveryService(ISessionCoordinator sessionCoordinator)
    {
        _sessionCoordinator = sessionCoordinator;
        _devices = new Dictionary<String, DiscoveryDevice>(StringComparer.OrdinalIgnoreCase);
        _lifecycleLock = new SemaphoreSlim(1, 1);
        _settings = AppSettings.CreateDefault();
    }

    public event EventHandler? DevicesChanged;

    public IReadOnlyList<DiscoveryDevice> CurrentDevices
    {
        get
        {
            lock (_devices)
            {
                return _devices.Values.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }
    }

    public async Task StartAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        await RestartAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    public async Task RestartAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _settings = settings;
            await StopInternalAsync().ConfigureAwait(false);
            _lifecycleCts = new CancellationTokenSource();
            _ipv4Listener = CreateIpv4Listener(settings.DiscoveryPort);
            _ipv6Listener = CreateIpv6Listener(settings.DiscoveryPort);
            _ipv4Broadcaster = CreateIpv4Broadcaster();
            _ipv6Broadcaster = CreateIpv6Broadcaster();
            _ipv4ReceiveLoopTask = Task.Run(() => ReceiveLoopAsync(_ipv4Listener, _lifecycleCts.Token), _lifecycleCts.Token);
            if (_ipv6Listener is not null)
            {
                _ipv6ReceiveLoopTask = Task.Run(() => ReceiveLoopAsync(_ipv6Listener, _lifecycleCts.Token), _lifecycleCts.Token);
            }
            _announceLoopTask = Task.Run(() => AnnounceLoopAsync(_lifecycleCts.Token), _lifecycleCts.Token);
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await StopInternalAsync().ConfigureAwait(false);
            lock (_devices)
            {
                _devices.Clear();
            }

            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _lifecycleLock.Release();
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        Byte[] payload = BuildAnnouncementPayload();

        if (_ipv4Broadcaster is not null)
        {
            try
            {
                await SendAnnouncementsAsync(_ipv4Broadcaster, GetIpv4BroadcastEndpoints(_settings.DiscoveryPort), payload, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SocketException || ex is ArgumentException || ex is InvalidOperationException)
            {
            }
        }

        if (_ipv6Broadcaster is not null)
        {
            try
            {
                await SendAnnouncementsAsync(_ipv6Broadcaster, GetIpv6DiscoveryEndpoints(_settings.DiscoveryPort), payload, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SocketException || ex is ArgumentException || ex is InvalidOperationException)
            {
            }
        }

        CleanupStaleDevices();
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);

        try
        {
            await StopInternalAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
            _lifecycleLock.Dispose();
        }
    }

    private static UdpClient CreateIpv4Listener(Int32 port)
    {
        Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.Bind(new IPEndPoint(IPAddress.Any, port));
        return new UdpClient { Client = socket };
    }

    private static UdpClient? CreateIpv6Listener(Int32 port)
    {
        try
        {
            Socket socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(IPAddress.IPv6Any, port));

            foreach (Int32 interfaceIndex in GetIpv6InterfaceIndices())
            {
                try
                {
                    socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.AddMembership, new IPv6MulticastOption(DiscoveryIpv6MulticastAddress, interfaceIndex));
                }
                catch (Exception ex) when (ex is SocketException || ex is ArgumentException)
                {
                    Trace.TraceWarning("Skipping IPv6 discovery membership on interface {0}: {1}", interfaceIndex, ex.Message);
                }
            }

            return new UdpClient { Client = socket };
        }
        catch (Exception ex) when (ex is SocketException || ex is ArgumentException)
        {
            Trace.TraceWarning("IPv6 discovery listener could not start: {0}", ex.Message);
            return null;
        }
    }

    private static UdpClient CreateIpv4Broadcaster()
    {
        Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.Bind(new IPEndPoint(IPAddress.Any, 0));
        UdpClient broadcaster = new UdpClient { Client = socket };
        broadcaster.EnableBroadcast = true;
        return broadcaster;
    }

    private static UdpClient? CreateIpv6Broadcaster()
    {
        try
        {
            Socket socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
            socket.DualMode = false;
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
            return new UdpClient { Client = socket };
        }
        catch (Exception ex) when (ex is SocketException || ex is ArgumentException)
        {
            Trace.TraceWarning("IPv6 discovery broadcaster could not start: {0}", ex.Message);
            return null;
        }
    }

    private async Task AnnounceLoopAsync(CancellationToken cancellationToken)
    {
        PeriodicTimer periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _settings.AutoRefreshIntervalSeconds)));

        try
        {
            while (await periodicTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await RefreshAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is SocketException || ex is ArgumentException || ex is InvalidOperationException)
                {
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            periodicTimer.Dispose();
        }
    }

    private async Task ReceiveLoopAsync(UdpClient? listener, CancellationToken cancellationToken)
    {
        if (listener is null)
        {
            return;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                UdpReceiveResult result = await listener.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                HandlePacket(result);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException)
        {
        }
    }

    private void HandlePacket(UdpReceiveResult result)
    {
        DiscoveryAnnouncement? announcement;

        try
        {
            String json = Encoding.UTF8.GetString(result.Buffer);
            announcement = JsonSerializer.Deserialize(json, ShadowLinkJsonSerializerContext.Default.DiscoveryAnnouncement);
        }
        catch (Exception ex) when (ex is JsonException || ex is ArgumentException)
        {
            Trace.TraceWarning("Ignoring malformed discovery announcement: {0}", ex.Message);
            return;
        }

        if (announcement is null)
        {
            return;
        }

        if (announcement.MachineId.Equals(_settings.MachineId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        List<DiscoveryNetworkEndpoint> networkEndpoints = announcement.NetworkEndpoints?.ToList() ?? new List<DiscoveryNetworkEndpoint>();
        String sourceAddress = FormatAddress(result.RemoteEndPoint.Address);
        if (!String.IsNullOrWhiteSpace(sourceAddress) &&
            !networkEndpoints.Any(item => item.Address.Equals(sourceAddress, StringComparison.OrdinalIgnoreCase)))
        {
            networkEndpoints.Insert(0, new DiscoveryNetworkEndpoint
            {
                Address = sourceAddress,
                InterfaceName = "Detected path",
                InterfaceDescription = "Announcement source",
                LinkSpeedMbps = 0,
                IsUsbTransport = false,
                IsThunderboltTransport = false
            });
        }

        DiscoveryDevice device = new DiscoveryDevice
        {
            MachineId = announcement.MachineId,
            DisplayName = String.IsNullOrWhiteSpace(announcement.DisplayName) ? announcement.HostName : announcement.DisplayName,
            HostName = announcement.HostName,
            OperatingSystem = announcement.OperatingSystem,
            PlatformFamily = announcement.PlatformFamily,
            NetworkAddress = sourceAddress,
            DiscoveryPort = announcement.DiscoveryPort,
            ControlPort = announcement.ControlPort,
            SupportsKeyboardRelay = announcement.SupportsKeyboardRelay,
            SupportsMouseRelay = announcement.SupportsMouseRelay,
            SupportsDesktopCapture = announcement.SupportsDesktopCapture,
            SupportsUsbNetworking = announcement.SupportsUsbNetworking,
            AcceptsIncomingSessions = announcement.AcceptsIncomingSessions,
            PreferredTransport = announcement.PreferredTransport,
            NetworkEndpoints = networkEndpoints,
            LastSeenUtc = DateTimeOffset.UtcNow
        };

        lock (_devices)
        {
            _devices[device.MachineId] = device;
        }

        CleanupStaleDevices();
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    private Byte[] BuildAnnouncementPayload()
    {
        LocalShareSupport localShareSupport = PlatformEnvironment.GetLocalShareSupport();
        IReadOnlyList<DiscoveryNetworkEndpoint> networkEndpoints = PlatformEnvironment.GetLocalNetworkEndpoints();
        SessionStateSnapshot sessionState = _sessionCoordinator.CurrentState;
        Boolean acceptsIncomingSessions = sessionState.IsListening && sessionState.ActiveDirection == ConnectionDirection.Send;
        DiscoveryAnnouncement announcement = new DiscoveryAnnouncement
        {
            MachineId = _settings.MachineId,
            DisplayName = _settings.DisplayName,
            HostName = Environment.MachineName,
            OperatingSystem = RuntimeInformation.OSDescription,
            PlatformFamily = localShareSupport.PlatformFamily,
            DiscoveryPort = _settings.DiscoveryPort,
            ControlPort = _settings.ControlPort,
            SupportsKeyboardRelay = localShareSupport.SupportsRemoteInputInjection && _settings.EnableKeyboardRelay,
            SupportsMouseRelay = localShareSupport.SupportsRemoteInputInjection && _settings.EnableMouseRelay,
            SupportsDesktopCapture = localShareSupport.IsSupported,
            SupportsUsbNetworking = networkEndpoints.Any(item => item.IsThunderboltTransport),
            AcceptsIncomingSessions = acceptsIncomingSessions,
            PreferredTransport = _settings.PreferredTransport,
            NetworkEndpoints = networkEndpoints.ToList()
        };

        String json = JsonSerializer.Serialize(announcement, ShadowLinkJsonSerializerContext.Default.DiscoveryAnnouncement);
        return Encoding.UTF8.GetBytes(json);
    }

    private IEnumerable<IPEndPoint> GetIpv4BroadcastEndpoints(Int32 port)
    {
        HashSet<String> addresses = new HashSet<String>(StringComparer.OrdinalIgnoreCase)
        {
            IPAddress.Broadcast.ToString()
        };

        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            IPInterfaceProperties properties = networkInterface.GetIPProperties();
            foreach (UnicastIPAddressInformation unicastAddress in properties.UnicastAddresses)
            {
                if (unicastAddress.Address.AddressFamily != AddressFamily.InterNetwork || unicastAddress.IPv4Mask is null)
                {
                    continue;
                }

                Byte[] addressBytes = unicastAddress.Address.GetAddressBytes();
                Byte[] maskBytes = unicastAddress.IPv4Mask.GetAddressBytes();
                Byte[] broadcastBytes = new Byte[4];

                for (Int32 index = 0; index < 4; index++)
                {
                    broadcastBytes[index] = (Byte)(addressBytes[index] | (~maskBytes[index]));
                }

                addresses.Add(new IPAddress(broadcastBytes).ToString());
            }
        }

        return addresses.Select(item => new IPEndPoint(IPAddress.Parse(item), port)).ToList();
    }

    private static IEnumerable<IPEndPoint> GetIpv6DiscoveryEndpoints(Int32 port)
    {
        List<IPEndPoint> endpoints = new List<IPEndPoint>();
        foreach (Int32 interfaceIndex in GetIpv6InterfaceIndices())
        {
            try
            {
                endpoints.Add(new IPEndPoint(new IPAddress(DiscoveryIpv6MulticastAddress.GetAddressBytes(), interfaceIndex), port));
            }
            catch (ArgumentException ex)
            {
                Trace.TraceWarning("Skipping IPv6 discovery endpoint for interface {0}: {1}", interfaceIndex, ex.Message);
            }
        }

        return endpoints;
    }

    private static async Task SendAnnouncementsAsync(UdpClient broadcaster, IEnumerable<IPEndPoint> endpoints, Byte[] payload, CancellationToken cancellationToken)
    {
        foreach (IPEndPoint endpoint in endpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (broadcaster.Client.AddressFamily != endpoint.AddressFamily)
            {
                continue;
            }

            try
            {
                await broadcaster.SendAsync(payload, payload.Length, endpoint).ConfigureAwait(false);
            }
            catch (ArgumentException)
            {
            }
            catch (SocketException)
            {
            }
        }
    }

    private static IReadOnlyList<Int32> GetIpv6InterfaceIndices()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(item => item.OperationalStatus == OperationalStatus.Up && item.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Where(item => item.GetIPProperties().UnicastAddresses.Any(address => address.Address.AddressFamily == AddressFamily.InterNetworkV6))
            .Select(item => item.GetIPProperties().GetIPv6Properties()?.Index ?? -1)
            .Where(item => item > 0)
            .Distinct()
            .ToList();
    }

    private static String FormatAddress(IPAddress address)
    {
        String formatted = address.ToString();
        return address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0 && !formatted.Contains('%')
            ? formatted + "%" + address.ScopeId
            : formatted;
    }

    private void CleanupStaleDevices()
    {
        Boolean changed = false;
        DateTimeOffset threshold = DateTimeOffset.UtcNow.AddSeconds(-10);

        lock (_devices)
        {
            List<String> staleKeys = _devices
                .Where(item => item.Value.LastSeenUtc < threshold)
                .Select(item => item.Key)
                .ToList();

            foreach (String staleKey in staleKeys)
            {
                _devices.Remove(staleKey);
                changed = true;
            }
        }

        if (changed)
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task StopInternalAsync()
    {
        CancellationTokenSource? lifecycleCts = _lifecycleCts;
        _lifecycleCts = null;
        lifecycleCts?.Cancel();

        if (_ipv4Listener is not null)
        {
            _ipv4Listener.Dispose();
            _ipv4Listener = null;
        }

        if (_ipv6Listener is not null)
        {
            _ipv6Listener.Dispose();
            _ipv6Listener = null;
        }

        if (_ipv4Broadcaster is not null)
        {
            _ipv4Broadcaster.Dispose();
            _ipv4Broadcaster = null;
        }

        if (_ipv6Broadcaster is not null)
        {
            _ipv6Broadcaster.Dispose();
            _ipv6Broadcaster = null;
        }

        if (_ipv4ReceiveLoopTask is not null)
        {
            try
            {
                await _ipv4ReceiveLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
            }

            _ipv4ReceiveLoopTask = null;
        }

        if (_ipv6ReceiveLoopTask is not null)
        {
            try
            {
                await _ipv6ReceiveLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
            }

            _ipv6ReceiveLoopTask = null;
        }

        if (_announceLoopTask is not null)
        {
            try
            {
                await _announceLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
            }

            _announceLoopTask = null;
        }

        lifecycleCts?.Dispose();

        lock (_devices)
        {
            _devices.Clear();
        }

        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }
}
