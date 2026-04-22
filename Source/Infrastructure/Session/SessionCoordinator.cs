using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ShadowLink.Core.Contracts;
using ShadowLink.Core.Models;
using ShadowLink.Infrastructure.Serialization;
using ShadowLink.Localization;
using ShadowLink.Services;

namespace ShadowLink.Infrastructure.Session;

public sealed class SessionCoordinator : ISessionCoordinator
{
    private const Int32 ClipboardPollIntervalMilliseconds = 600;
    private const Int32 FileTransferChunkSize = 512 * 1024;
    private readonly IDesktopStreamHost _desktopStreamHost;
    private readonly IRemoteSessionWindowManager _remoteSessionWindowManager;
    private readonly IAppInteractionService _appInteractionService;
    private readonly SemaphoreSlim _lifecycleLock;
    private readonly SemaphoreSlim _sendLock;
    private readonly List<ActivityEntry> _activityEntries;
    private readonly Dictionary<Guid, IncomingFileTransferState> _incomingFileTransfers;
    private CancellationTokenSource? _listenerCts;
    private CancellationTokenSource? _activeSessionCts;
    private TcpListener? _listener;
    private Task? _acceptLoopTask;
    private TcpClient? _activeClient;
    private NetworkStream? _activeStream;
    private Task? _activeReceiveLoopTask;
    private Task? _activeCaptureLoopTask;
    private Task? _activeClipboardLoopTask;
    private AppSettings _settings;
    private AppSettings _activeCaptureSettings;
    private SessionStateSnapshot _currentState;
    private String? _lastAppliedClipboardText;

    public SessionCoordinator(IDesktopStreamHost desktopStreamHost, IRemoteSessionWindowManager remoteSessionWindowManager, IAppInteractionService appInteractionService)
    {
        _desktopStreamHost = desktopStreamHost;
        _remoteSessionWindowManager = remoteSessionWindowManager;
        _appInteractionService = appInteractionService;
        _remoteSessionWindowManager.AllDisplaysClosed += HandleAllDisplaysClosed;
        _lifecycleLock = new SemaphoreSlim(1, 1);
        _sendLock = new SemaphoreSlim(1, 1);
        _activityEntries = new List<ActivityEntry>();
        _incomingFileTransfers = new Dictionary<Guid, IncomingFileTransferState>();
        _settings = AppSettings.CreateDefault();
        _activeCaptureSettings = CloneSettings(_settings);
        _currentState = new SessionStateSnapshot
        {
            StatusTitle = T("status.ready"),
            StatusDetail = T("status.choose_role"),
            ListenerSummary = T("status.offline"),
            ActiveTransportSummary = T("status.auto"),
            CanShareLocalDesktop = PlatformEnvironment.GetLocalShareSupport().IsSupported,
            ShareSupportDetail = PlatformEnvironment.GetLocalShareSupport().Detail
        };
    }

    public event EventHandler? StateChanged;

    public SessionStateSnapshot CurrentState => _currentState;

    public async Task StartAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _settings = settings;
            _activeCaptureSettings = CloneSettings(settings);
            _desktopStreamHost.UpdateSettings(settings);
            await CloseActiveSessionAsync(true).ConfigureAwait(false);
            await StopListenerAsync().ConfigureAwait(false);
            UpdateState(
                T("status.ready"),
                T("status.choose_role"),
                T("status.offline"),
                T("status.no_active_peer"),
                T("status.choose_peer"),
                BuildTransportLabel(settings.PreferredTransport),
                PlatformFamily.Unknown,
                String.Empty,
                settings.DefaultDirection,
                false,
                false,
                false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task ApplySettingsAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _settings = settings;
            _activeCaptureSettings = CloneSettings(settings);
            _desktopStreamHost.UpdateSettings(settings);
            UpdateState(
                _currentState.StatusTitle,
                _currentState.StatusDetail,
                _currentState.ListenerSummary,
                _currentState.ActivePeerDisplayName,
                _currentState.ActivePeerAddress,
                BuildTransportLabel(settings.PreferredTransport),
                _currentState.ActivePeerPlatformFamily,
                _currentState.ActivePeerOperatingSystem,
                _currentState.ActiveDirection,
                _currentState.IsListening,
                _currentState.IsConnected,
                _currentState.RequiresPassphrase);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task RestartAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _settings = settings;
            _activeCaptureSettings = CloneSettings(settings);
            _desktopStreamHost.UpdateSettings(settings);
            await StopListenerAsync().ConfigureAwait(false);
            await CloseActiveSessionAsync(true).ConfigureAwait(false);
            _listenerCts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.IPv6Any, settings.ControlPort);
            _listener.Server.DualMode = true;
            _listener.Start();
            AddActivity(T("activity.transport"), F("session.activity.listening", settings.ControlPort));
            UpdateState(
                T("status.ready_for_connections"),
                T("status.accepting_incoming"),
                F("session.listener.port", settings.ControlPort),
                T("status.no_active_peer"),
                T("status.choose_or_wait"),
                BuildTransportLabel(settings.PreferredTransport),
                PlatformFamily.Unknown,
                String.Empty,
                settings.DefaultDirection,
                true,
                false,
                false);
            _acceptLoopTask = StartLongRunningTask(() => AcceptLoopAsync(_listener, _listenerCts.Token), _listenerCts.Token);
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
            await CloseActiveSessionAsync(true).ConfigureAwait(false);
            await StopListenerAsync().ConfigureAwait(false);
            AddActivity(T("activity.session"), T("session.activity.stopped_sharing"));
            UpdateState(
                T("status.ready"),
                T("status.sharing_stopped"),
                T("status.offline"),
                T("status.no_active_peer"),
                T("status.start_sharing_again"),
                BuildTransportLabel(_settings.PreferredTransport),
                PlatformFamily.Unknown,
                String.Empty,
                _settings.DefaultDirection,
                false,
                false,
                false);
        }
        finally
        {
            _lifecycleLock.Release();
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task ConnectAsync(DiscoveryDevice device, ConnectionDirection direction, AppSettings settings, CancellationToken cancellationToken)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _settings = settings;
            _desktopStreamHost.UpdateSettings(settings);
            await CloseActiveSessionAsync(true).ConfigureAwait(false);

            TcpClient client = await ConnectToPeerAsync(device, cancellationToken).ConfigureAwait(false);

            NetworkStream stream = client.GetStream();
            using CancellationTokenSource handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            handshakeCts.CancelAfter(TimeSpan.FromSeconds(5));
            SessionHelloMessage request = new SessionHelloMessage
            {
                MachineId = settings.MachineId,
                DisplayName = settings.DisplayName,
                OperatingSystem = Environment.OSVersion.VersionString,
                PlatformFamily = PlatformEnvironment.DetectPlatformFamily(),
                Direction = direction,
                SessionPassphrase = settings.SessionPassphrase,
                SupportsKeyboardRelay = settings.EnableKeyboardRelay,
                SupportsMouseRelay = settings.EnableMouseRelay,
                RequestedStreamWidth = settings.StreamWidth,
                RequestedStreamHeight = settings.StreamHeight,
                RequestedStreamFrameRate = settings.StreamFrameRate,
                RequestedStreamColorMode = settings.StreamColorMode,
                RequestedStreamTileSize = settings.StreamTileSize,
                RequestedStreamDictionarySizeMb = settings.StreamDictionarySizeMb,
                RequestedStreamStaticCodebookSharePercent = settings.StreamStaticCodebookSharePercent
            };

            try
            {
                await WriteSessionHelloMessageAsync(stream, request, handshakeCts.Token).ConfigureAwait(false);
                SessionHelloResponse? response = await ReadSessionHelloResponseAsync(stream, handshakeCts.Token).ConfigureAwait(false);

                if (response is null)
                {
                    throw new IOException(F("session.error.handshake_closed", device.DisplayName));
                }

                if (!response.Accepted)
                {
                    client.Dispose();
                    AddActivity(T("activity.session"), response.Message ?? T("session.error.peer_rejected"));
                    Boolean requiresPassphrase = response.RequiresPassphrase == true;
                    UpdateState(
                        requiresPassphrase ? T("status.passphrase_required") : T("status.connection_refused"),
                        requiresPassphrase ? T("status.enter_remote_passphrase") : response.Message ?? T("status.peer_rejected"),
                        _listener is null ? T("status.offline") : F("session.listener.port", settings.ControlPort),
                        device.DisplayName,
                        device.NetworkAddress,
                        BuildTransportLabel(settings.PreferredTransport),
                        device.PlatformFamily,
                        device.OperatingSystem,
                        direction,
                        _listener is not null,
                        false,
                        requiresPassphrase);
                    return;
                }

                if (direction == ConnectionDirection.Send && !TryValidateLocalShareAvailability(out String shareUnavailableReason))
                {
                    client.Dispose();
                    AddActivity(T("activity.streaming"), shareUnavailableReason);
                    UpdateState(
                        T("status.sharing_unavailable"),
                        shareUnavailableReason,
                        _listener is null ? T("status.offline") : F("session.listener.port", settings.ControlPort),
                        response.ResponderDisplayName,
                        device.NetworkAddress,
                        BuildTransportLabel(settings.PreferredTransport),
                        response.ResponderPlatformFamily,
                        response.ResponderOperatingSystem,
                        direction,
                        _listener is not null,
                        false,
                        false);
                    return;
                }

                String connectedAddress = FormatEndpointAddress((IPEndPoint)client.Client.RemoteEndPoint!);
                Boolean isLocalDisplaySource = direction == ConnectionDirection.Send;
                StartActiveSession(client, stream, response.ResponderDisplayName, connectedAddress, direction, isLocalDisplaySource, CloneSettings(settings));
                AddActivity(T("activity.session"), F("session.activity.connected", device.DisplayName, connectedAddress));
                DiscoveryNetworkEndpoint? connectedEndpoint = device.NetworkEndpoints.FirstOrDefault(item => item.Address.Equals(connectedAddress, StringComparison.OrdinalIgnoreCase));
                UpdateState(
                    direction == ConnectionDirection.Send ? T("status.sharing_this_machine") : T("status.ready_to_control"),
                    response.Message,
                    _listener is null ? T("status.offline") : F("session.listener.port", settings.ControlPort),
                    response.ResponderDisplayName,
                    connectedAddress,
                    connectedEndpoint is null ? BuildTransportLabel(settings.PreferredTransport) : PlatformEnvironment.BuildTransportSummary(connectedEndpoint),
                    response.ResponderPlatformFamily,
                    response.ResponderOperatingSystem,
                    direction,
                    _listener is not null,
                    true,
                    false);
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is OperationCanceledException || ex is JsonException)
            {
                client.Dispose();
                if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                throw new IOException(F("session.error.handshake_incomplete", device.DisplayName), ex);
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Boolean hadActiveClient = _activeClient is not null;
            Boolean isListening = _listener is not null;
            await CloseActiveSessionAsync(true).ConfigureAwait(false);
            AddActivity(T("activity.session"), hadActiveClient ? T("session.activity.closed") : T("session.activity.returned_ready"));
            UpdateState(
                isListening ? T("status.ready_for_connections") : T("status.ready"),
                hadActiveClient
                    ? isListening
                        ? T("status.session_ended_listening")
                        : T("status.session_ended_ready")
                    : isListening
                        ? T("status.waiting_next_connection")
                        : T("status.idle"),
                isListening ? F("session.listener.port", _settings.ControlPort) : T("status.offline"),
                T("status.no_active_peer"),
                isListening ? T("status.choose_or_wait") : T("status.choose_peer"),
                BuildTransportLabel(_settings.PreferredTransport),
                PlatformFamily.Unknown,
                String.Empty,
                _settings.DefaultDirection,
                isListening,
                false,
                false);
        }
        finally
        {
            _lifecycleLock.Release();
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task SendKeyChordAsync(IReadOnlyList<String> keyNames, CancellationToken cancellationToken)
    {
        if (_activeStream is null || !_currentState.IsConnected || _currentState.ActiveDirection != ConnectionDirection.Receive || keyNames.Count == 0)
        {
            return;
        }

        foreach (String keyName in keyNames)
        {
            await SendRemoteInputAsync(new RemoteInputEvent
            {
                Kind = RemoteInputEventKind.KeyDown,
                Key = keyName
            }, cancellationToken).ConfigureAwait(false);
        }

        for (Int32 index = keyNames.Count - 1; index >= 0; index--)
        {
            await SendRemoteInputAsync(new RemoteInputEvent
            {
                Kind = RemoteInputEventKind.KeyUp,
                Key = keyNames[index]
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task SendFilesAsync(CancellationToken cancellationToken)
    {
        if (_activeStream is null || !_currentState.IsConnected || _currentState.ActiveDirection != ConnectionDirection.Receive)
        {
            return;
        }

        IReadOnlyList<LocalFileReference> files = await _appInteractionService
            .PickFilesForTransferAsync(T("files.pick_send_title"), cancellationToken)
            .ConfigureAwait(false);

        if (files.Count == 0)
        {
            AddActivity(T("activity.files"), T("files.selection_none"));
            return;
        }

        foreach (LocalFileReference file in files)
        {
            await SendLocalFileAsync(file, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RequestFilesFromPeerAsync(CancellationToken cancellationToken)
    {
        if (_activeStream is null || !_currentState.IsConnected || _currentState.ActiveDirection != ConnectionDirection.Receive)
        {
            return;
        }

        await SendPacketAsync(SessionPacketType.FileTransfer, new FileTransferPacket
        {
            Kind = FileTransferPacketKind.RequestSelection
        }, ReadOnlyMemory<Byte>.Empty, cancellationToken).ConfigureAwait(false);
        AddActivity(T("activity.files"), T("files.requested_remote_selection"));
    }

    public async ValueTask DisposeAsync()
    {
        ClosedSessionTasks closedSessionTasks = default;
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);

        try
        {
            closedSessionTasks = DetachActiveSession(true);
            await StopListenerAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }

        await DrainClosedSessionTasksAsync(closedSessionTasks).ConfigureAwait(false);
        _remoteSessionWindowManager.AllDisplaysClosed -= HandleAllDisplaysClosed;
        _lifecycleLock.Dispose();
        _sendLock.Dispose();
    }

    private void HandleAllDisplaysClosed(Object? sender, EventArgs eventArgs)
    {
        FireAndForgetLifecycle(Task.Run(async () =>
        {
            await _lifecycleLock.WaitAsync().ConfigureAwait(false);

            try
            {
                if (_activeClient is null || _currentState.ActiveDirection != ConnectionDirection.Receive || !_currentState.IsConnected)
                {
                    return;
                }

                await CloseActiveSessionAsync(true).ConfigureAwait(false);
                AddActivity(T("activity.session"), T("session.activity.closed_windows"));
                UpdateState(
                    _listener is null ? T("status.ready") : T("status.ready_for_connections"),
                    _listener is null ? T("status.connect_again") : T("status.session_ended_listening"),
                    _listener is null ? T("status.offline") : F("session.listener.port", _settings.ControlPort),
                    T("status.no_active_peer"),
                    _listener is null ? T("status.choose_peer") : T("status.choose_or_wait"),
                    BuildTransportLabel(_settings.PreferredTransport),
                    PlatformFamily.Unknown,
                    String.Empty,
                    _settings.DefaultDirection,
                    _listener is not null,
                    false,
                    false);
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }));
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient? inboundClient = null;
                try
                {
                    inboundClient = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                    await HandleIncomingClientAsync(inboundClient, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException || ex is SocketException || ex is JsonException)
                {
                    inboundClient?.Dispose();
                    AddActivity(T("activity.session"), F("session.activity.inbound_dropped", ex.Message));
                }
                catch (Exception ex)
                {
                    inboundClient?.Dispose();
                    AddActivity(T("activity.session"), F("session.activity.inbound_prepare_failed", ex.Message));
                }
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

    private async Task HandleIncomingClientAsync(TcpClient inboundClient, CancellationToken cancellationToken)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ConfigureSocket(inboundClient);
            await CloseActiveSessionAsync(true).ConfigureAwait(false);

            NetworkStream stream = inboundClient.GetStream();
            using CancellationTokenSource handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            handshakeCts.CancelAfter(TimeSpan.FromSeconds(5));
            SessionHelloMessage? request = await ReadSessionHelloMessageAsync(stream, handshakeCts.Token).ConfigureAwait(false);
            if (request is null)
            {
                inboundClient.Dispose();
                return;
            }

            try
            {
                if (!String.IsNullOrWhiteSpace(_settings.SessionPassphrase) && !_settings.SessionPassphrase.Equals(request.SessionPassphrase, StringComparison.Ordinal))
                {
                    SessionHelloResponse rejectedResponse = new SessionHelloResponse
                    {
                        Accepted = false,
                        Message = T("status.passphrase_required_short"),
                        ResponderDisplayName = _settings.DisplayName,
                        ResponderOperatingSystem = Environment.OSVersion.VersionString,
                        ResponderPlatformFamily = PlatformEnvironment.DetectPlatformFamily(),
                        RequiresPassphrase = true
                    };

                    await WriteSessionHelloResponseAsync(stream, rejectedResponse, handshakeCts.Token).ConfigureAwait(false);
                    inboundClient.Dispose();
                    AddActivity(T("activity.security"), F("session.activity.passphrase_rejected", request.DisplayName));
                    return;
                }

                Boolean isLocalDisplaySource = request.Direction == ConnectionDirection.Receive;
                if (isLocalDisplaySource && !TryValidateLocalShareAvailability(out String shareUnavailableReason))
                {
                    SessionHelloResponse unsupportedResponse = new SessionHelloResponse
                    {
                        Accepted = false,
                        Message = shareUnavailableReason,
                        ResponderDisplayName = _settings.DisplayName,
                        ResponderOperatingSystem = Environment.OSVersion.VersionString,
                        ResponderPlatformFamily = PlatformEnvironment.DetectPlatformFamily()
                    };

                    await WriteSessionHelloResponseAsync(stream, unsupportedResponse, handshakeCts.Token).ConfigureAwait(false);
                    inboundClient.Dispose();
                    AddActivity(T("activity.streaming"), shareUnavailableReason);
                    return;
                }

                AppSettings captureSettings = isLocalDisplaySource ? BuildCaptureSettingsForRequest(request) : CloneSettings(_settings);
                SessionHelloResponse response = new SessionHelloResponse
                {
                    Accepted = true,
                    Message = isLocalDisplaySource
                        ? T("session.handshake.remote_can_control")
                        : T("session.handshake.remote_ready_to_send"),
                    ResponderDisplayName = _settings.DisplayName,
                    ResponderOperatingSystem = Environment.OSVersion.VersionString,
                    ResponderPlatformFamily = PlatformEnvironment.DetectPlatformFamily()
                };

                await WriteSessionHelloResponseAsync(stream, response, handshakeCts.Token).ConfigureAwait(false);

                String peerAddress = FormatEndpointAddress((IPEndPoint)inboundClient.Client.RemoteEndPoint!);
                ConnectionDirection localDirection = isLocalDisplaySource ? ConnectionDirection.Send : ConnectionDirection.Receive;
                StartActiveSession(inboundClient, stream, request.DisplayName, peerAddress, localDirection, isLocalDisplaySource, captureSettings);
                AddActivity(T("activity.session"), F("session.activity.accepted_inbound", request.DisplayName, peerAddress));
                UpdateState(
                    isLocalDisplaySource ? T("status.sharing_this_machine") : T("status.receiving_remote_screen"),
                    response.Message,
                    F("session.listener.port", _settings.ControlPort),
                    request.DisplayName,
                    peerAddress,
                    BuildTransportLabel(_settings.PreferredTransport),
                    request.PlatformFamily,
                    request.OperatingSystem,
                    localDirection,
                    true,
                    true,
                    false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await TryWriteSessionHelloResponseAsync(stream, BuildHandshakeFailureResponse(T("session.error.prepare_timeout"))).ConfigureAwait(false);
                inboundClient.Dispose();
                AddActivity(T("activity.session"), F("session.activity.prepare_timeout", request.DisplayName));
            }
            catch (Exception ex)
            {
                await TryWriteSessionHelloResponseAsync(stream, BuildHandshakeFailureResponse(F("session.error.prepare_failed", ex.Message))).ConfigureAwait(false);
                inboundClient.Dispose();
                AddActivity(T("activity.session"), F("session.activity.rejected_inbound", request.DisplayName, ex.Message));
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private void StartActiveSession(TcpClient client, NetworkStream stream, String peerDisplayName, String peerAddress, ConnectionDirection direction, Boolean isLocalDisplaySource, AppSettings captureSettings)
    {
        ConfigureSocket(client);
        _activeClient = client;
        _activeStream = stream;
        _lastAppliedClipboardText = null;
        _activeCaptureSettings = captureSettings;
        if (isLocalDisplaySource)
        {
            _desktopStreamHost.UpdateSettings(captureSettings);
        }
        _activeSessionCts = new CancellationTokenSource();
        CancellationToken cancellationToken = _activeSessionCts.Token;
        _activeReceiveLoopTask = StartLongRunningTask(() => ReceiveLoopAsync(client, stream, peerDisplayName, peerAddress, direction, isLocalDisplaySource, cancellationToken), cancellationToken);
        _activeClipboardLoopTask = StartLongRunningTask(() => ClipboardLoopAsync(cancellationToken), cancellationToken);

        if (isLocalDisplaySource)
        {
            _activeCaptureLoopTask = StartLongRunningTask(() => CaptureLoopAsync(cancellationToken), cancellationToken);
        }
    }

    private async Task ReceiveLoopAsync(TcpClient client, NetworkStream stream, String peerDisplayName, String peerAddress, ConnectionDirection direction, Boolean isLocalDisplaySource, CancellationToken cancellationToken)
    {
        Dictionary<String, TileFrameDecodeState> decodeStates = new Dictionary<String, TileFrameDecodeState>(StringComparer.OrdinalIgnoreCase);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                SessionBinaryPacket? packet = await SessionBinaryProtocol.ReadPacketAsync(stream, cancellationToken).ConfigureAwait(false);
                if (packet is null)
                {
                    break;
                }

                if (isLocalDisplaySource)
                {
                    switch ((SessionPacketType)packet.PacketType)
                    {
                        case SessionPacketType.InputEvent:
                            InputEventPacket? inputPacket = JsonSerializer.Deserialize(packet.Metadata, ShadowLinkJsonSerializerContext.Default.InputEventPacket);
                            if (inputPacket?.InputEvent is not null)
                            {
                                _desktopStreamHost.ApplyInput(inputPacket.InputEvent);
                            }
                            break;
                        case SessionPacketType.Clipboard:
                            ClipboardPacket? clipboardPacket = JsonSerializer.Deserialize(packet.Metadata, ShadowLinkJsonSerializerContext.Default.ClipboardPacket);
                            await ApplyClipboardPacketAsync(clipboardPacket, cancellationToken).ConfigureAwait(false);
                            break;
                        case SessionPacketType.FileTransfer:
                            FileTransferPacket? inboundFilePacket = JsonSerializer.Deserialize(packet.Metadata, ShadowLinkJsonSerializerContext.Default.FileTransferPacket);
                            await HandleFileTransferPacketAsync(inboundFilePacket, packet.Payload, cancellationToken).ConfigureAwait(false);
                            break;
                    }

                    continue;
                }

                switch ((SessionPacketType)packet.PacketType)
                {
                    case SessionPacketType.Clipboard:
                        ClipboardPacket? clipboardPacket = JsonSerializer.Deserialize(packet.Metadata, ShadowLinkJsonSerializerContext.Default.ClipboardPacket);
                        await ApplyClipboardPacketAsync(clipboardPacket, cancellationToken).ConfigureAwait(false);
                        break;
                    case SessionPacketType.FileTransfer:
                        FileTransferPacket? filePacket = JsonSerializer.Deserialize(packet.Metadata, ShadowLinkJsonSerializerContext.Default.FileTransferPacket);
                        await HandleFileTransferPacketAsync(filePacket, packet.Payload, cancellationToken).ConfigureAwait(false);
                        break;
                    case SessionPacketType.DisplayManifest:
                        DisplayManifestPacket? manifestPacket = JsonSerializer.Deserialize(packet.Metadata, ShadowLinkJsonSerializerContext.Default.DisplayManifestPacket);
                        IReadOnlyList<RemoteDisplayDescriptor> displays = manifestPacket is null
                            ? Array.Empty<RemoteDisplayDescriptor>()
                            : manifestPacket.Displays;
                        _remoteSessionWindowManager.ShowDisplays(displays, GetReleaseGesture(), SendInputAsync, direction, _settings.DisplayScaleMode);
                        if (displays.Count == 0)
                        {
                            AddActivity(T("activity.streaming"), T("streaming.no_displays"));
                        }
                        else
                        {
                            AddActivity(T("activity.streaming"), F("streaming.opened_windows", displays.Count));
                        }
                        break;
                    case SessionPacketType.DisplayFrame:
                        DisplayFramePacket? framePacket = JsonSerializer.Deserialize(packet.Metadata, ShadowLinkJsonSerializerContext.Default.DisplayFramePacket);
                        if (!String.IsNullOrWhiteSpace(framePacket?.DisplayId) && packet.Payload.Length > 0)
                        {
                            Byte[] framePayload = FramePayloadCodec.Decode(packet.Payload, framePacket.IsPayloadCompressed);
                            if (!decodeStates.TryGetValue(framePacket.DisplayId, out TileFrameDecodeState? decodeState) ||
                                decodeState.FrameWidth != framePacket.FrameWidth ||
                                decodeState.FrameHeight != framePacket.FrameHeight ||
                                decodeState.TileSize != framePacket.TileSize ||
                                decodeState.ColorMode != framePacket.ColorMode ||
                                decodeState.DictionarySizeMb != framePacket.DictionarySizeMb ||
                                decodeState.StaticCodebookSharePercent != framePacket.StaticCodebookSharePercent)
                            {
                                decodeState = new TileFrameDecodeState(
                                    framePacket.FrameWidth,
                                    framePacket.FrameHeight,
                                    framePacket.TileSize,
                                    framePacket.ColorMode,
                                    framePacket.DictionarySizeMb,
                                    framePacket.StaticCodebookSharePercent);
                                decodeStates[framePacket.DisplayId] = decodeState;
                            }

                            Byte[] bgraFrame = TileFrameCodec.DecodeFrame(framePayload, framePacket, decodeState);
                            _remoteSessionWindowManager.UpdateFrame(framePacket.DisplayId, bgraFrame, framePacket.FrameWidth, framePacket.FrameHeight, framePacket.FrameWidth * 4);
                        }
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            await _lifecycleLock.WaitAsync().ConfigureAwait(false);

            try
            {
                if (ReferenceEquals(client, _activeClient))
                {
                    await CloseActiveSessionAsync(true).ConfigureAwait(false);
                    AddActivity(T("activity.session"), F("session.activity.connection_ended", peerDisplayName));
                    UpdateState(
                        _listener is null ? T("status.ready") : T("status.ready_for_connections"),
                        _listener is null ? T("status.connect_again") : T("status.session_ended_listening"),
                        _listener is null ? T("status.offline") : F("session.listener.port", _settings.ControlPort),
                        peerDisplayName,
                        peerAddress,
                        BuildTransportLabel(_settings.PreferredTransport),
                        PlatformFamily.Unknown,
                        String.Empty,
                        direction,
                        _listener is not null,
                        false,
                        false);
                }
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }
    }

    private async Task CaptureLoopAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<RemoteDisplayDescriptor> displays;
        try
        {
            displays = _desktopStreamHost.GetDisplays();
        }
        catch (Exception ex)
        {
            AddActivity(T("activity.streaming"), F("streaming.capture_failed", ex.Message));
            return;
        }

        Dictionary<String, UInt64> lastFrameSignatures = new Dictionary<String, UInt64>(StringComparer.OrdinalIgnoreCase);
        Dictionary<String, DateTimeOffset> lastSentTimestamps = new Dictionary<String, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        Dictionary<String, TileFrameEncodeState> encodeStates = new Dictionary<String, TileFrameEncodeState>(StringComparer.OrdinalIgnoreCase);
        DisplayManifestPacket manifestPacket = new DisplayManifestPacket
        {
            Displays = displays.ToList()
        };
        await SendPacketAsync(SessionPacketType.DisplayManifest, manifestPacket, ReadOnlyMemory<Byte>.Empty, cancellationToken).ConfigureAwait(false);

        if (!_desktopStreamHost.IsSupported || displays.Count == 0)
        {
            AddActivity(T("activity.streaming"), T("streaming.capture_unavailable"));
            return;
        }

        Int32 frameDelayMilliseconds = Math.Max(8, 1000 / Math.Max(1, _activeCaptureSettings.StreamFrameRate));

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                foreach (RemoteDisplayDescriptor display in displays)
                {
                    CapturedDisplayFrame frame = _desktopStreamHost.CaptureDisplayFrame(display.DisplayId);
                    if (frame.Pixels.Length == 0 || frame.Width <= 0 || frame.Height <= 0)
                    {
                        continue;
                    }

                    UInt64 signature = ComputeFrameSignature(frame.Pixels);
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    if (lastFrameSignatures.TryGetValue(display.DisplayId, out UInt64 previousSignature) &&
                        previousSignature == signature &&
                        lastSentTimestamps.TryGetValue(display.DisplayId, out DateTimeOffset previousSentTimestamp) &&
                        now - previousSentTimestamp < TimeSpan.FromMilliseconds(700))
                    {
                        continue;
                    }

                    lastFrameSignatures[display.DisplayId] = signature;
                    lastSentTimestamps[display.DisplayId] = now;
                    if (!encodeStates.TryGetValue(display.DisplayId, out TileFrameEncodeState? encodeState))
                    {
                        encodeState = new TileFrameEncodeState(
                            _activeCaptureSettings.StreamDictionarySizeMb,
                            _activeCaptureSettings.StreamStaticCodebookSharePercent);
                        encodeStates.Add(display.DisplayId, encodeState);
                    }

                    TileFrameEncodeResult encodeResult = TileFrameCodec.EncodeFrame(frame, _activeCaptureSettings, encodeState);
                    if (encodeResult.ChangedTileCount == 0)
                    {
                        continue;
                    }

                    (Boolean isCompressed, Byte[] payload) = FramePayloadCodec.Encode(
                        encodeResult.Payload,
                        _activeCaptureSettings.StreamColorMode,
                        encodeResult.ChangedTileCount);
                    DisplayFramePacket framePacket = new DisplayFramePacket
                    {
                        DisplayId = display.DisplayId,
                        FrameWidth = frame.Width,
                        FrameHeight = frame.Height,
                        TileSize = Math.Max(4, _activeCaptureSettings.StreamTileSize),
                        ColorMode = _activeCaptureSettings.StreamColorMode,
                        DictionarySizeMb = _activeCaptureSettings.StreamDictionarySizeMb,
                        StaticCodebookSharePercent = _activeCaptureSettings.StreamStaticCodebookSharePercent,
                        IsPayloadCompressed = isCompressed
                    };
                    await SendPacketAsync(SessionPacketType.DisplayFrame, framePacket, payload, cancellationToken).ConfigureAwait(false);
                }

                await Task.Delay(frameDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task ClipboardLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                String clipboardText = (await _appInteractionService.GetClipboardTextAsync(cancellationToken).ConfigureAwait(false)) ?? String.Empty;
                if (!String.Equals(clipboardText, _lastAppliedClipboardText, StringComparison.Ordinal))
                {
                    await SendPacketAsync(SessionPacketType.Clipboard, new ClipboardPacket
                    {
                        Text = clipboardText
                    }, ReadOnlyMemory<Byte>.Empty, cancellationToken).ConfigureAwait(false);
                    _lastAppliedClipboardText = clipboardText;
                }

                await Task.Delay(ClipboardPollIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task ApplyClipboardPacketAsync(ClipboardPacket? clipboardPacket, CancellationToken cancellationToken)
    {
        if (clipboardPacket is null)
        {
            return;
        }

        String nextClipboardText = clipboardPacket.Text ?? String.Empty;
        String currentClipboardText = (await _appInteractionService.GetClipboardTextAsync(cancellationToken).ConfigureAwait(false)) ?? String.Empty;
        _lastAppliedClipboardText = nextClipboardText;

        if (String.Equals(currentClipboardText, nextClipboardText, StringComparison.Ordinal))
        {
            return;
        }

        await _appInteractionService.SetClipboardTextAsync(nextClipboardText, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleFileTransferPacketAsync(FileTransferPacket? filePacket, ReadOnlyMemory<Byte> payload, CancellationToken cancellationToken)
    {
        if (filePacket is null)
        {
            return;
        }

        switch (filePacket.Kind)
        {
            case FileTransferPacketKind.RequestSelection:
                IReadOnlyList<LocalFileReference> requestedFiles = await _appInteractionService
                    .PickFilesForTransferAsync(T("files.pick_request_title"), cancellationToken)
                    .ConfigureAwait(false);

                if (requestedFiles.Count == 0)
                {
                    AddActivity(T("activity.files"), T("files.selection_none"));
                    return;
                }

                foreach (LocalFileReference requestedFile in requestedFiles)
                {
                    await SendLocalFileAsync(requestedFile, cancellationToken).ConfigureAwait(false);
                }
                break;
            case FileTransferPacketKind.BeginFile:
                await BeginIncomingFileTransferAsync(filePacket, cancellationToken).ConfigureAwait(false);
                break;
            case FileTransferPacketKind.Chunk:
                await AppendIncomingFileTransferChunkAsync(filePacket, payload, cancellationToken).ConfigureAwait(false);
                break;
            case FileTransferPacketKind.CompleteFile:
                CompleteIncomingFileTransfer(filePacket);
                break;
            case FileTransferPacketKind.Abort:
                AbortIncomingFileTransfer(filePacket.TransferId, true);
                if (!String.IsNullOrWhiteSpace(filePacket.Message))
                {
                    AddActivity(T("activity.files"), F("files.transfer_failed", filePacket.Message));
                }
                break;
        }
    }

    private async Task SendLocalFileAsync(LocalFileReference file, CancellationToken cancellationToken)
    {
        if (String.IsNullOrWhiteSpace(file.LocalPath) || !File.Exists(file.LocalPath))
        {
            AddActivity(T("activity.files"), F("files.transfer_failed", file.FileName));
            return;
        }

        Guid transferId = Guid.NewGuid();
        Byte[] buffer = new Byte[FileTransferChunkSize];
        try
        {
            await using FileStream fileStream = new FileStream(
                file.LocalPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                FileTransferChunkSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            await SendPacketAsync(SessionPacketType.FileTransfer, new FileTransferPacket
            {
                TransferId = transferId,
                Kind = FileTransferPacketKind.BeginFile,
                FileName = String.IsNullOrWhiteSpace(file.FileName) ? Path.GetFileName(file.LocalPath) : file.FileName,
                TotalBytes = fileStream.Length
            }, ReadOnlyMemory<Byte>.Empty, cancellationToken).ConfigureAwait(false);

            Int32 chunkIndex = 0;
            while (true)
            {
                Int32 bytesRead = await fileStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (bytesRead <= 0)
                {
                    break;
                }

                await SendPacketAsync(SessionPacketType.FileTransfer, new FileTransferPacket
                {
                    TransferId = transferId,
                    Kind = FileTransferPacketKind.Chunk,
                    ChunkIndex = chunkIndex++
                }, new ReadOnlyMemory<Byte>(buffer, 0, bytesRead), cancellationToken).ConfigureAwait(false);
            }

            await SendPacketAsync(SessionPacketType.FileTransfer, new FileTransferPacket
            {
                TransferId = transferId,
                Kind = FileTransferPacketKind.CompleteFile,
                FileName = String.IsNullOrWhiteSpace(file.FileName) ? Path.GetFileName(file.LocalPath) : file.FileName,
                TotalBytes = fileStream.Length
            }, ReadOnlyMemory<Byte>.Empty, cancellationToken).ConfigureAwait(false);
            AddActivity(T("activity.files"), F("files.sent", String.IsNullOrWhiteSpace(file.FileName) ? Path.GetFileName(file.LocalPath) : file.FileName));
        }
        catch (OperationCanceledException)
        {
            await TryAbortOutgoingTransferAsync(transferId, T("files.transfer_cancelled")).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is SocketException)
        {
            await TryAbortOutgoingTransferAsync(transferId, ex.Message).ConfigureAwait(false);
            AddActivity(T("activity.files"), F("files.transfer_failed", ex.Message));
        }
    }

    private async Task TryAbortOutgoingTransferAsync(Guid transferId, String message)
    {
        try
        {
            await SendPacketAsync(SessionPacketType.FileTransfer, new FileTransferPacket
            {
                TransferId = transferId,
                Kind = FileTransferPacketKind.Abort,
                Message = message
            }, ReadOnlyMemory<Byte>.Empty, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is SocketException || ex is OperationCanceledException)
        {
        }
    }

    private async Task BeginIncomingFileTransferAsync(FileTransferPacket filePacket, CancellationToken cancellationToken)
    {
        AbortIncomingFileTransfer(filePacket.TransferId, true);

        String incomingDirectory = GetIncomingTransferDirectory();
        Directory.CreateDirectory(incomingDirectory);
        String fileName = String.IsNullOrWhiteSpace(filePacket.FileName) ? "transfer.bin" : Path.GetFileName(filePacket.FileName);
        String filePath = CreateUniqueIncomingFilePath(incomingDirectory, fileName);
        FileStream stream = new FileStream(
            filePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            FileTransferChunkSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        _incomingFileTransfers[filePacket.TransferId] = new IncomingFileTransferState(filePath, fileName, filePacket.TotalBytes, stream);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task AppendIncomingFileTransferChunkAsync(FileTransferPacket filePacket, ReadOnlyMemory<Byte> payload, CancellationToken cancellationToken)
    {
        if (!_incomingFileTransfers.TryGetValue(filePacket.TransferId, out IncomingFileTransferState? state) || state is null)
        {
            return;
        }

        await state.Stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        state.BytesWritten += payload.Length;
    }

    private void CompleteIncomingFileTransfer(FileTransferPacket filePacket)
    {
        if (!_incomingFileTransfers.Remove(filePacket.TransferId, out IncomingFileTransferState? state) || state is null)
        {
            return;
        }

        state.Stream.Dispose();
        AddActivity(T("activity.files"), F("files.received", state.FileName));
        AddActivity(T("activity.files"), F("files.saved_to", state.FilePath));
    }

    private void AbortIncomingFileTransfer(Guid transferId, Boolean deletePartialFile)
    {
        if (!_incomingFileTransfers.Remove(transferId, out IncomingFileTransferState? state) || state is null)
        {
            return;
        }

        try
        {
            state.Stream.Dispose();
        }
        finally
        {
            if (deletePartialFile)
            {
                try
                {
                    if (File.Exists(state.FilePath))
                    {
                        File.Delete(state.FilePath);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private void AbortAllIncomingFileTransfers()
    {
        Guid[] transferIds = _incomingFileTransfers.Keys.ToArray();
        foreach (Guid transferId in transferIds)
        {
            AbortIncomingFileTransfer(transferId, true);
        }
    }

    private static String GetIncomingTransferDirectory()
    {
        String userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (String.IsNullOrWhiteSpace(userProfile))
        {
            userProfile = AppContext.BaseDirectory;
        }

        return Path.Combine(userProfile, "Downloads", "ShadowLink");
    }

    private static String CreateUniqueIncomingFilePath(String directoryPath, String fileName)
    {
        String baseName = Path.GetFileNameWithoutExtension(fileName);
        String extension = Path.GetExtension(fileName);
        String candidatePath = Path.Combine(directoryPath, fileName);
        Int32 suffix = 2;

        while (File.Exists(candidatePath))
        {
            candidatePath = Path.Combine(directoryPath, baseName + " (" + suffix.ToString() + ")" + extension);
            suffix++;
        }

        return candidatePath;
    }

    private async Task SendInputAsync(RemoteInputEvent inputEvent)
    {
        await SendRemoteInputAsync(inputEvent, CancellationToken.None).ConfigureAwait(false);
    }

    private Task SendRemoteInputAsync(RemoteInputEvent inputEvent, CancellationToken cancellationToken)
    {
        return SendPacketAsync(SessionPacketType.InputEvent, new InputEventPacket
        {
            InputEvent = inputEvent
        }, ReadOnlyMemory<Byte>.Empty, cancellationToken);
    }

    private async Task SendPacketAsync<TMetadata>(SessionPacketType packetType, TMetadata metadata, ReadOnlyMemory<Byte> payload, CancellationToken cancellationToken)
    {
        NetworkStream? stream = _activeStream;
        if (stream is null)
        {
            return;
        }

        Byte[] metadataBytes = JsonSerializer.SerializeToUtf8Bytes(metadata, typeof(TMetadata), ShadowLinkJsonSerializerContext.Default);

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await SessionBinaryProtocol.WritePacketAsync(stream, (Byte)packetType, metadataBytes, payload, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private Task CloseActiveSessionAsync(Boolean closeWindows)
    {
        ClosedSessionTasks closedSessionTasks = DetachActiveSession(closeWindows);
        if (!closedSessionTasks.HasPendingWork)
        {
            return Task.CompletedTask;
        }

        FireAndForgetLifecycle(DrainClosedSessionTasksAsync(closedSessionTasks));
        return Task.CompletedTask;
    }

    private ClosedSessionTasks DetachActiveSession(Boolean closeWindows)
    {
        CancellationTokenSource? activeSessionCts = _activeSessionCts;
        _activeSessionCts = null;
        Task? activeReceiveLoopTask = _activeReceiveLoopTask;
        Task? activeCaptureLoopTask = _activeCaptureLoopTask;
        Task? activeClipboardLoopTask = _activeClipboardLoopTask;
        _activeReceiveLoopTask = null;
        _activeCaptureLoopTask = null;
        _activeClipboardLoopTask = null;
        activeSessionCts?.Cancel();
        _lastAppliedClipboardText = null;

        NetworkStream? activeStream = _activeStream;
        _activeStream = null;
        activeStream?.Dispose();

        TcpClient? activeClient = _activeClient;
        _activeClient = null;
        activeClient?.Dispose();

        if (closeWindows)
        {
            _remoteSessionWindowManager.CloseAll();
        }

        AbortAllIncomingFileTransfers();
        _desktopStreamHost.UpdateSettings(_settings);
        return new ClosedSessionTasks(activeSessionCts, activeReceiveLoopTask, activeCaptureLoopTask, activeClipboardLoopTask, Task.CurrentId);
    }

    private static async Task DrainClosedSessionTasksAsync(ClosedSessionTasks closedSessionTasks)
    {
        try
        {
            await AwaitClosedSessionTaskAsync(closedSessionTasks.ActiveCaptureLoopTask, closedSessionTasks.CurrentTaskId).ConfigureAwait(false);
            await AwaitClosedSessionTaskAsync(closedSessionTasks.ActiveClipboardLoopTask, closedSessionTasks.CurrentTaskId).ConfigureAwait(false);
            await AwaitClosedSessionTaskAsync(closedSessionTasks.ActiveReceiveLoopTask, closedSessionTasks.CurrentTaskId).ConfigureAwait(false);
        }
        finally
        {
            closedSessionTasks.ActiveSessionCts?.Dispose();
        }
    }

    private static async Task AwaitClosedSessionTaskAsync(Task? task, Int32? currentTaskId)
    {
        if (task is null || task.Id == currentTaskId)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException || ex is IOException || ex is ObjectDisposedException || ex is SocketException)
        {
        }
    }

    private async Task StopListenerAsync()
    {
        CancellationTokenSource? listenerCts = _listenerCts;
        _listenerCts = null;
        Task? acceptLoopTask = _acceptLoopTask;
        _acceptLoopTask = null;
        listenerCts?.Cancel();

        if (_listener is not null)
        {
            _listener.Stop();
            _listener = null;
        }

        if (acceptLoopTask is not null && acceptLoopTask.Id != Task.CurrentId)
        {
            try
            {
                await acceptLoopTask.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException || ex is SocketException)
            {
            }
        }

        listenerCts?.Dispose();
    }

    private async Task<TcpClient> ConnectToPeerAsync(DiscoveryDevice device, CancellationToken cancellationToken)
    {
        List<ConnectionCandidate> candidates = BuildConnectionCandidates(device);
        List<String> attempts = new List<String>();
        Exception? lastException = null;

        foreach (ConnectionCandidate candidate in candidates)
        {
            IReadOnlyList<IPAddress> addresses;
            try
            {
                addresses = await ResolveAddressesAsync(candidate.HostOrAddress, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SocketException || ex is ArgumentException)
            {
                lastException = ex;
                continue;
            }

            foreach (IPAddress address in addresses)
            {
                String endpointLabel = address.AddressFamily == AddressFamily.InterNetworkV6
                    ? "[" + address + "]:" + device.ControlPort
                    : address + ":" + device.ControlPort;
                attempts.Add(endpointLabel);

                try
                {
                    using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
                    TcpClient client = new TcpClient(address.AddressFamily);
                    try
                    {
                        ConfigureSocket(client);
                        await client.ConnectAsync(address, device.ControlPort, timeoutCts.Token).ConfigureAwait(false);
                        return client;
                    }
                    catch
                    {
                        client.Dispose();
                        throw;
                    }
                }
                catch (Exception ex) when (ex is SocketException || ex is OperationCanceledException)
                {
                    lastException = ex;
                }
            }
        }

        String attemptSummary = attempts.Count == 0
            ? T("session.error.no_endpoint_resolved")
            : F("session.error.tried_endpoints", String.Join(", ", attempts));
        String message = F("session.error.could_not_reach", device.DisplayName, device.ControlPort, attemptSummary);
        throw new IOException(message, lastException);
    }

    private List<ConnectionCandidate> BuildConnectionCandidates(DiscoveryDevice device)
    {
        List<ConnectionCandidate> candidates = new List<ConnectionCandidate>();
        IReadOnlyList<DiscoveryNetworkEndpoint> endpoints = device.NetworkEndpoints
            .OrderByDescending(item => ScoreEndpoint(item, _settings.PreferredTransport))
            .ThenBy(item => item.Address, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (_settings.PreferredTransport == TransportPreference.UsbCNetwork &&
            endpoints.Count == 0 &&
            !device.MachineId.StartsWith("manual-", StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException(F("session.error.direct_mode_no_endpoint", device.DisplayName));
        }

        if (_settings.PreferredTransport == TransportPreference.UsbCNetwork &&
            endpoints.Count > 0 &&
            !endpoints.Any(item => item.IsThunderboltTransport))
        {
            throw new IOException(F("session.error.direct_mode_no_tb", device.DisplayName));
        }

        if (!String.IsNullOrWhiteSpace(device.NetworkAddress) &&
            !candidates.Any(item => item.HostOrAddress.Equals(device.NetworkAddress.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            candidates.Add(new ConnectionCandidate(device.NetworkAddress.Trim()));
        }

        foreach (DiscoveryNetworkEndpoint endpoint in endpoints)
        {
            if (_settings.PreferredTransport == TransportPreference.Network && endpoint.IsThunderboltTransport)
            {
                continue;
            }

            if (_settings.PreferredTransport == TransportPreference.UsbCNetwork && !endpoint.IsThunderboltTransport)
            {
                continue;
            }

            if (!String.IsNullOrWhiteSpace(endpoint.Address) &&
                !candidates.Any(item => item.HostOrAddress.Equals(endpoint.Address.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add(new ConnectionCandidate(endpoint.Address.Trim()));
            }
        }

        if (!String.IsNullOrWhiteSpace(device.HostName) &&
            !candidates.Any(item => item.HostOrAddress.Equals(device.HostName.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            candidates.Add(new ConnectionCandidate(device.HostName.Trim()));
        }

        return candidates;
    }

    private static async Task<IReadOnlyList<IPAddress>> ResolveAddressesAsync(String candidate, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(candidate, out IPAddress? parsedAddress))
        {
            return new[] { parsedAddress };
        }

        IPAddress[] resolvedAddresses = await Dns.GetHostAddressesAsync(candidate, cancellationToken).ConfigureAwait(false);
        return resolvedAddresses
            .Where(item => item.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
            .Distinct()
            .ToArray();
    }

    private static void ConfigureSocket(TcpClient client)
    {
        Socket socket = client.Client;
        if (socket.AddressFamily == AddressFamily.InterNetworkV6 && !socket.Connected && !socket.IsBound)
        {
            try
            {
                socket.DualMode = true;
            }
            catch (Exception ex) when (ex is SocketException || ex is InvalidOperationException || ex is NotSupportedException)
            {
            }
        }

        client.NoDelay = true;
        client.SendBufferSize = 16 * 1024 * 1024;
        client.ReceiveBufferSize = 16 * 1024 * 1024;
    }

    private static String FormatEndpointAddress(IPEndPoint endpoint)
    {
        String formatted = endpoint.Address.ToString();
        return endpoint.Address.AddressFamily == AddressFamily.InterNetworkV6 && endpoint.Address.ScopeId != 0 && !formatted.Contains('%')
            ? formatted + "%" + endpoint.Address.ScopeId
            : formatted;
    }

    private async Task WriteSessionHelloMessageAsync(NetworkStream stream, SessionHelloMessage payload, CancellationToken cancellationToken)
    {
        String json = JsonSerializer.Serialize(payload, ShadowLinkJsonSerializerContext.Default.SessionHelloMessage) + Environment.NewLine;
        Byte[] buffer = Encoding.UTF8.GetBytes(json);
        await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteSessionHelloResponseAsync(NetworkStream stream, SessionHelloResponse payload, CancellationToken cancellationToken)
    {
        String json = JsonSerializer.Serialize(payload, ShadowLinkJsonSerializerContext.Default.SessionHelloResponse) + Environment.NewLine;
        Byte[] buffer = Encoding.UTF8.GetBytes(json);
        await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<SessionHelloMessage?> ReadSessionHelloMessageAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        String? line = await ReadProtocolLineAsync(stream, cancellationToken).ConfigureAwait(false);
        return String.IsNullOrWhiteSpace(line) ? default : JsonSerializer.Deserialize(line, ShadowLinkJsonSerializerContext.Default.SessionHelloMessage);
    }

    private async Task<SessionHelloResponse?> ReadSessionHelloResponseAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        String? line = await ReadProtocolLineAsync(stream, cancellationToken).ConfigureAwait(false);
        return String.IsNullOrWhiteSpace(line) ? default : JsonSerializer.Deserialize(line, ShadowLinkJsonSerializerContext.Default.SessionHelloResponse);
    }

    private static async Task<String?> ReadProtocolLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        List<Byte> buffer = new List<Byte>(256);
        Byte[] nextByte = new Byte[1];

        while (true)
        {
            Int32 bytesRead = await stream.ReadAsync(nextByte, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            if (nextByte[0] == (Byte)'\n')
            {
                break;
            }

            if (nextByte[0] != (Byte)'\r')
            {
                buffer.Add(nextByte[0]);
            }
        }

        return buffer.Count == 0 ? null : Encoding.UTF8.GetString(buffer.ToArray());
    }

    private void AddActivity(String category, String message)
    {
        _activityEntries.Insert(0, new ActivityEntry(DateTimeOffset.UtcNow, category, message));

        if (_activityEntries.Count > 24)
        {
            _activityEntries.RemoveRange(24, _activityEntries.Count - 24);
        }

        _currentState = new SessionStateSnapshot
        {
            StatusTitle = _currentState.StatusTitle,
            StatusDetail = _currentState.StatusDetail,
            ListenerSummary = _currentState.ListenerSummary,
            ActivePeerDisplayName = _currentState.ActivePeerDisplayName,
            ActivePeerAddress = _currentState.ActivePeerAddress,
            ActiveTransportSummary = _currentState.ActiveTransportSummary,
            ActivePeerPlatformFamily = _currentState.ActivePeerPlatformFamily,
            ActivePeerOperatingSystem = _currentState.ActivePeerOperatingSystem,
            ActiveDirection = _currentState.ActiveDirection,
            IsListening = _currentState.IsListening,
            IsConnected = _currentState.IsConnected,
            RequiresPassphrase = _currentState.RequiresPassphrase,
            CanShareLocalDesktop = _currentState.CanShareLocalDesktop,
            ShareSupportDetail = _currentState.ShareSupportDetail,
            ActivityEntries = _activityEntries.ToArray()
        };

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private String GetReleaseGesture()
    {
        ShortcutBinding? releaseShortcut = _settings.ShortcutBindings.FirstOrDefault(item => item.Name.Equals("shortcut.release.name", StringComparison.OrdinalIgnoreCase));
        return releaseShortcut?.Gesture ?? "Ctrl+Alt+Shift+Backspace";
    }

    private static String BuildTransportLabel(TransportPreference preference)
    {
        return preference switch
        {
            TransportPreference.Auto => T("status.auto"),
            TransportPreference.Network => T("transport.network"),
            TransportPreference.UsbCNetwork => T("transport.tb_usb4"),
            _ => T("status.auto")
        };
    }

    private static UInt64 ComputeFrameSignature(Byte[] frame)
    {
        const UInt64 offset = 14695981039346656037;
        const UInt64 prime = 1099511628211;
        UInt64 hash = offset;
        Int32 step = Math.Max(1, frame.Length / 4096);

        for (Int32 index = 0; index < frame.Length; index += step)
        {
            hash ^= frame[index];
            hash *= prime;
        }

        hash ^= (UInt64)frame.Length;
        hash *= prime;
        return hash;
    }

    private AppSettings BuildCaptureSettingsForRequest(SessionHelloMessage request)
    {
        AppSettings captureSettings = CloneSettings(_settings);
        Boolean hasRequestedStreamSettings = request.RequestedStreamWidth > 0 ||
                                            request.RequestedStreamHeight > 0 ||
                                            request.RequestedStreamFrameRate > 0 ||
                                            request.RequestedStreamTileSize > 0 ||
                                            request.RequestedStreamDictionarySizeMb > 0;
        captureSettings.StreamWidth = request.RequestedStreamWidth > 0 ? Math.Max(320, request.RequestedStreamWidth) : _settings.StreamWidth;
        captureSettings.StreamHeight = request.RequestedStreamHeight > 0 ? Math.Max(320, request.RequestedStreamHeight) : _settings.StreamHeight;
        captureSettings.StreamFrameRate = request.RequestedStreamFrameRate > 0 ? Math.Max(1, request.RequestedStreamFrameRate) : _settings.StreamFrameRate;
        captureSettings.StreamColorMode = hasRequestedStreamSettings ? request.RequestedStreamColorMode : _settings.StreamColorMode;
        captureSettings.StreamTileSize = request.RequestedStreamTileSize > 0 ? Math.Max(4, request.RequestedStreamTileSize) : _settings.StreamTileSize;
        captureSettings.StreamDictionarySizeMb = request.RequestedStreamDictionarySizeMb > 0 ? Math.Max(64, request.RequestedStreamDictionarySizeMb) : _settings.StreamDictionarySizeMb;
        captureSettings.StreamStaticCodebookSharePercent = request.RequestedStreamStaticCodebookSharePercent > 0
            ? Math.Clamp(request.RequestedStreamStaticCodebookSharePercent, 5, 95)
            : _settings.StreamStaticCodebookSharePercent;
        return captureSettings;
    }

    private static AppSettings CloneSettings(AppSettings settings)
    {
        return new AppSettings
        {
            MachineId = settings.MachineId,
            DisplayName = settings.DisplayName,
            DefaultDirection = settings.DefaultDirection,
            PreferredTransport = settings.PreferredTransport,
            DiscoveryPort = settings.DiscoveryPort,
            ControlPort = settings.ControlPort,
            AutoRefreshIntervalSeconds = settings.AutoRefreshIntervalSeconds,
            StreamWidth = settings.StreamWidth,
            StreamHeight = settings.StreamHeight,
            StreamFrameRate = settings.StreamFrameRate,
            StreamColorMode = settings.StreamColorMode,
            StreamTileSize = settings.StreamTileSize,
            StreamDictionarySizeMb = settings.StreamDictionarySizeMb,
            StreamStaticCodebookSharePercent = settings.StreamStaticCodebookSharePercent,
            DisplayScaleMode = settings.DisplayScaleMode,
            EnableKeyboardRelay = settings.EnableKeyboardRelay,
            EnableMouseRelay = settings.EnableMouseRelay,
            AutoStartDiscovery = settings.AutoStartDiscovery,
            RememberRecentPeers = settings.RememberRecentPeers,
            SessionPassphrase = settings.SessionPassphrase,
            ShortcutBindings = settings.ShortcutBindings.Select(item => new ShortcutBinding
            {
                Name = item.Name,
                Gesture = item.Gesture,
                Description = item.Description,
                IsEnabled = item.IsEnabled
            }).ToList()
        };
    }

    private void UpdateState(
        String statusTitle,
        String statusDetail,
        String listenerSummary,
        String activePeerDisplayName,
        String activePeerAddress,
        String activeTransportSummary,
        PlatformFamily activePeerPlatformFamily,
        String activePeerOperatingSystem,
        ConnectionDirection activeDirection,
        Boolean isListening,
        Boolean isConnected,
        Boolean requiresPassphrase)
    {
        LocalShareSupport shareSupport = PlatformEnvironment.GetLocalShareSupport();
        _currentState = new SessionStateSnapshot
        {
            StatusTitle = statusTitle,
            StatusDetail = statusDetail,
            ListenerSummary = listenerSummary,
            ActivePeerDisplayName = activePeerDisplayName,
            ActivePeerAddress = activePeerAddress,
            ActiveTransportSummary = activeTransportSummary,
            ActivePeerPlatformFamily = activePeerPlatformFamily,
            ActivePeerOperatingSystem = activePeerOperatingSystem,
            ActiveDirection = activeDirection,
            IsListening = isListening,
            IsConnected = isConnected,
            RequiresPassphrase = requiresPassphrase,
            CanShareLocalDesktop = shareSupport.IsSupported,
            ShareSupportDetail = shareSupport.Detail,
            ActivityEntries = _activityEntries.ToArray()
        };

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private Boolean TryValidateLocalShareAvailability(out String reason)
    {
        try
        {
            LocalShareSupport shareSupport = PlatformEnvironment.GetLocalShareSupport();
            if (!shareSupport.IsSupported || !_desktopStreamHost.IsSupported)
            {
                reason = shareSupport.Detail;
                return false;
            }

            IReadOnlyList<RemoteDisplayDescriptor> displays = _desktopStreamHost.GetDisplays();
            if (displays.Count == 0)
            {
                reason = T("streaming.no_local_displays");
                return false;
            }

            reason = String.Empty;
            return true;
        }
        catch (Exception ex)
        {
            reason = F("streaming.prepare_failed", ex.Message);
            Trace.TraceError("Local share availability validation failed: {0}", ex);
            return false;
        }
    }

    private SessionHelloResponse BuildHandshakeFailureResponse(String message)
    {
        return new SessionHelloResponse
        {
            Accepted = false,
            Message = message,
            ResponderDisplayName = _settings.DisplayName,
            ResponderOperatingSystem = Environment.OSVersion.VersionString,
            ResponderPlatformFamily = PlatformEnvironment.DetectPlatformFamily()
        };
    }

    private async Task TryWriteSessionHelloResponseAsync(NetworkStream stream, SessionHelloResponse payload)
    {
        try
        {
            await WriteSessionHelloResponseAsync(stream, payload, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is SocketException)
        {
        }
    }

    private static Int64 ScoreEndpoint(DiscoveryNetworkEndpoint endpoint, TransportPreference preference)
    {
        return preference switch
        {
            TransportPreference.Network => endpoint.IsThunderboltTransport ? Int64.MinValue / 4 : endpoint.LinkSpeedMbps,
            TransportPreference.UsbCNetwork => endpoint.IsThunderboltTransport ? 2_000_000L + endpoint.LinkSpeedMbps : Int64.MinValue / 4,
            _ => (endpoint.IsThunderboltTransport ? 2_000_000L : 0L) + endpoint.LinkSpeedMbps
        };
    }

    private static async void FireAndForgetLifecycle(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Trace.TraceError("Session lifecycle task failed: {0}", ex);
        }
    }

    private static Task StartLongRunningTask(Func<Task> work, CancellationToken cancellationToken)
    {
        return Task.Factory.StartNew(
                work,
                cancellationToken,
                TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                TaskScheduler.Default)
            .Unwrap();
    }

    private static String T(String key)
    {
        return ShadowLinkText.Translate(key);
    }

    private static String F(String key, params Object[] args)
    {
        return ShadowLinkText.TranslateFormat(key, args);
    }

}
