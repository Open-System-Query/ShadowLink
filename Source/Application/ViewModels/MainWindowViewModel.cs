using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ShadowLink.Application.Commands;
using ShadowLink.Core.Contracts;
using ShadowLink.Core.Models;
using ShadowLink.Localization;
using ShadowLink.Services;

namespace ShadowLink.Application.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private const String DefaultDiscoveryPortText = "45210";
    private const String DefaultControlPortText = "45211";
    private const String DefaultStreamWidthText = "1280";
    private const String DefaultStreamHeightText = "720";
    private const String DefaultFrameRateText = "30";
    private const String DefaultTileSizeText = "16";
    private const String DefaultDictionarySizeMbText = "1024";
    private const String DefaultPatternsPercentText = "50";
    private const Int32 DefaultDiscoveryPort = 45210;
    private const Int32 DefaultControlPort = 45211;
    private const Int32 DefaultStreamWidth = 1280;
    private const Int32 DefaultStreamHeight = 720;
    private const Int32 DefaultFrameRate = 30;
    private const Int32 DefaultTileSize = 16;
    private const Int32 DefaultDictionarySizeMb = 1024;
    private const Int32 DefaultPatternsPercent = 50;
    private const Int32 RoleStepIndex = 0;
    private const Int32 PeerStepIndex = 1;
    private const Int32 LaunchStepIndex = 2;
    private const Int32 OptionsStepIndex = 3;
    private const Int32 ToolsStepIndex = 4;
    private const Int32 AdvancedStepIndex = 5;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IDeviceDiscoveryService _deviceDiscoveryService;
    private readonly ISessionCoordinator _sessionCoordinator;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IFirewallConfigurationService _firewallConfigurationService;
    private readonly IAppInteractionService _appInteractionService;
    private readonly RelayCommand _goToPeerStepCommand;
    private readonly RelayCommand _goToAdvancedStepCommand;
    private readonly RelayCommand _goToToolsStepCommand;
    private readonly AsyncRelayCommand _connectCommand;
    private readonly AsyncRelayCommand _stopShareCommand;
    private readonly AsyncRelayCommand _disconnectCommand;
    private readonly AsyncRelayCommand _configureFirewallCommand;
    private readonly AsyncRelayCommand _closeOptionsCommand;
    private readonly RelayCommand _cancelLaunchCommand;
    private readonly RelayCommand _nextStepCommand;
    private readonly RelayCommand _previousStepCommand;
    private readonly RelayCommand _goToLaunchStepCommand;
    private readonly RelayCommand _applyNativePresetCommand;
    private readonly RelayCommand _applyFullHdPresetCommand;
    private readonly RelayCommand _applyHdPresetCommand;
    private readonly RelayCommand _applyLaptopPresetCommand;
    private readonly RelayCommand _applySvgaPresetCommand;
    private readonly RelayCommand _setBgra32ColorModeCommand;
    private readonly RelayCommand _setBgr24ColorModeCommand;
    private readonly RelayCommand _setRgb565ColorModeCommand;
    private readonly RelayCommand _setRgb332ColorModeCommand;
    private readonly RelayCommand _setIndexed4ColorModeCommand;
    private readonly RelayCommand _applyTile8PresetCommand;
    private readonly RelayCommand _applyTile16PresetCommand;
    private readonly RelayCommand _applyTile32PresetCommand;
    private readonly RelayCommand _applyDictionary512PresetCommand;
    private readonly RelayCommand _applyDictionary1024PresetCommand;
    private readonly RelayCommand _applyDictionary2048PresetCommand;
    private readonly RelayCommand _applyDictionary4096PresetCommand;
    private readonly RelayCommand _applyDictionary8192PresetCommand;
    private readonly RelayCommand _applyDictionary16384PresetCommand;
    private readonly RelayCommand _setDisplayScaleFitCommand;
    private readonly RelayCommand _setDisplayScaleZoomToFitCommand;
    private readonly RelayCommand _setDisplayScaleFillCommand;
    private readonly AsyncRelayCommand _openRemoteStartMenuCommand;
    private readonly AsyncRelayCommand _openRemoteRunDialogCommand;
    private readonly AsyncRelayCommand _openRemoteExplorerCommand;
    private readonly AsyncRelayCommand _openRemoteSettingsCommand;
    private readonly AsyncRelayCommand _openRemoteTaskManagerCommand;
    private readonly AsyncRelayCommand _openRemoteKeyboardCommand;
    private readonly AsyncRelayCommand _openRemoteSpotlightCommand;
    private readonly AsyncRelayCommand _openRemoteFinderCommand;
    private readonly AsyncRelayCommand _openRemoteMissionControlCommand;
    private readonly AsyncRelayCommand _openRemoteForceQuitCommand;
    private readonly AsyncRelayCommand _openRemoteTerminalCommand;
    private readonly AsyncRelayCommand _openRemoteAppSwitcherCommand;
    private readonly AsyncRelayCommand _lockRemoteScreenCommand;
    private readonly AsyncRelayCommand _sendFilesCommand;
    private readonly AsyncRelayCommand _requestFilesFromPeerCommand;
    private Int32 _lastPrimaryStepIndex;
    private AppSettings _settings;
    private DeviceCardViewModel? _selectedDevice;
    private DiscoveryDevice? _selectedDiscoveryTarget;
    private String _displayName = Environment.MachineName;
    private String _discoveryPortText = DefaultDiscoveryPortText;
    private String _controlPortText = DefaultControlPortText;
    private String _sessionPassphrase = String.Empty;
    private String _statusTitle = ShadowLinkText.Translate("status.preparing_workspace");
    private String _statusDetail = ShadowLinkText.Translate("status.loading_profile");
    private String _listenerSummary = ShadowLinkText.Translate("status.offline");
    private String _activePeerDisplayName = ShadowLinkText.Translate("status.no_active_peer");
    private String _activePeerAddress = ShadowLinkText.Translate("status.choose_peer");
    private String _transportSummary = ShadowLinkText.Translate("status.auto");
    private String _activePeerOperatingSystem = String.Empty;
    private String _manualPeerName = String.Empty;
    private String _manualPeerAddress = String.Empty;
    private String _manualPeerPortText = DefaultControlPortText;
    private String _streamWidthText = DefaultStreamWidthText;
    private String _streamHeightText = DefaultStreamHeightText;
    private String _streamFrameRateText = DefaultFrameRateText;
    private String _streamTileSizeText = DefaultTileSizeText;
    private String _streamDictionarySizeMbText = DefaultDictionarySizeMbText;
    private String _streamStaticCodebookSharePercentText = DefaultPatternsPercentText;
    private ConnectionDirection _selectedDirection = ConnectionDirection.Receive;
    private TransportPreference _selectedTransport = TransportPreference.Auto;
    private StreamColorMode _selectedStreamColorMode = StreamColorMode.Bgr24;
    private RemoteDisplayScaleMode _selectedDisplayScaleMode = RemoteDisplayScaleMode.ZoomToFit;
    private Boolean _enableKeyboardRelay = true;
    private Boolean _enableMouseRelay = true;
    private Boolean _autoStartDiscovery = true;
    private Boolean _rememberRecentPeers = true;
    private Boolean _useManualConnection;
    private Boolean _isRefreshingDiscovery;
    private Boolean _isConnectingToPeer;
    private Boolean _isWaitingForInboundPeer;
    private Boolean _isDisconnectingSession;
    private Boolean _isSessionConnected;
    private Boolean _requiresConnectionPassphrase;
    private Boolean _canShareLocalDesktop = true;
    private Int32 _currentStepIndex;
    private PlatformFamily _activePeerPlatformFamily;
    private FirewallConfigurationStatus _firewallStatus = FirewallConfigurationStatus.Hidden;
    private String _shareSupportDetail = String.Empty;
    private IReadOnlyList<DiscoveryNetworkEndpoint> _localNetworkEndpoints = Array.Empty<DiscoveryNetworkEndpoint>();
    private DirectCableStatus _localDirectCableStatus = new DirectCableStatus();
    private Int32 _localNetworkStateVersion;
    private Boolean _hasLoadedLocalNetworkState;
    private Boolean _isConfiguringFirewall;
    private Boolean _isSavingSettings;
    private Boolean _lastSettingsSaveSucceeded = true;
    private String? _shareDiscoveryWarning;
    private Boolean _isShareDiscoveryStarting;
    private CancellationTokenSource? _launchOperationCts;
    private CancellationTokenSource? _shareAncillaryWorkCts;

    public MainWindowViewModel(
        ISettingsRepository settingsRepository,
        IDeviceDiscoveryService deviceDiscoveryService,
        ISessionCoordinator sessionCoordinator,
        IUiDispatcher uiDispatcher,
        IFirewallConfigurationService firewallConfigurationService,
        IAppInteractionService appInteractionService)
    {
        _settingsRepository = settingsRepository;
        _deviceDiscoveryService = deviceDiscoveryService;
        _sessionCoordinator = sessionCoordinator;
        _uiDispatcher = uiDispatcher;
        _firewallConfigurationService = firewallConfigurationService;
        _appInteractionService = appInteractionService;
        _settings = AppSettings.CreateDefault();
        _lastPrimaryStepIndex = 0;

        DeviceCards = new ObservableCollection<DeviceCardViewModel>();
        ShortcutBindings = new ObservableCollection<ShortcutBindingViewModel>();
        ActivityEntries = new ObservableCollection<ActivityEntry>();

        RefreshDiscoveryCommand = new AsyncRelayCommand(RefreshDiscoveryAsync);
        SaveProfileCommand = new AsyncRelayCommand(SaveProfileAsync);
        PrepareSendCommand = new RelayCommand(SetSendMode);
        PrepareReceiveCommand = new RelayCommand(SetReceiveMode);
        UseDiscoveryEntryCommand = new RelayCommand(UseDiscoveryEntry);
        UseManualEntryCommand = new RelayCommand(UseManualEntry);
        SetAutoTransportCommand = new RelayCommand(() => SetTransport(TransportPreference.Auto));
        SetNetworkTransportCommand = new RelayCommand(() => SetTransport(TransportPreference.Network));
        SetUsbTransportCommand = new RelayCommand(() => SetTransport(TransportPreference.UsbCNetwork));
        GoToRoleStepCommand = new RelayCommand(() => SetCurrentStep(RoleStepIndex));
        _goToPeerStepCommand = new RelayCommand(() => SetCurrentStep(PeerStepIndex), () => IsPeerStepAvailable);
        _goToLaunchStepCommand = new RelayCommand(() => SetCurrentStep(LaunchStepIndex));
        _goToAdvancedStepCommand = new RelayCommand(() => OpenSecondaryStep(AdvancedStepIndex));
        _goToToolsStepCommand = new RelayCommand(() => OpenSecondaryStep(ToolsStepIndex), () => IsToolsAvailable);
        GoToOptionsStepCommand = new RelayCommand(() => OpenSecondaryStep(OptionsStepIndex));
        _previousStepCommand = new RelayCommand(MoveToPreviousStep, () => CurrentStepIndex > 0);
        _nextStepCommand = new RelayCommand(MoveToNextStep, CanMoveToNextStep);
        _disconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => _isSessionConnected);
        _connectCommand = new AsyncRelayCommand(ConnectAsync, CanStartSession);
        _stopShareCommand = new AsyncRelayCommand(StopShareAsync, CanStopSharing);
        _configureFirewallCommand = new AsyncRelayCommand(ConfigureFirewallAsync, () => IsFirewallActionVisible);
        _closeOptionsCommand = new AsyncRelayCommand(CloseOptionsAsync);
        _cancelLaunchCommand = new RelayCommand(CancelLaunch, CanCancelLaunch);
        _applyNativePresetCommand = new RelayCommand(ApplyNativeResolutionPreset);
        _applyFullHdPresetCommand = new RelayCommand(() => ApplyStreamResolutionPreset(1920, 1080));
        _applyHdPresetCommand = new RelayCommand(() => ApplyStreamResolutionPreset(1280, 720));
        _applyLaptopPresetCommand = new RelayCommand(() => ApplyStreamResolutionPreset(1366, 768));
        _applySvgaPresetCommand = new RelayCommand(() => ApplyStreamResolutionPreset(800, 600));
        _setBgra32ColorModeCommand = new RelayCommand(() => SetStreamColorMode(StreamColorMode.Bgra32));
        _setBgr24ColorModeCommand = new RelayCommand(() => SetStreamColorMode(StreamColorMode.Bgr24));
        _setRgb565ColorModeCommand = new RelayCommand(() => SetStreamColorMode(StreamColorMode.Rgb565));
        _setRgb332ColorModeCommand = new RelayCommand(() => SetStreamColorMode(StreamColorMode.Rgb332));
        _setIndexed4ColorModeCommand = new RelayCommand(() => SetStreamColorMode(StreamColorMode.Indexed4));
        _applyTile8PresetCommand = new RelayCommand(() => ApplyTileSizePreset(8));
        _applyTile16PresetCommand = new RelayCommand(() => ApplyTileSizePreset(16));
        _applyTile32PresetCommand = new RelayCommand(() => ApplyTileSizePreset(32));
        _applyDictionary512PresetCommand = new RelayCommand(() => ApplyDictionarySizePreset(512));
        _applyDictionary1024PresetCommand = new RelayCommand(() => ApplyDictionarySizePreset(1024));
        _applyDictionary2048PresetCommand = new RelayCommand(() => ApplyDictionarySizePreset(2048));
        _applyDictionary4096PresetCommand = new RelayCommand(() => ApplyDictionarySizePreset(4096));
        _applyDictionary8192PresetCommand = new RelayCommand(() => ApplyDictionarySizePreset(8192));
        _applyDictionary16384PresetCommand = new RelayCommand(() => ApplyDictionarySizePreset(16384));
        _setDisplayScaleFitCommand = new RelayCommand(() => SetDisplayScaleMode(RemoteDisplayScaleMode.Fit));
        _setDisplayScaleZoomToFitCommand = new RelayCommand(() => SetDisplayScaleMode(RemoteDisplayScaleMode.ZoomToFit));
        _setDisplayScaleFillCommand = new RelayCommand(() => SetDisplayScaleMode(RemoteDisplayScaleMode.Fill));
        _openRemoteStartMenuCommand = new AsyncRelayCommand(() => SendRemoteToolAsync("remote.tool.start_menu", "LWin"), () => IsToolsAvailable);
        _openRemoteRunDialogCommand = new AsyncRelayCommand(() => SendRemoteToolAsync("remote.tool.run", "LWin", "R"), () => IsToolsAvailable);
        _openRemoteExplorerCommand = new AsyncRelayCommand(() => SendRemoteToolAsync("remote.tool.explorer", "LWin", "E"), () => IsToolsAvailable);
        _openRemoteSettingsCommand = new AsyncRelayCommand(() => SendRemoteToolAsync("remote.tool.settings", "LWin", "I"), () => IsToolsAvailable);
        _openRemoteTaskManagerCommand = new AsyncRelayCommand(() => SendRemoteToolAsync("remote.tool.task_manager", "Ctrl", "Shift", "Escape"), () => IsToolsAvailable);
        _openRemoteKeyboardCommand = new AsyncRelayCommand(() => SendRemoteToolAsync("remote.tool.keyboard", "LWin", "Ctrl", "O"), () => IsToolsAvailable);
        _openRemoteSpotlightCommand = new AsyncRelayCommand(() => SendRemoteToolAsync("remote.tool.spotlight", "Meta", "Space"), () => IsToolsAvailable);
        _openRemoteFinderCommand = new AsyncRelayCommand(() => SendRemoteToolAsync("remote.tool.finder", "Meta", "N"), () => IsToolsAvailable);
        _openRemoteMissionControlCommand = new AsyncRelayCommand(() => SendRemoteToolAsync("remote.tool.mission_control", "Ctrl", "Up"), () => IsToolsAvailable);
        _openRemoteForceQuitCommand = new AsyncRelayCommand(() => SendRemoteToolAsync("remote.tool.force_quit", "Meta", "Alt", "Escape"), () => IsToolsAvailable);
        _openRemoteTerminalCommand = new AsyncRelayCommand(() => SendRemoteToolAsync("remote.tool.terminal", "Ctrl", "Alt", "T"), () => IsToolsAvailable);
        _openRemoteAppSwitcherCommand = new AsyncRelayCommand(OpenRemoteAppSwitcherAsync, () => IsToolsAvailable);
        _lockRemoteScreenCommand = new AsyncRelayCommand(LockRemoteScreenAsync, () => IsToolsAvailable);
        _sendFilesCommand = new AsyncRelayCommand(SendFilesAsync, () => IsToolsAvailable);
        _requestFilesFromPeerCommand = new AsyncRelayCommand(RequestFilesFromPeerAsync, () => IsToolsAvailable);

        _deviceDiscoveryService.DevicesChanged += HandleDevicesChanged;
        _sessionCoordinator.StateChanged += HandleSessionStateChanged;
        QueueLocalNetworkStateRefresh();
    }

    public String AppTitle => T("app.title");

    public String AppSubtitle => T("app.subtitle");

    public String ShellCaption => SelectedDirection == ConnectionDirection.Send
        ? T("app.shell.share")
        : T("app.shell.control");

    public Boolean IsShareFlowVisible => SelectedDirection == ConnectionDirection.Send;

    public Boolean IsControlFlowVisible => SelectedDirection == ConnectionDirection.Receive;

    public String StatusTitle
    {
        get => _statusTitle;
        private set
        {
            if (SetProperty(ref _statusTitle, value))
            {
                OnPropertyChanged(nameof(FooterStatusTitle));
            }
        }
    }

    public String StatusDetail
    {
        get => _statusDetail;
        private set
        {
            if (SetProperty(ref _statusDetail, value))
            {
                OnPropertyChanged(nameof(FooterStatusDetail));
            }
        }
    }

    public String ListenerSummary
    {
        get => _listenerSummary;
        private set => SetProperty(ref _listenerSummary, value);
    }

    public String ActivePeerDisplayName
    {
        get => _activePeerDisplayName;
        private set => SetProperty(ref _activePeerDisplayName, value);
    }

    public String ActivePeerAddress
    {
        get => _activePeerAddress;
        private set => SetProperty(ref _activePeerAddress, value);
    }

    public String TransportSummary
    {
        get => _transportSummary;
        private set => SetProperty(ref _transportSummary, value);
    }

    public String DisplayName
    {
        get => _displayName;
        set
        {
            if (SetProperty(ref _displayName, value))
            {
                OnPropertyChanged(nameof(LocalShareName));
                OnPropertyChanged(nameof(LocalShareSummary));
            }
        }
    }

    public String DiscoveryPortText
    {
        get => _discoveryPortText;
        set
        {
            if (SetProperty(ref _discoveryPortText, value))
            {
                OnPropertyChanged(nameof(LocalShareSummary));
            }
        }
    }

    public String ControlPortText
    {
        get => _controlPortText;
        set
        {
            if (SetProperty(ref _controlPortText, value))
            {
                OnPropertyChanged(nameof(LocalSharePort));
                OnPropertyChanged(nameof(LocalShareSummary));
                OnPropertyChanged(nameof(ManualConnectionHint));
            }
        }
    }

    public String SessionPassphrase
    {
        get => _sessionPassphrase;
        set
        {
            if (SetProperty(ref _sessionPassphrase, value))
            {
                OnPropertyChanged(nameof(ShareSecuritySummary));
                OnPropertyChanged(nameof(ShareAccessHint));
                OnPropertyChanged(nameof(ConnectionPassphraseHint));
                OnPropertyChanged(nameof(AccessSummary));
            }
        }
    }

    public String ManualPeerName
    {
        get => _manualPeerName;
        set
        {
            if (SetProperty(ref _manualPeerName, value))
            {
                NotifyConnectionTargetChanged();
            }
        }
    }

    public String ManualPeerAddress
    {
        get => _manualPeerAddress;
        set
        {
            if (SetProperty(ref _manualPeerAddress, value))
            {
                NotifyConnectionTargetChanged();
            }
        }
    }

    public String ManualPeerPortText
    {
        get => _manualPeerPortText;
        set
        {
            if (SetProperty(ref _manualPeerPortText, value))
            {
                NotifyConnectionTargetChanged();
            }
        }
    }

    public String StreamWidthText
    {
        get => _streamWidthText;
        set
        {
            if (SetProperty(ref _streamWidthText, value))
            {
                OnPropertyChanged(nameof(StreamResolutionSummary));
                OnPropertyChanged(nameof(ReviewResolutionSummary));
                OnPropertyChanged(nameof(StreamSettingsSummary));
                OnPropertyChanged(nameof(LaunchStageBody));
                OnPropertyChanged(nameof(IsNativePresetSelected));
                OnPropertyChanged(nameof(IsFullHdPresetSelected));
                OnPropertyChanged(nameof(IsHdPresetSelected));
                OnPropertyChanged(nameof(IsLaptopPresetSelected));
                OnPropertyChanged(nameof(IsSvgaPresetSelected));
            }
        }
    }

    public String StreamHeightText
    {
        get => _streamHeightText;
        set
        {
            if (SetProperty(ref _streamHeightText, value))
            {
                OnPropertyChanged(nameof(StreamResolutionSummary));
                OnPropertyChanged(nameof(ReviewResolutionSummary));
                OnPropertyChanged(nameof(StreamSettingsSummary));
                OnPropertyChanged(nameof(LaunchStageBody));
                OnPropertyChanged(nameof(IsNativePresetSelected));
                OnPropertyChanged(nameof(IsFullHdPresetSelected));
                OnPropertyChanged(nameof(IsHdPresetSelected));
                OnPropertyChanged(nameof(IsLaptopPresetSelected));
                OnPropertyChanged(nameof(IsSvgaPresetSelected));
            }
        }
    }

    public String StreamFrameRateText
    {
        get => _streamFrameRateText;
        set
        {
            if (SetProperty(ref _streamFrameRateText, value))
            {
                OnPropertyChanged(nameof(CodecSummary));
            }
        }
    }

    public String StreamTileSizeText
    {
        get => _streamTileSizeText;
        set
        {
            if (SetProperty(ref _streamTileSizeText, value))
            {
                OnPropertyChanged(nameof(CodecSummary));
                OnPropertyChanged(nameof(IsTile8PresetSelected));
                OnPropertyChanged(nameof(IsTile16PresetSelected));
                OnPropertyChanged(nameof(IsTile32PresetSelected));
            }
        }
    }

    public String StreamDictionarySizeMbText
    {
        get => _streamDictionarySizeMbText;
        set
        {
            if (SetProperty(ref _streamDictionarySizeMbText, value))
            {
                OnPropertyChanged(nameof(CodecSummary));
                OnPropertyChanged(nameof(CodebookSplitSummary));
                OnPropertyChanged(nameof(IsDictionary512PresetSelected));
                OnPropertyChanged(nameof(IsDictionary1024PresetSelected));
                OnPropertyChanged(nameof(IsDictionary2048PresetSelected));
                OnPropertyChanged(nameof(IsDictionary4096PresetSelected));
                OnPropertyChanged(nameof(IsDictionary8192PresetSelected));
                OnPropertyChanged(nameof(IsDictionary16384PresetSelected));
            }
        }
    }

    public String StreamStaticCodebookSharePercentText
    {
        get => _streamStaticCodebookSharePercentText;
        set
        {
            if (SetProperty(ref _streamStaticCodebookSharePercentText, value))
            {
                OnPropertyChanged(nameof(CodecSummary));
                OnPropertyChanged(nameof(CodebookSplitSummary));
                OnPropertyChanged(nameof(StreamStaticCodebookSharePercent));
            }
        }
    }

    public Int32 StreamStaticCodebookSharePercent
    {
        get => ParseStaticCodebookSharePercent(StreamStaticCodebookSharePercentText, 50);
        set
        {
            Int32 clampedValue = Math.Clamp(value, 5, 95);
            String normalizedValue = clampedValue.ToString();
            if (String.Equals(StreamStaticCodebookSharePercentText, normalizedValue, StringComparison.Ordinal))
            {
                return;
            }

            StreamStaticCodebookSharePercentText = normalizedValue;
        }
    }

    public ConnectionDirection SelectedDirection
    {
        get => _selectedDirection;
        set
        {
            if (SetProperty(ref _selectedDirection, value))
            {
                OnPropertyChanged(nameof(DirectionSummary));
                OnPropertyChanged(nameof(RoleSummary));
                OnPropertyChanged(nameof(ShellCaption));
                OnPropertyChanged(nameof(IsShareFlowVisible));
                OnPropertyChanged(nameof(IsControlFlowVisible));
                OnPropertyChanged(nameof(IsSendModeSelected));
                OnPropertyChanged(nameof(IsReceiveModeSelected));
                OnPropertyChanged(nameof(IsPeerStepAvailable));
                OnPropertyChanged(nameof(ReviewHeadline));
                OnPropertyChanged(nameof(LaunchStageTitle));
                OnPropertyChanged(nameof(LaunchStepEyebrow));
                OnPropertyChanged(nameof(LaunchStageBody));
                OnPropertyChanged(nameof(ShareAccessHint));
                OnPropertyChanged(nameof(ConnectionPassphraseHint));
                OnPropertyChanged(nameof(AccessSummary));
                OnPropertyChanged(nameof(IsShareDetailsVisible));
                OnPropertyChanged(nameof(IsPassphrasePromptVisible));
                OnPropertyChanged(nameof(ReviewTargetSummary));
                OnPropertyChanged(nameof(CurrentStepSummary));
                OnPropertyChanged(nameof(NextStepButtonLabel));
                OnPropertyChanged(nameof(IsDiscoveryProgressVisible));
                OnPropertyChanged(nameof(DiscoveryProgressTitle));
                OnPropertyChanged(nameof(DiscoveryProgressDetail));
                OnPropertyChanged(nameof(IsSessionProgressVisible));
                OnPropertyChanged(nameof(SessionProgressTitle));
                OnPropertyChanged(nameof(SessionProgressDetail));
                OnPropertyChanged(nameof(IsControllerStreamSettingsVisible));
                OnPropertyChanged(nameof(IsShareStreamSettingsVisible));
                RefreshLaunchState();
                _goToPeerStepCommand.RaiseCanExecuteChanged();
                _goToToolsStepCommand.RaiseCanExecuteChanged();
                _nextStepCommand.RaiseCanExecuteChanged();
                _connectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public TransportPreference SelectedTransport
    {
        get => _selectedTransport;
        set
        {
            value = TransportPreference.Auto;
            if (SetProperty(ref _selectedTransport, value))
            {
                OnPropertyChanged(nameof(LocalAddressSummary));
                OnPropertyChanged(nameof(TransportModeSummary));
                OnPropertyChanged(nameof(IsAutoTransportSelected));
                OnPropertyChanged(nameof(IsNetworkTransportSelected));
                OnPropertyChanged(nameof(IsUsbTransportSelected));
                OnPropertyChanged(nameof(HasLocalThunderboltDirectLink));
                OnPropertyChanged(nameof(IsDirectCableStatusVisible));
                OnPropertyChanged(nameof(DirectCableStatusTitle));
                OnPropertyChanged(nameof(DirectCableStatusDetail));
                OnPropertyChanged(nameof(ShareAccessHint));
                OnPropertyChanged(nameof(ManualConnectionHint));
                OnPropertyChanged(nameof(ReviewRouteSummary));
                OnPropertyChanged(nameof(ReviewTargetSummary));
                OnPropertyChanged(nameof(LocalShareSummary));
                OnPropertyChanged(nameof(DiscoveryProgressDetail));
                OnPropertyChanged(nameof(NoDiscoveredDevicesHint));
                OnPropertyChanged(nameof(LaunchStageBody));
                OnPropertyChanged(nameof(SessionProgressDetail));
                SynchronizeDevices();
            }
        }
    }

    public StreamColorMode SelectedStreamColorMode
    {
        get => _selectedStreamColorMode;
        set
        {
            if (SetProperty(ref _selectedStreamColorMode, value))
            {
                OnPropertyChanged(nameof(CodecSummary));
                OnPropertyChanged(nameof(IsBgra32ColorModeSelected));
                OnPropertyChanged(nameof(IsBgr24ColorModeSelected));
                OnPropertyChanged(nameof(IsRgb565ColorModeSelected));
                OnPropertyChanged(nameof(IsRgb332ColorModeSelected));
                OnPropertyChanged(nameof(IsIndexed4ColorModeSelected));
            }
        }
    }

    public RemoteDisplayScaleMode SelectedDisplayScaleMode
    {
        get => _selectedDisplayScaleMode;
        set
        {
            if (SetProperty(ref _selectedDisplayScaleMode, value))
            {
                OnPropertyChanged(nameof(IsDisplayScaleFitSelected));
                OnPropertyChanged(nameof(IsDisplayScaleZoomToFitSelected));
                OnPropertyChanged(nameof(IsDisplayScaleFillSelected));
                OnPropertyChanged(nameof(ReviewResolutionSummary));
                OnPropertyChanged(nameof(StreamSettingsSummary));
                OnPropertyChanged(nameof(CodecSummary));
            }
        }
    }

    public Boolean EnableKeyboardRelay
    {
        get => _enableKeyboardRelay;
        set => SetProperty(ref _enableKeyboardRelay, value);
    }

    public Boolean EnableMouseRelay
    {
        get => _enableMouseRelay;
        set => SetProperty(ref _enableMouseRelay, value);
    }

    public Boolean AutoStartDiscovery
    {
        get => _autoStartDiscovery;
        set => SetProperty(ref _autoStartDiscovery, value);
    }

    public Boolean RememberRecentPeers
    {
        get => _rememberRecentPeers;
        set => SetProperty(ref _rememberRecentPeers, value);
    }

    public Boolean IsSendModeSelected
    {
        get => SelectedDirection == ConnectionDirection.Send;
        set
        {
            if (value)
            {
                SetSendMode();
            }
        }
    }

    public Boolean IsReceiveModeSelected
    {
        get => SelectedDirection == ConnectionDirection.Receive;
        set
        {
            if (value)
            {
                SetReceiveMode();
            }
        }
    }

    public Boolean IsAutoTransportSelected
    {
        get => SelectedTransport == TransportPreference.Auto;
        set
        {
            if (value)
            {
                SetTransport(TransportPreference.Auto);
            }
        }
    }

    public Boolean IsNetworkTransportSelected
    {
        get => SelectedTransport == TransportPreference.Network;
        set
        {
            if (value)
            {
                SetTransport(TransportPreference.Network);
            }
        }
    }

    public Boolean IsUsbTransportSelected
    {
        get => SelectedTransport == TransportPreference.UsbCNetwork;
        set
        {
            if (value)
            {
                SetTransport(TransportPreference.UsbCNetwork);
            }
        }
    }

    public Boolean IsBgra32ColorModeSelected => SelectedStreamColorMode == StreamColorMode.Bgra32;

    public Boolean IsBgr24ColorModeSelected => SelectedStreamColorMode == StreamColorMode.Bgr24;

    public Boolean IsRgb565ColorModeSelected => SelectedStreamColorMode == StreamColorMode.Rgb565;

    public Boolean IsRgb332ColorModeSelected => SelectedStreamColorMode == StreamColorMode.Rgb332;

    public Boolean IsIndexed4ColorModeSelected => SelectedStreamColorMode == StreamColorMode.Indexed4;

    public Boolean IsDisplayScaleFitSelected => SelectedDisplayScaleMode == RemoteDisplayScaleMode.Fit;

    public Boolean IsDisplayScaleZoomToFitSelected => SelectedDisplayScaleMode == RemoteDisplayScaleMode.ZoomToFit;

    public Boolean IsDisplayScaleFillSelected => SelectedDisplayScaleMode == RemoteDisplayScaleMode.Fill;

    public Boolean IsDiscoveryConnectionSelected
    {
        get => !_useManualConnection;
        set
        {
            if (value)
            {
                UseDiscoveryEntry();
            }
        }
    }

    public Boolean IsManualConnectionSelected
    {
        get => _useManualConnection;
        set
        {
            if (value)
            {
                UseManualEntry();
            }
        }
    }

    public DeviceCardViewModel? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                if (value is not null)
                {
                    _selectedDiscoveryTarget = value.Device;
                    _useManualConnection = false;
                    OnPropertyChanged(nameof(IsDiscoveryConnectionSelected));
                    OnPropertyChanged(nameof(IsManualConnectionSelected));
                }

                NotifyConnectionTargetChanged();
            }
        }
    }

    public Int32 CurrentStepIndex
    {
        get => _currentStepIndex;
        private set
        {
            if (SetProperty(ref _currentStepIndex, value))
            {
                OnPropertyChanged(nameof(CurrentStepLabel));
                OnPropertyChanged(nameof(CurrentStepTitle));
                OnPropertyChanged(nameof(CurrentStepSummary));
                OnPropertyChanged(nameof(IsRoleStepVisible));
                OnPropertyChanged(nameof(IsPeerStepVisible));
                OnPropertyChanged(nameof(IsLaunchStepVisible));
                OnPropertyChanged(nameof(IsOptionsStepVisible));
                OnPropertyChanged(nameof(IsAdvancedStepVisible));
                OnPropertyChanged(nameof(IsToolsStepVisible));
                OnPropertyChanged(nameof(NextStepButtonLabel));
                OnPropertyChanged(nameof(IsRoleStepSelected));
                OnPropertyChanged(nameof(IsPeerStepSelected));
                OnPropertyChanged(nameof(IsLaunchStepSelected));
                OnPropertyChanged(nameof(IsOptionsStepSelected));
                OnPropertyChanged(nameof(IsAdvancedStepSelected));
                OnPropertyChanged(nameof(IsToolsStepSelected));
                OnPropertyChanged(nameof(FooterStatusTitle));
                OnPropertyChanged(nameof(FooterStatusDetail));
                _previousStepCommand.RaiseCanExecuteChanged();
                _nextStepCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public Boolean IsRoleStepSelected
    {
        get => CurrentStepIndex == RoleStepIndex;
        set
        {
            if (value)
            {
                SetCurrentStep(RoleStepIndex);
            }
        }
    }

    public Boolean IsPeerStepSelected
    {
        get => CurrentStepIndex == PeerStepIndex;
        set
        {
            if (value)
            {
                SetCurrentStep(PeerStepIndex);
            }
        }
    }

    public Boolean IsLaunchStepSelected
    {
        get => CurrentStepIndex == LaunchStepIndex;
        set
        {
            if (value)
            {
                SetCurrentStep(LaunchStepIndex);
            }
        }
    }

    public Boolean IsOptionsStepSelected
    {
        get => CurrentStepIndex == OptionsStepIndex;
        set
        {
            if (value)
            {
                SetCurrentStep(OptionsStepIndex);
            }
        }
    }

    public Boolean IsToolsStepSelected
    {
        get => CurrentStepIndex == ToolsStepIndex;
        set
        {
            if (value)
            {
                SetCurrentStep(ToolsStepIndex);
            }
        }
    }

    public Boolean IsAdvancedStepSelected
    {
        get => CurrentStepIndex == AdvancedStepIndex;
        set
        {
            if (value)
            {
                SetCurrentStep(AdvancedStepIndex);
            }
        }
    }

    public Boolean IsRoleStepVisible => CurrentStepIndex == RoleStepIndex;

    public Boolean IsPeerStepVisible => CurrentStepIndex == PeerStepIndex;

    public Boolean IsLaunchStepVisible => CurrentStepIndex == LaunchStepIndex;

    public Boolean IsOptionsStepVisible => CurrentStepIndex == OptionsStepIndex;

    public Boolean IsAdvancedStepVisible => CurrentStepIndex == AdvancedStepIndex;

    public Boolean IsToolsStepVisible => CurrentStepIndex == ToolsStepIndex;

    public Boolean IsControllerStreamSettingsVisible => SelectedDirection == ConnectionDirection.Receive;

    public Boolean IsShareStreamSettingsVisible => SelectedDirection == ConnectionDirection.Send;

    public ObservableCollection<DeviceCardViewModel> DeviceCards { get; }

    public ObservableCollection<ShortcutBindingViewModel> ShortcutBindings { get; }

    public ObservableCollection<ActivityEntry> ActivityEntries { get; }

    public AsyncRelayCommand RefreshDiscoveryCommand { get; }

    public AsyncRelayCommand SaveProfileCommand { get; }

    public RelayCommand PrepareSendCommand { get; }

    public RelayCommand PrepareReceiveCommand { get; }

    public RelayCommand UseDiscoveryEntryCommand { get; }

    public RelayCommand UseManualEntryCommand { get; }

    public RelayCommand SetAutoTransportCommand { get; }

    public RelayCommand SetNetworkTransportCommand { get; }

    public RelayCommand SetUsbTransportCommand { get; }

    public RelayCommand GoToRoleStepCommand { get; }

    public RelayCommand GoToPeerStepCommand => _goToPeerStepCommand;

    public RelayCommand GoToLaunchStepCommand => _goToLaunchStepCommand;

    public RelayCommand GoToOptionsStepCommand { get; }

    public RelayCommand GoToAdvancedStepCommand => _goToAdvancedStepCommand;

    public RelayCommand GoToToolsStepCommand => _goToToolsStepCommand;

    public RelayCommand NextStepCommand => _nextStepCommand;

    public RelayCommand PreviousStepCommand => _previousStepCommand;

    public AsyncRelayCommand ConnectCommand => _connectCommand;

    public AsyncRelayCommand StopShareCommand => _stopShareCommand;

    public AsyncRelayCommand DisconnectCommand => _disconnectCommand;

    public AsyncRelayCommand ConfigureFirewallCommand => _configureFirewallCommand;

    public AsyncRelayCommand CloseOptionsCommand => _closeOptionsCommand;

    public RelayCommand CancelLaunchCommand => _cancelLaunchCommand;

    public RelayCommand ApplyNativePresetCommand => _applyNativePresetCommand;

    public RelayCommand ApplyFullHdPresetCommand => _applyFullHdPresetCommand;

    public RelayCommand ApplyHdPresetCommand => _applyHdPresetCommand;

    public RelayCommand ApplyLaptopPresetCommand => _applyLaptopPresetCommand;

    public RelayCommand ApplySvgaPresetCommand => _applySvgaPresetCommand;

    public RelayCommand SetBgra32ColorModeCommand => _setBgra32ColorModeCommand;

    public RelayCommand SetBgr24ColorModeCommand => _setBgr24ColorModeCommand;

    public RelayCommand SetRgb565ColorModeCommand => _setRgb565ColorModeCommand;

    public RelayCommand SetRgb332ColorModeCommand => _setRgb332ColorModeCommand;

    public RelayCommand SetIndexed4ColorModeCommand => _setIndexed4ColorModeCommand;

    public RelayCommand ApplyTile8PresetCommand => _applyTile8PresetCommand;

    public RelayCommand ApplyTile16PresetCommand => _applyTile16PresetCommand;

    public RelayCommand ApplyTile32PresetCommand => _applyTile32PresetCommand;

    public RelayCommand ApplyDictionary512PresetCommand => _applyDictionary512PresetCommand;

    public RelayCommand ApplyDictionary1024PresetCommand => _applyDictionary1024PresetCommand;

    public RelayCommand ApplyDictionary2048PresetCommand => _applyDictionary2048PresetCommand;

    public RelayCommand ApplyDictionary4096PresetCommand => _applyDictionary4096PresetCommand;

    public RelayCommand ApplyDictionary8192PresetCommand => _applyDictionary8192PresetCommand;

    public RelayCommand ApplyDictionary16384PresetCommand => _applyDictionary16384PresetCommand;

    public RelayCommand SetDisplayScaleFitCommand => _setDisplayScaleFitCommand;

    public RelayCommand SetDisplayScaleZoomToFitCommand => _setDisplayScaleZoomToFitCommand;

    public RelayCommand SetDisplayScaleFillCommand => _setDisplayScaleFillCommand;

    public AsyncRelayCommand OpenRemoteStartMenuCommand => _openRemoteStartMenuCommand;

    public AsyncRelayCommand OpenRemoteRunDialogCommand => _openRemoteRunDialogCommand;

    public AsyncRelayCommand OpenRemoteExplorerCommand => _openRemoteExplorerCommand;

    public AsyncRelayCommand OpenRemoteSettingsCommand => _openRemoteSettingsCommand;

    public AsyncRelayCommand OpenRemoteTaskManagerCommand => _openRemoteTaskManagerCommand;

    public AsyncRelayCommand OpenRemoteKeyboardCommand => _openRemoteKeyboardCommand;

    public AsyncRelayCommand OpenRemoteSpotlightCommand => _openRemoteSpotlightCommand;

    public AsyncRelayCommand OpenRemoteFinderCommand => _openRemoteFinderCommand;

    public AsyncRelayCommand OpenRemoteMissionControlCommand => _openRemoteMissionControlCommand;

    public AsyncRelayCommand OpenRemoteForceQuitCommand => _openRemoteForceQuitCommand;

    public AsyncRelayCommand OpenRemoteTerminalCommand => _openRemoteTerminalCommand;

    public AsyncRelayCommand OpenRemoteAppSwitcherCommand => _openRemoteAppSwitcherCommand;

    public AsyncRelayCommand LockRemoteScreenCommand => _lockRemoteScreenCommand;

    public AsyncRelayCommand SendFilesCommand => _sendFilesCommand;

    public AsyncRelayCommand RequestFilesFromPeerCommand => _requestFilesFromPeerCommand;

    public String CurrentStepLabel => CurrentStepIndex switch
    {
        OptionsStepIndex => T("common.settings"),
        AdvancedStepIndex => T("common.advanced"),
        ToolsStepIndex => T("common.tools"),
        _ => F("wizard.step_of", CurrentStepIndex + 1, 3)
    };

    public String CurrentStepTitle => CurrentStepIndex switch
    {
        RoleStepIndex => T("wizard.role.title"),
        PeerStepIndex => T("wizard.peer.title"),
        LaunchStepIndex => T("wizard.launch.title"),
        OptionsStepIndex => T("common.settings"),
        AdvancedStepIndex => T("common.advanced"),
        ToolsStepIndex => T("stage.tools.title"),
        _ => T("wizard.role.title")
    };

    public String CurrentStepSummary => CurrentStepIndex switch
    {
        RoleStepIndex => T("wizard.role.summary"),
        PeerStepIndex => T("wizard.peer.summary"),
        LaunchStepIndex => SelectedDirection == ConnectionDirection.Send ? T("wizard.launch.share_summary") : T("wizard.launch.connect_summary"),
        OptionsStepIndex => T("wizard.settings.summary"),
        AdvancedStepIndex => T("wizard.advanced.summary"),
        ToolsStepIndex => T("wizard.tools.summary"),
        _ => T("wizard.role.summary")
    };

    public String NextStepButtonLabel => CurrentStepIndex switch
    {
        RoleStepIndex => SelectedDirection == ConnectionDirection.Send ? T("wizard.next.review_share") : T("wizard.next.choose_peer"),
        PeerStepIndex => T("wizard.next.review_session"),
        LaunchStepIndex => T("common.advanced"),
        _ => T("common.next")
    };

    public String KnownDeviceSummary => DeviceCards.Count switch
    {
        0 => T("devices.count.none"),
        1 => T("devices.count.one"),
        _ => F("devices.count.many", DeviceCards.Count)
    };

    public Boolean HasDiscoveredDevices => DeviceCards.Count > 0;

    public Boolean HasNoDiscoveredDevices => !HasDiscoveredDevices;

    public Boolean IsPeerStepAvailable => SelectedDirection == ConnectionDirection.Receive;

    public Boolean IsToolsAvailable => IsConnected && SelectedDirection == ConnectionDirection.Receive;

    public Boolean IsWindowsRemoteToolsVisible => IsToolsAvailable && _activePeerPlatformFamily == PlatformFamily.Windows;

    public Boolean IsMacRemoteToolsVisible => IsToolsAvailable && _activePeerPlatformFamily == PlatformFamily.MacOS;

    public Boolean IsLinuxRemoteToolsVisible => IsToolsAvailable && _activePeerPlatformFamily == PlatformFamily.Linux;

    public Boolean IsUnknownRemoteToolsVisible => IsToolsAvailable && _activePeerPlatformFamily == PlatformFamily.Unknown;

    public Boolean HasConnectionTarget => _useManualConnection
        ? !String.IsNullOrWhiteSpace(ManualPeerAddress)
        : SelectedDevice is not null || _selectedDiscoveryTarget is not null;

    public String DirectionSummary => SelectedDirection == ConnectionDirection.Send
        ? T("mode.share.title")
        : T("mode.control.title");

    public Boolean IsShareDetailsVisible => SelectedDirection == ConnectionDirection.Send;

    public Boolean IsShareSupportWarningVisible => SelectedDirection == ConnectionDirection.Send && !_canShareLocalDesktop;

    public Boolean IsPassphrasePromptVisible => SelectedDirection == ConnectionDirection.Receive && _requiresConnectionPassphrase;

    public String RoleSummary => SelectedDirection == ConnectionDirection.Send
        ? T("role.summary.share")
        : T("role.summary.control");

    public String ShareSupportDetail => _shareSupportDetail;

    public String LocalShareName => String.IsNullOrWhiteSpace(DisplayName)
        ? Environment.MachineName
        : DisplayName.Trim();

    public String LocalHostName => Environment.MachineName;

    public String LocalAddressSummary => BuildLocalAddressSummary();

    public String LocalSharePort => ParsePort(ControlPortText, 45211).ToString();

    public String LocalShareSummary
    {
        get
        {
            if (!_hasLoadedLocalNetworkState)
            {
                return "Checking reachable addresses for port " + LocalSharePort + ".";
            }

            if (HasReachableLocalShareEndpoint())
            {
                return LocalAddressSummary + " via port " + LocalSharePort + ".";
            }

            if (_localDirectCableStatus.HasCompatibleInterface && !_localDirectCableStatus.HasLink)
            {
                return "Waiting for the Thunderbolt/USB4 direct cable link before this machine can be reached.";
            }

            if (_localDirectCableStatus.HasLink && !_localDirectCableStatus.HasUsableNetworkPath)
            {
                return "Waiting for the direct cable to receive a usable network address.";
            }

            return "No reachable network address detected yet. Connect Wi-Fi, Ethernet, or a direct cable. ShadowLink will update automatically.";
        }
    }

    public Boolean HasLocalThunderboltDirectLink => _localDirectCableStatus.HasUsableNetworkPath;

    public Boolean IsDirectCableStatusVisible => _localDirectCableStatus.HasCompatibleInterface;

    public String DirectCableStatusTitle
    {
        get
        {
            DirectCableStatus status = _localDirectCableStatus;
            if (status.HasUsableNetworkPath)
            {
                return "Thunderbolt/USB4 direct link ready";
            }

            return status.HasLink
                ? "Thunderbolt/USB4 link detected"
                : status.HasCompatibleInterface
                    ? "Thunderbolt/USB4 interface found"
                    : "Thunderbolt/USB4 direct link not detected";
        }
    }

    public String DirectCableStatusDetail
    {
        get
        {
            DirectCableStatus status = _localDirectCableStatus;
            if (status.HasUsableNetworkPath)
            {
                String endpointSummary = String.Join(", ", status.Endpoints.Select(item => item.Address + " (" + item.InterfaceName + ")"));
                return "ShadowLink detected a ready Thunderbolt/USB4 direct network path on " + endpointSummary + ". Scan and connection attempts in direct mode will stay on this path.";
            }

            if (status.HasLink)
            {
                String interfaceSummary = String.Join(", ", status.InterfaceNames);
                return "Thunderbolt/USB4 networking is linked on " + interfaceSummary + ", but this machine does not have a usable direct IP address yet. Wait for the OS to finish creating the direct network path, then refresh.";
            }

            if (status.HasCompatibleInterface)
            {
                String interfaceSummary = String.Join(", ", status.InterfaceNames);
                return "Thunderbolt/USB4 networking support was found on " + interfaceSummary + ", but the direct cable link is not up yet. Connect the cable and make sure Thunderbolt Bridge or Thunderbolt Networking is enabled in the OS.";
            }

            return "No Thunderbolt/USB4 networking interface is visible on this machine yet. Enable Thunderbolt Bridge or Thunderbolt Networking in the OS, then reconnect the cable.";
        }
    }

    public String ShareSecuritySummary => String.IsNullOrWhiteSpace(SessionPassphrase)
        ? T("share.security.open")
        : T("share.security.passphrase_required");

    public String ShareAccessHint => String.IsNullOrWhiteSpace(SessionPassphrase)
        ? _canShareLocalDesktop
            ? HasLocalThunderboltDirectLink
                ? "Anyone on the same reachable network path can request a session. A Thunderbolt/USB4 direct link is also available and will be preferred automatically."
                : "Anyone on the same reachable network path can request a session."
            : _shareSupportDetail
        : "Only peers with this passphrase can connect.";

    public String ConnectionModeSummary => _useManualConnection
        ? T("common.direct_address")
        : T("common.scan");

    public String SelectedDeviceSummary => !HasConnectionTarget
        ? "No target selected yet."
        : BuildConnectionTargetSummary();

    public String ManualConnectionHint => String.IsNullOrWhiteSpace(ManualPeerAddress)
        ? HasLocalThunderboltDirectLink
            ? "Enter an IP address or hostname. ShadowLink will prefer the ready Thunderbolt/USB4 direct path when it can."
            : "Enter an IP address or hostname."
        : "ShadowLink will connect to " + ManualPeerAddress.Trim() + ":" + ParsePort(ManualPeerPortText, 45211) + ".";

    public String ConnectionPassphraseHint => String.IsNullOrWhiteSpace(SessionPassphrase)
        ? "Leave blank unless the remote machine requires a passphrase."
        : "This passphrase will be sent when connecting.";

    public String PassphrasePromptTitle => T("dialog.passphrase.title");

    public String PassphrasePromptDetail => T("passphrase.prompt.detail");

    public String TransportModeSummary => T("transport.summary.automatic");

    public String ReviewHeadline => !HasConnectionTarget
        ? SelectedDirection == ConnectionDirection.Send
            ? (_canShareLocalDesktop ? "Adapters" : "Sharing unavailable")
            : "Select a target first"
        : SelectedDirection == ConnectionDirection.Send
            ? (_canShareLocalDesktop ? "Adapters" : "Sharing unavailable")
            : "Ready to connect";

    public String LaunchStageTitle => SelectedDirection == ConnectionDirection.Send
        ? (_canShareLocalDesktop ? "Ready to share" : "Sharing unavailable")
        : "Ready to connect";

    public String LaunchStepEyebrow => SelectedDirection == ConnectionDirection.Send ? T("step.2") : T("step.3");

    public String LaunchStageBody => SelectedDirection == ConnectionDirection.Send
        ? (_canShareLocalDesktop
            ? "Start sharing and wait for the other device to choose the session quality."
            : _shareSupportDetail)
        : "Review the target and connect. Streaming uses " + StreamResolutionSummary.ToLowerInvariant() + ".";

    public Boolean IsLaunchToolsVisible => IsToolsAvailable;

    public Boolean IsProtectedWindowsControlLimited => _appInteractionService.CanOfferElevatedControl;

    public String ProtectedWindowsControlSummary => _appInteractionService.CanOfferElevatedControl
        ? "Protected Windows surfaces on this machine need elevated control."
        : "Protected Windows surfaces are available.";

    public String RemoteToolsPlatformSummary => String.IsNullOrWhiteSpace(_activePeerOperatingSystem)
        ? "Connected toolset follows the remote platform."
        : "Connected to " + _activePeerOperatingSystem + ".";

    public String ReviewTargetSummary => SelectedDirection == ConnectionDirection.Send
        ? LocalShareSummary
        : HasConnectionTarget
            ? BuildConnectionTargetSummary()
            : "Choose a device or enter an address.";

    public String ReviewRouteSummary => "Route: Auto";

    public String AccessSummary => SelectedDirection == ConnectionDirection.Send
        ? String.IsNullOrWhiteSpace(SessionPassphrase)
            ? "No passphrase"
            : "Passphrase required"
        : _requiresConnectionPassphrase
            ? "Passphrase required"
            : String.IsNullOrWhiteSpace(SessionPassphrase)
                ? "No passphrase"
                : "Passphrase entered";

    public String StreamResolutionSummary => ParseDimension(StreamWidthText, 1280) + " x " + ParseDimension(StreamHeightText, 720) + " stream";

    public String CodebookSplitSummary
    {
        get
        {
            Int32 staticSharePercent = ParseStaticCodebookSharePercent(StreamStaticCodebookSharePercentText, 50);
            return "Patterns " + staticSharePercent + "%, learned reuse " + (100 - staticSharePercent) + "% of the total stream memory budget.";
        }
    }

    public String ReviewResolutionSummary => ParseDimension(StreamWidthText, 1280) + " x " + ParseDimension(StreamHeightText, 720) + ", " + BuildDisplayScaleLabel(SelectedDisplayScaleMode);

    public String StreamSettingsSummary => "Remote sessions open at " + ReviewResolutionSummary + ".";

    public String CodecSummary => ParseFrameRate(StreamFrameRateText, 30) + " fps, " +
        BuildColorModeLabel(SelectedStreamColorMode) + ", " +
        ParseTileSize(StreamTileSizeText, 16) + " x " + ParseTileSize(StreamTileSizeText, 16) + " tiles, " +
        BuildDictionarySizeLabel(ParseDictionarySizeMb(StreamDictionarySizeMbText, 1024)) + " dictionary, " +
        BuildDisplayScaleLabel(SelectedDisplayScaleMode);

    public Boolean IsNativePresetSelected
    {
        get
        {
            (Int32 nativeWidth, Int32 nativeHeight) = GetNativeStreamResolution();
            return ParseDimension(StreamWidthText, 1280) == nativeWidth && ParseDimension(StreamHeightText, 720) == nativeHeight;
        }
    }

    public Boolean IsFullHdPresetSelected => ParseDimension(StreamWidthText, 1280) == 1920 && ParseDimension(StreamHeightText, 720) == 1080;

    public Boolean IsHdPresetSelected => ParseDimension(StreamWidthText, 1280) == 1280 && ParseDimension(StreamHeightText, 720) == 720;

    public Boolean IsLaptopPresetSelected => ParseDimension(StreamWidthText, 1280) == 1366 && ParseDimension(StreamHeightText, 720) == 768;

    public Boolean IsSvgaPresetSelected => ParseDimension(StreamWidthText, 1280) == 800 && ParseDimension(StreamHeightText, 720) == 600;

    public Boolean IsTile8PresetSelected => ParseTileSize(StreamTileSizeText, 16) == 8;

    public Boolean IsTile16PresetSelected => ParseTileSize(StreamTileSizeText, 16) == 16;

    public Boolean IsTile32PresetSelected => ParseTileSize(StreamTileSizeText, 16) == 32;

    public Boolean IsDictionary512PresetSelected => ParseDictionarySizeMb(StreamDictionarySizeMbText, 1024) == 512;

    public Boolean IsDictionary1024PresetSelected => ParseDictionarySizeMb(StreamDictionarySizeMbText, 1024) == 1024;

    public Boolean IsDictionary2048PresetSelected => ParseDictionarySizeMb(StreamDictionarySizeMbText, 1024) == 2048;

    public Boolean IsDictionary4096PresetSelected => ParseDictionarySizeMb(StreamDictionarySizeMbText, 1024) == 4096;

    public Boolean IsDictionary8192PresetSelected => ParseDictionarySizeMb(StreamDictionarySizeMbText, 1024) == 8192;

    public Boolean IsDictionary16384PresetSelected => ParseDictionarySizeMb(StreamDictionarySizeMbText, 1024) == 16384;

    public Boolean IsConnected => _isSessionConnected;

    public Boolean IsDiscoveryProgressVisible => _isRefreshingDiscovery;

    public String DiscoveryProgressTitle => T("discovery.progress.title");

    public String DiscoveryProgressDetail => T("discovery.progress.detail");

    public String NoDiscoveredDevicesHint
    {
        get
        {
            DirectCableStatus status = _localDirectCableStatus;
            if (!status.HasCompatibleInterface)
            {
                return "No device found. Refresh or use Direct to enter a host or IP.";
            }

            if (!status.HasLink)
            {
                return "No device found. A Thunderbolt/USB4 interface exists on this machine, but the direct cable link is still down.";
            }

            if (!status.HasUsableNetworkPath)
            {
                return "No device found. The Thunderbolt/USB4 link is up, but this machine does not have a usable direct IP address yet.";
            }

            return "No device found yet. If you expected a direct-cable peer, make sure the other machine is on the same Thunderbolt/USB4 network path, then refresh.";
        }
    }
 

    public Boolean IsSessionProgressVisible => _isConnectingToPeer || _isWaitingForInboundPeer || _isDisconnectingSession;

    public String SessionProgressTitle => _isDisconnectingSession
        ? SelectedDirection == ConnectionDirection.Send
            ? "Stopping share"
            : "Disconnecting"
        : _isConnectingToPeer
            ? SelectedDirection == ConnectionDirection.Send
                ? "Preparing to share"
                : "Connecting now"
            : _isWaitingForInboundPeer
                ? BuildWaitingForPeerStatusTitle()
                : String.Empty;

    public String SessionProgressDetail => _isDisconnectingSession
        ? SelectedDirection == ConnectionDirection.Send
            ? "Stopping the listener, ending any active share session, and removing nearby discovery announcements."
            : "Closing the current session and returning the workspace to ready state."
        : _isConnectingToPeer
            ? SelectedDirection == ConnectionDirection.Send
                ? "ShadowLink is starting the share listener and preparing nearby discovery in the background."
                : "ShadowLink is opening the session and verifying access."
            : _isWaitingForInboundPeer
                ? BuildWaitingForPeerStatusDetail()
                : String.Empty;

    public Boolean IsFooterBusy => IsDiscoveryProgressVisible || IsSessionProgressVisible || _isSavingSettings || _isConfiguringFirewall;

    public String FooterStatusTitle => ShouldShowFooterStatus() ? StatusTitle : CurrentStepTitle;

    public String FooterStatusDetail => ShouldShowFooterStatus() ? StatusDetail : CurrentStepSummary;

    public String EscapeSummary => BuildEscapeSummary();

    public String PrimarySessionActionLabel => SelectedDirection == ConnectionDirection.Send ? T("common.start_sharing") : T("common.connect");

    public Boolean IsPrimarySessionActionVisible => !IsConnected &&
                                                    !_isDisconnectingSession &&
                                                    !_isConnectingToPeer &&
                                                    !(SelectedDirection == ConnectionDirection.Send && _isWaitingForInboundPeer);

    public Boolean IsStopShareVisible => SelectedDirection == ConnectionDirection.Send &&
                                         !_isDisconnectingSession &&
                                         (_isConnectingToPeer || _isWaitingForInboundPeer || IsConnected);

    public Boolean IsCancelLaunchVisible => SelectedDirection == ConnectionDirection.Receive &&
                                            _isConnectingToPeer &&
                                            !_isDisconnectingSession &&
                                            !IsConnected;

    public Boolean IsDisconnectVisible => SelectedDirection == ConnectionDirection.Receive && IsConnected && !_isDisconnectingSession;

    public Boolean IsLaunchBackVisible => !_isConnectingToPeer && !_isWaitingForInboundPeer && !_isDisconnectingSession && !IsConnected;

    public Boolean IsLaunchAdvancedVisible => !_isConnectingToPeer && !_isWaitingForInboundPeer && !_isDisconnectingSession && !IsConnected;

    public Boolean IsFirewallStatusVisible => _firewallStatus.IsVisible && !_firewallStatus.IsReady;

    public String FirewallStatusTitle => _firewallStatus.Title;

    public String FirewallStatusDetail => _firewallStatus.Detail;

    public Boolean IsFirewallActionVisible => _firewallStatus.IsVisible && _firewallStatus.IsSupported && !_firewallStatus.IsReady;

    public String FirewallActionLabel => String.IsNullOrWhiteSpace(_firewallStatus.ActionLabel) ? "Allow access" : _firewallStatus.ActionLabel;

    public async Task InitializeAsync()
    {
        CancellationToken cancellationToken = CancellationToken.None;
        _settings = await _settingsRepository.LoadAsync(cancellationToken).ConfigureAwait(false);
        NormalizeShortcutBindings(_settings);
        QueueLocalNetworkStateRefresh();

        _uiDispatcher.Post(() =>
        {
            ApplySettingsToUi(_settings);
            ApplySessionSnapshot(_sessionCoordinator.CurrentState);
            SetCurrentStep(0);
        });

        if (await _appInteractionService.PromptForElevatedStartupAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await _sessionCoordinator.StartAsync(_settings, cancellationToken).ConfigureAwait(false);

        if (_settings.AutoStartDiscovery && _settings.DefaultDirection == ConnectionDirection.Receive)
        {
            try
            {
                await _deviceDiscoveryService.StartAsync(_settings, cancellationToken).ConfigureAwait(false);
                await _deviceDiscoveryService.RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _uiDispatcher.Post(() =>
                {
                    ActivityEntries.Insert(0, new ActivityEntry(DateTimeOffset.UtcNow, "Discovery", ex.Message));
                    UpdateIdleStatusForCurrentContext();
                    RefreshLaunchState();
                });
            }
        }
        else
        {
            await _deviceDiscoveryService.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await RefreshFirewallStatusAsync(_settings, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _uiDispatcher.Post(() =>
            {
                ActivityEntries.Insert(0, new ActivityEntry(DateTimeOffset.UtcNow, "Firewall", ex.Message));
                UpdateIdleStatusForCurrentContext();
                RefreshLaunchState();
            });
        }
    }

    private async Task RefreshDiscoveryAsync()
    {
        QueueLocalNetworkStateRefresh();
        _uiDispatcher.Post(() =>
        {
            _isRefreshingDiscovery = true;
            RefreshLaunchState();
            OnPropertyChanged(nameof(IsDiscoveryProgressVisible));
            OnPropertyChanged(nameof(DiscoveryProgressTitle));
            OnPropertyChanged(nameof(DiscoveryProgressDetail));
        });
        await Task.Yield();

        try
        {
            AppSettings settings = BuildSettingsFromUi();
            await _deviceDiscoveryService.RestartAsync(settings, CancellationToken.None).ConfigureAwait(false);
            await _deviceDiscoveryService.RefreshAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _uiDispatcher.Post(() =>
            {
                StatusTitle = "Discovery refresh failed";
                StatusDetail = ex.Message;
                ActivityEntries.Insert(0, new ActivityEntry(DateTimeOffset.UtcNow, "Discovery", ex.Message));
                RefreshLaunchState();
            });
        }
        finally
        {
            _uiDispatcher.Post(() =>
            {
                _isRefreshingDiscovery = false;
                RefreshLaunchState();
            });
        }
    }

    private async Task SaveProfileAsync()
    {
        Boolean ownsSavingState = !_isSavingSettings;
        _lastSettingsSaveSucceeded = false;
        if (ownsSavingState)
        {
            _uiDispatcher.Post(() =>
            {
                _isSavingSettings = true;
                StatusTitle = "Saving settings";
                StatusDetail = "Applying the updated settings.";
                RefreshLaunchState();
            });
            await Task.Yield();
        }

        try
        {
            AppSettings pendingSettings = BuildSettingsFromUi();
            Boolean settingsChanged = !AreSettingsEquivalent(_settings, pendingSettings);
            AppSettings currentSettings = _settings;

            if (settingsChanged)
            {
                _settings = pendingSettings;
                await _settingsRepository.SaveAsync(_settings, CancellationToken.None).ConfigureAwait(false);
                Boolean requiresCoordinatorRestart = RequiresCoordinatorRestart(currentSettings, _settings);
                Boolean requiresDiscoveryRefresh = RequiresDiscoveryRefresh(currentSettings, _settings);
                Boolean requiresFirewallRefresh = RequiresFirewallRefresh(currentSettings, _settings);

                if (requiresCoordinatorRestart)
                {
                    await _sessionCoordinator.StartAsync(_settings, CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    await _sessionCoordinator.ApplySettingsAsync(_settings, CancellationToken.None).ConfigureAwait(false);
                }

                if (requiresDiscoveryRefresh)
                {
                    if (_settings.AutoStartDiscovery && SelectedDirection == ConnectionDirection.Receive)
                    {
                        await _deviceDiscoveryService.RestartAsync(_settings, CancellationToken.None).ConfigureAwait(false);
                        await _deviceDiscoveryService.RefreshAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    else
                    {
                        await _deviceDiscoveryService.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }

                if (requiresFirewallRefresh)
                {
                    await RefreshFirewallStatusAsync(_settings, CancellationToken.None).ConfigureAwait(false);
                }
            }

            _lastSettingsSaveSucceeded = true;
            _uiDispatcher.Post(() =>
            {
                ApplySessionSnapshot(_sessionCoordinator.CurrentState);
                OnPropertyChanged(nameof(EscapeSummary));
                OnPropertyChanged(nameof(TransportModeSummary));
                OnPropertyChanged(nameof(DirectionSummary));
                OnPropertyChanged(nameof(RoleSummary));
                OnPropertyChanged(nameof(ReviewRouteSummary));
            });

            if (!settingsChanged)
            {
                _uiDispatcher.Post(() =>
                {
                    StatusTitle = "Settings already applied";
                    StatusDetail = "Nothing changed, so ShadowLink kept the current session configuration.";
                    RefreshLaunchState();
                });
            }
            else if (!RequiresCoordinatorRestart(currentSettings, _settings) && !RequiresDiscoveryRefresh(currentSettings, _settings))
            {
                _uiDispatcher.Post(() =>
                {
                    StatusTitle = "Settings saved";
                    StatusDetail = "Advanced session settings were saved without restarting listeners or discovery.";
                    RefreshLaunchState();
                });
            }
        }
        catch (Exception ex)
        {
            _uiDispatcher.Post(() =>
            {
                StatusTitle = "Save failed";
                StatusDetail = ex.Message;
                ActivityEntries.Insert(0, new ActivityEntry(DateTimeOffset.UtcNow, "Settings", ex.Message));
                RefreshLaunchState();
            });
        }
        finally
        {
            if (ownsSavingState)
            {
                _uiDispatcher.Post(() =>
                {
                    _isSavingSettings = false;
                    RefreshLaunchState();
                });
            }
        }
    }

    private async Task CloseOptionsAsync()
    {
        Int32 returnStepIndex = _lastPrimaryStepIndex;
        await SaveProfileAsync().ConfigureAwait(false);
        _uiDispatcher.Post(() =>
        {
            if (_lastSettingsSaveSucceeded)
            {
                SetCurrentStep(returnStepIndex);
                UpdateIdleStatusForCurrentContext();
            }
        });
    }

    private void SetSendMode()
    {
        if (_isSessionConnected || _isConnectingToPeer || _isWaitingForInboundPeer || _isDisconnectingSession)
        {
            return;
        }

        _isConnectingToPeer = false;
        _isWaitingForInboundPeer = false;
        _isDisconnectingSession = false;
        _requiresConnectionPassphrase = false;
        _shareDiscoveryWarning = null;
        QueueLocalNetworkStateRefresh();
        SelectedDirection = ConnectionDirection.Send;
        QueueDiscoveryModeRefresh(ConnectionDirection.Send);
        UpdateIdleStatusForCurrentContext();
        RefreshLaunchState();
    }

    private void SetReceiveMode()
    {
        if (_isSessionConnected || _isConnectingToPeer || _isWaitingForInboundPeer || _isDisconnectingSession)
        {
            return;
        }

        _isConnectingToPeer = false;
        _isWaitingForInboundPeer = false;
        _isDisconnectingSession = false;
        _requiresConnectionPassphrase = false;
        _shareDiscoveryWarning = null;
        QueueLocalNetworkStateRefresh();
        SelectedDirection = ConnectionDirection.Receive;
        QueueDiscoveryModeRefresh(ConnectionDirection.Receive);
        UpdateIdleStatusForCurrentContext();
        RefreshLaunchState();
    }

    private void SetTransport(TransportPreference preference)
    {
        SelectedTransport = TransportPreference.Auto;
    }

    private void ApplyStreamResolutionPreset(Int32 width, Int32 height)
    {
        StreamWidthText = width.ToString();
        StreamHeightText = height.ToString();
    }

    private void UseDiscoveryEntry()
    {
        if (_useManualConnection)
        {
            _useManualConnection = false;
            _requiresConnectionPassphrase = false;
            OnPropertyChanged(nameof(IsDiscoveryConnectionSelected));
            OnPropertyChanged(nameof(IsManualConnectionSelected));
            NotifyConnectionTargetChanged();
            UpdateIdleStatusForCurrentContext();
        }
    }

    private void UseManualEntry()
    {
        if (!_useManualConnection)
        {
            _useManualConnection = true;
            _requiresConnectionPassphrase = false;
            OnPropertyChanged(nameof(IsDiscoveryConnectionSelected));
            OnPropertyChanged(nameof(IsManualConnectionSelected));
            NotifyConnectionTargetChanged();
            UpdateIdleStatusForCurrentContext();
        }
    }

    private void OpenSecondaryStep(Int32 stepIndex)
    {
        if (CurrentStepIndex is not OptionsStepIndex and not ToolsStepIndex and not AdvancedStepIndex)
        {
            _lastPrimaryStepIndex = CurrentStepIndex;
        }

        SetCurrentStep(stepIndex);
    }

    private void SetCurrentStep(Int32 stepIndex)
    {
        if (SelectedDirection == ConnectionDirection.Send && stepIndex == PeerStepIndex)
        {
            stepIndex = LaunchStepIndex;
        }

        if (stepIndex < 0)
        {
            stepIndex = 0;
        }

        if (stepIndex > AdvancedStepIndex)
        {
            stepIndex = AdvancedStepIndex;
        }

        CurrentStepIndex = stepIndex;
    }

    private void MoveToNextStep()
    {
        if (!CanMoveToNextStep())
        {
            return;
        }

            if (CurrentStepIndex == RoleStepIndex && SelectedDirection == ConnectionDirection.Send)
        {
            SetCurrentStep(LaunchStepIndex);
            return;
        }

        SetCurrentStep(CurrentStepIndex + 1);
    }

    private void MoveToPreviousStep()
    {
        if (CurrentStepIndex > 0)
        {
            if (CurrentStepIndex == AdvancedStepIndex)
            {
                SetCurrentStep(OptionsStepIndex);
                return;
            }

            if (CurrentStepIndex is OptionsStepIndex or ToolsStepIndex)
            {
                SetCurrentStep(_lastPrimaryStepIndex);
                return;
            }

            if (SelectedDirection == ConnectionDirection.Send && CurrentStepIndex == LaunchStepIndex)
            {
                SetCurrentStep(RoleStepIndex);
                return;
            }

            SetCurrentStep(CurrentStepIndex - 1);
        }
    }

    private Boolean CanMoveToNextStep()
    {
        if (_isConnectingToPeer || _isWaitingForInboundPeer || _isDisconnectingSession)
        {
            return false;
        }

        return CurrentStepIndex switch
        {
            RoleStepIndex => true,
            PeerStepIndex => HasConnectionTarget,
            LaunchStepIndex => true,
            _ => false
        };
    }

    private Boolean CanStartSession()
    {
        if (_isConnectingToPeer || _isWaitingForInboundPeer || _isDisconnectingSession || _isRefreshingDiscovery || IsConnected)
        {
            return false;
        }

        if (SelectedDirection == ConnectionDirection.Send)
        {
            return _canShareLocalDesktop;
        }

        return HasConnectionTarget;
    }

    private Boolean CanStopSharing()
    {
        return SelectedDirection == ConnectionDirection.Send &&
               !_isDisconnectingSession &&
               (_isConnectingToPeer || _isWaitingForInboundPeer || IsConnected);
    }

    private Boolean CanCancelLaunch()
    {
        return SelectedDirection == ConnectionDirection.Receive &&
               _isConnectingToPeer &&
               !_isDisconnectingSession &&
               !IsConnected;
    }

    private void CancelLaunch()
    {
        CancelLaunchOperation();
        ApplyCancelledLaunchState();
    }

    private void ApplyCancelledLaunchState(CancellationTokenSource? launchOperationCts = null)
    {
        Boolean shouldApplyState = launchOperationCts is null || ReferenceEquals(_launchOperationCts, launchOperationCts);
        _uiDispatcher.Post(() =>
        {
            if (!shouldApplyState)
            {
                return;
            }

            _isConnectingToPeer = false;
            _isWaitingForInboundPeer = false;
            _isDisconnectingSession = false;
            _isSessionConnected = false;
            _requiresConnectionPassphrase = false;
            StatusTitle = "Connection cancelled";
            StatusDetail = "The connection attempt was cancelled.";
            RefreshLaunchState();
        });
    }

    private async Task ConnectAsync()
    {
        CancellationTokenSource launchOperationCts = BeginLaunchOperation();
        CancellationToken cancellationToken = launchOperationCts.Token;
        String? retrySessionPassphrase = null;

        try
        {
            if (SelectedDirection == ConnectionDirection.Send && !_canShareLocalDesktop)
            {
                _uiDispatcher.Post(() =>
                {
                    StatusTitle = "Sharing unavailable";
                    StatusDetail = _shareSupportDetail;
                    RefreshLaunchState();
                });
                return;
            }

            while (true)
            {
                DiscoveryDevice? targetDevice = SelectedDirection == ConnectionDirection.Receive
                    ? BuildConnectionTargetDevice()
                    : null;
                AppSettings pendingSettings = BuildSettingsFromUi();
                if (!String.IsNullOrWhiteSpace(retrySessionPassphrase))
                {
                    pendingSettings.SessionPassphrase = retrySessionPassphrase;
                }

                Boolean settingsChanged = !AreSettingsEquivalent(_settings, pendingSettings);
                Boolean requiresSessionRestart = SelectedDirection == ConnectionDirection.Send || RequiresSessionRestart(_settings, pendingSettings);

                if (SelectedDirection == ConnectionDirection.Receive && targetDevice is null)
                {
                    _uiDispatcher.Post(() =>
                    {
                        StatusTitle = "Select a target first";
                        StatusDetail = "Choose a discovered machine or enter a direct address before connecting.";
                        RefreshLaunchState();
                    });
                    return;
                }

                if (SelectedDirection == ConnectionDirection.Receive &&
                    targetDevice is not null &&
                    !_useManualConnection &&
                    !targetDevice.AcceptsIncomingSessions)
                {
                    _uiDispatcher.Post(() =>
                    {
                        StatusTitle = "Target not ready";
                        StatusDetail = targetDevice.DisplayName + " is visible on the network, but it is not currently sharing. Start sharing on that machine and wait for it to appear as ready.";
                        RefreshLaunchState();
                    });
                    return;
                }

                if (SelectedDirection == ConnectionDirection.Receive &&
                    targetDevice is not null &&
                    targetDevice.MachineId.Equals(_settings.MachineId, StringComparison.OrdinalIgnoreCase))
                {
                    _uiDispatcher.Post(() =>
                    {
                        StatusTitle = "Choose another machine";
                        StatusDetail = "ShadowLink cannot connect this machine to itself. Pick the other device from the list.";
                        RefreshLaunchState();
                    });
                    return;
                }

                _uiDispatcher.Post(() =>
                {
                    _isConnectingToPeer = true;
                    _isWaitingForInboundPeer = false;
                    _isDisconnectingSession = false;
                    _isSessionConnected = false;
                    _requiresConnectionPassphrase = false;
                    StatusTitle = SelectedDirection == ConnectionDirection.Send ? "Preparing to share" : "Preparing connection";
                    StatusDetail = BuildLaunchPreparationDetail(SelectedDirection, settingsChanged, requiresSessionRestart);
                    RefreshLaunchState();
                });
                await Task.Yield();

                cancellationToken.ThrowIfCancellationRequested();
                _settings = pendingSettings;
                if (requiresSessionRestart)
                {
                    await _sessionCoordinator.RestartAsync(_settings, cancellationToken).ConfigureAwait(false);
                }
                _shareDiscoveryWarning = null;

                cancellationToken.ThrowIfCancellationRequested();
                if (SelectedDirection == ConnectionDirection.Send)
                {
                    _uiDispatcher.Post(() =>
                    {
                        _isConnectingToPeer = false;
                        _isWaitingForInboundPeer = true;
                        _isDisconnectingSession = false;
                        _isSessionConnected = false;
                        _requiresConnectionPassphrase = false;
                        StatusTitle = BuildWaitingForPeerStatusTitle();
                        StatusDetail = BuildWaitingForPeerStatusDetail();
                        RefreshLaunchState();
                        SetCurrentStep(2);
                    });
                    _isShareDiscoveryStarting = _settings.AutoStartDiscovery;
                    CancellationTokenSource shareAncillaryWorkCts = BeginShareAncillaryWork();
                    FireAndForgetShareAncillaryWork(StartShareAncillaryWorkAsync(_settings, shareAncillaryWorkCts));
                    return;
                }

                DiscoveryDevice resolvedTargetDevice = targetDevice!;
                _uiDispatcher.Post(() =>
                {
                    _isConnectingToPeer = true;
                    _isWaitingForInboundPeer = false;
                    _isDisconnectingSession = false;
                    _isSessionConnected = false;
                    _requiresConnectionPassphrase = false;
                    StatusTitle = "Connecting";
                    StatusDetail = "Opening a session to " + resolvedTargetDevice.DisplayName + ".";
                    RefreshLaunchState();
                });

                await _sessionCoordinator.ConnectAsync(resolvedTargetDevice, SelectedDirection, _settings, cancellationToken).ConfigureAwait(false);
                SessionStateSnapshot snapshot = _sessionCoordinator.CurrentState;
                if (snapshot.RequiresPassphrase)
                {
                    _uiDispatcher.Post(() =>
                    {
                        _isConnectingToPeer = false;
                        _isWaitingForInboundPeer = false;
                        _isDisconnectingSession = false;
                        _requiresConnectionPassphrase = false;
                        RefreshLaunchState();
                    });

                    Boolean isRetryingPassphrase = !String.IsNullOrWhiteSpace(retrySessionPassphrase) || !String.IsNullOrWhiteSpace(SessionPassphrase);
                    String promptTitle = isRetryingPassphrase ? "Passphrase incorrect" : "Passphrase required";
                    String promptDetail = isRetryingPassphrase
                        ? "The passphrase for " + resolvedTargetDevice.DisplayName + " was not accepted. Check it and try again."
                        : "Enter the passphrase for " + resolvedTargetDevice.DisplayName + " to continue.";
                    String? passphrase = await _appInteractionService.PromptForPassphraseAsync(promptTitle, promptDetail, cancellationToken).ConfigureAwait(false);
                    if (String.IsNullOrWhiteSpace(passphrase))
                    {
                        _uiDispatcher.Post(() =>
                        {
                            StatusTitle = "Passphrase required";
                            StatusDetail = "Connection was cancelled because no passphrase was entered.";
                            RefreshLaunchState();
                        });
                        return;
                    }

                    retrySessionPassphrase = passphrase;
                    _uiDispatcher.Post(() => SessionPassphrase = passphrase);
                    continue;
                }

                _uiDispatcher.Post(() =>
                {
                    _isConnectingToPeer = false;
                    _isWaitingForInboundPeer = false;
                    _isDisconnectingSession = false;
                    _isSessionConnected = snapshot.IsConnected;
                    _requiresConnectionPassphrase = false;
                    RefreshLaunchState();
                    SetCurrentStep(2);
                });
                return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (SelectedDirection == ConnectionDirection.Receive)
            {
                ApplyCancelledLaunchState(launchOperationCts);
            }
        }
        catch (Exception ex)
        {
            _shareDiscoveryWarning = null;
            Boolean shouldApplyState = ReferenceEquals(_launchOperationCts, launchOperationCts);
            _uiDispatcher.Post(() =>
            {
                if (!shouldApplyState)
                {
                    return;
                }

                _isConnectingToPeer = false;
                _isWaitingForInboundPeer = false;
                _isDisconnectingSession = false;
                _isSessionConnected = false;
                _requiresConnectionPassphrase = false;
                StatusTitle = "Connection failed";
                StatusDetail = ex.Message;
                ActivityEntries.Insert(0, new ActivityEntry(DateTimeOffset.UtcNow, "Connection", ex.Message));
                RefreshLaunchState();
            });
        }
        finally
        {
            CompleteLaunchOperation(launchOperationCts);
        }
    }

    private async Task StopShareAsync()
    {
        Boolean stopSucceeded = false;
        CancelLaunchOperation();
        CancelShareAncillaryWork();
        _isShareDiscoveryStarting = false;
        _shareDiscoveryWarning = null;
        _uiDispatcher.Post(() =>
        {
            _isConnectingToPeer = false;
            _isWaitingForInboundPeer = false;
            _isDisconnectingSession = true;
            _isSessionConnected = false;
            _requiresConnectionPassphrase = false;
            StatusTitle = "Stopping share";
            StatusDetail = "Closing the share session, listener, and nearby discovery.";
            RefreshLaunchState();
        });
        await Task.Yield();

        try
        {
            await _sessionCoordinator.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await _deviceDiscoveryService.StopAsync(CancellationToken.None).ConfigureAwait(false);
            stopSucceeded = true;
        }
        catch (Exception ex)
        {
            _uiDispatcher.Post(() =>
            {
                StatusTitle = "Stop failed";
                StatusDetail = ex.Message;
                ActivityEntries.Insert(0, new ActivityEntry(DateTimeOffset.UtcNow, "Session", ex.Message));
                RefreshLaunchState();
            });
        }
        finally
        {
            _uiDispatcher.Post(() =>
            {
                _isDisconnectingSession = false;
                if (stopSucceeded)
                {
                    ApplySessionSnapshot(_sessionCoordinator.CurrentState);
                    _isWaitingForInboundPeer = false;
                    _isSessionConnected = false;
                    _requiresConnectionPassphrase = false;
                }
                else
                {
                    SessionStateSnapshot snapshot = _sessionCoordinator.CurrentState;
                    _isWaitingForInboundPeer = snapshot.IsListening && !snapshot.IsConnected && SelectedDirection == ConnectionDirection.Send;
                    _isSessionConnected = snapshot.IsConnected;
                    _requiresConnectionPassphrase = snapshot.RequiresPassphrase;
                }
                RefreshLaunchState();
                SetCurrentStep(2);
            });
        }
    }

    private async Task DisconnectAsync()
    {
        Boolean disconnectSucceeded = false;
        _uiDispatcher.Post(() =>
        {
            _isConnectingToPeer = false;
            _isWaitingForInboundPeer = false;
            _isDisconnectingSession = true;
            StatusTitle = "Disconnecting";
            StatusDetail = "Closing the current session.";
            RefreshLaunchState();
        });
        await Task.Yield();

        try
        {
            await _sessionCoordinator.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            disconnectSucceeded = true;
        }
        catch (Exception ex)
        {
            _uiDispatcher.Post(() =>
            {
                StatusTitle = "Disconnect failed";
                StatusDetail = ex.Message;
                ActivityEntries.Insert(0, new ActivityEntry(DateTimeOffset.UtcNow, "Session", ex.Message));
                RefreshLaunchState();
            });
        }
        finally
        {
            _uiDispatcher.Post(() =>
            {
                _isDisconnectingSession = false;
                if (disconnectSucceeded)
                {
                    ApplySessionSnapshot(_sessionCoordinator.CurrentState);
                    _isSessionConnected = false;
                    _requiresConnectionPassphrase = false;
                }
                else
                {
                    SessionStateSnapshot snapshot = _sessionCoordinator.CurrentState;
                    _isSessionConnected = snapshot.IsConnected;
                    _requiresConnectionPassphrase = snapshot.RequiresPassphrase;
                }
                RefreshLaunchState();
            });
        }
    }

    private async Task ConfigureFirewallAsync()
    {
        _uiDispatcher.Post(() =>
        {
            _isConfiguringFirewall = true;
            StatusTitle = "Updating firewall";
            StatusDetail = "Allowing ShadowLink through the local firewall.";
            RefreshLaunchState();
        });
        await Task.Yield();

        try
        {
            AppSettings settings = BuildSettingsFromUi();
            FirewallConfigurationStatus status = await _firewallConfigurationService.EnsureOpenAsync(settings, CancellationToken.None).ConfigureAwait(false);
            ApplyFirewallStatus(status);
        }
        catch (Exception ex)
        {
            _uiDispatcher.Post(() =>
            {
                StatusTitle = "Firewall update failed";
                StatusDetail = ex.Message;
                ActivityEntries.Insert(0, new ActivityEntry(DateTimeOffset.UtcNow, "Firewall", ex.Message));
                RefreshLaunchState();
            });
        }
        finally
        {
            _uiDispatcher.Post(() =>
            {
                _isConfiguringFirewall = false;
                RefreshLaunchState();
            });
        }
    }

    private async Task SendRemoteToolAsync(String statusDetail, params String[] keyNames)
    {
        if (!IsToolsAvailable)
        {
            return;
        }

        await _sessionCoordinator.SendKeyChordAsync(keyNames, CancellationToken.None).ConfigureAwait(false);
        _uiDispatcher.Post(() =>
        {
            StatusTitle = "Remote tool";
            StatusDetail = statusDetail;
            RefreshLaunchState();
        });
    }

    private Task OpenRemoteAppSwitcherAsync()
    {
        return _activePeerPlatformFamily switch
        {
            PlatformFamily.MacOS => SendRemoteToolAsync("Opened the app switcher on the remote device.", "Meta", "Tab"),
            PlatformFamily.Linux => SendRemoteToolAsync("Opened the app switcher on the remote device.", "Alt", "Tab"),
            _ => SendRemoteToolAsync("Opened the app switcher on the remote device.", "Alt", "Tab")
        };
    }

    private Task LockRemoteScreenAsync()
    {
        return _activePeerPlatformFamily switch
        {
            PlatformFamily.Windows => SendRemoteToolAsync("Locked the remote device.", "LWin", "L"),
            PlatformFamily.MacOS => SendRemoteToolAsync("Locked the remote device.", "Ctrl", "Meta", "Q"),
            PlatformFamily.Linux => SendRemoteToolAsync("Locked the remote device.", "Ctrl", "Alt", "L"),
            _ => SendRemoteToolAsync("Locked the remote device.", "Ctrl", "Alt", "L")
        };
    }

    private void HandleDevicesChanged(Object? sender, EventArgs eventArgs)
    {
        _uiDispatcher.Post(SynchronizeDevices);
    }

    private void HandleSessionStateChanged(Object? sender, EventArgs eventArgs)
    {
        _uiDispatcher.Post(() =>
        {
            ApplySessionSnapshot(_sessionCoordinator.CurrentState);
            _connectCommand.RaiseCanExecuteChanged();
            _disconnectCommand.RaiseCanExecuteChanged();
        });
        QueueLocalNetworkStateRefresh();
    }

    private void SynchronizeDevices()
    {
        IReadOnlyList<DiscoveryDevice> devices = _deviceDiscoveryService.CurrentDevices
            .Where(item => item.AcceptsIncomingSessions)
            .Where(item => !item.MachineId.Equals(_settings.MachineId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Dictionary<String, DeviceCardViewModel> existing = DeviceCards.ToDictionary(item => item.MachineId, StringComparer.OrdinalIgnoreCase);
        HashSet<String> incomingIds = new HashSet<String>(devices.Select(item => item.MachineId), StringComparer.OrdinalIgnoreCase);

        foreach (DiscoveryDevice device in devices)
        {
            if (existing.TryGetValue(device.MachineId, out DeviceCardViewModel? current))
            {
                current.Update(device);
            }
            else
            {
                DeviceCards.Add(new DeviceCardViewModel(device));
            }
        }

        for (Int32 index = DeviceCards.Count - 1; index >= 0; index--)
        {
            if (!incomingIds.Contains(DeviceCards[index].MachineId))
            {
                DeviceCards.RemoveAt(index);
            }
        }

        if (_selectedDiscoveryTarget is not null)
        {
            DeviceCardViewModel? selectedCard = DeviceCards.FirstOrDefault(item => item.MachineId.Equals(_selectedDiscoveryTarget.MachineId, StringComparison.OrdinalIgnoreCase));
            if (selectedCard is not null)
            {
                _selectedDiscoveryTarget = selectedCard.Device;
            }

            if (!ReferenceEquals(_selectedDevice, selectedCard))
            {
                _selectedDevice = selectedCard;
                OnPropertyChanged(nameof(SelectedDevice));
            }
        }

        OnPropertyChanged(nameof(KnownDeviceSummary));
        OnPropertyChanged(nameof(HasDiscoveredDevices));
        OnPropertyChanged(nameof(HasNoDiscoveredDevices));
        NotifyConnectionTargetChanged();
    }

    private void ApplySessionSnapshot(SessionStateSnapshot snapshot)
    {
        Boolean preserveBusyStatus = IsIdleSnapshot(snapshot) &&
                                     (_isConnectingToPeer || _isRefreshingDiscovery || _isSavingSettings || _isConfiguringFirewall || _isWaitingForInboundPeer || _isDisconnectingSession);
        if (!preserveBusyStatus)
        {
            StatusTitle = snapshot.StatusTitle;
            StatusDetail = snapshot.StatusDetail;
        }

        ListenerSummary = snapshot.ListenerSummary;
        ActivePeerDisplayName = snapshot.ActivePeerDisplayName;
        ActivePeerAddress = snapshot.ActivePeerAddress;
        TransportSummary = snapshot.ActiveTransportSummary;
        _activePeerPlatformFamily = snapshot.ActivePeerPlatformFamily;
        _activePeerOperatingSystem = snapshot.ActivePeerOperatingSystem;
        _canShareLocalDesktop = snapshot.CanShareLocalDesktop;
        _shareSupportDetail = snapshot.ShareSupportDetail;
        _isSessionConnected = snapshot.IsConnected;
        _requiresConnectionPassphrase = snapshot.RequiresPassphrase;

        ActivityEntries.Clear();
        foreach (ActivityEntry entry in snapshot.ActivityEntries)
        {
            ActivityEntries.Add(entry);
        }

        if (snapshot.IsConnected)
        {
            _isConnectingToPeer = false;
            _isWaitingForInboundPeer = false;
            _isDisconnectingSession = false;
        }
        else if (snapshot.RequiresPassphrase)
        {
            _isConnectingToPeer = false;
            _isWaitingForInboundPeer = false;
            _isDisconnectingSession = false;
            SetCurrentStep(2);
        }
        else if (IsIdleSnapshot(snapshot))
        {
            if (!_isConnectingToPeer && !_isRefreshingDiscovery && !_isSavingSettings && !_isConfiguringFirewall && !_isWaitingForInboundPeer && !_isDisconnectingSession)
            {
                _isConnectingToPeer = false;
                _isWaitingForInboundPeer = false;
                _isDisconnectingSession = false;
            }
        }
        else if (!_isWaitingForInboundPeer && !_isSavingSettings && !_isConfiguringFirewall && !_isDisconnectingSession)
        {
            _isConnectingToPeer = false;
            _isDisconnectingSession = false;
            SetCurrentStep(2);
        }

        if (!_isConnectingToPeer && !_isWaitingForInboundPeer && !_isDisconnectingSession && !_isSessionConnected && !_requiresConnectionPassphrase)
        {
            UpdateIdleStatusForCurrentContext();
            return;
        }

        RefreshLaunchState();
    }

    private void ApplySettingsToUi(AppSettings settings)
    {
        DisplayName = settings.DisplayName;
        DiscoveryPortText = settings.DiscoveryPort.ToString();
        ControlPortText = settings.ControlPort.ToString();
        ManualPeerPortText = settings.ControlPort.ToString();
        SessionPassphrase = settings.SessionPassphrase;
        StreamWidthText = settings.StreamWidth.ToString();
        StreamHeightText = settings.StreamHeight.ToString();
        StreamFrameRateText = settings.StreamFrameRate.ToString();
        StreamTileSizeText = settings.StreamTileSize.ToString();
        StreamDictionarySizeMbText = settings.StreamDictionarySizeMb.ToString();
        StreamStaticCodebookSharePercentText = settings.StreamStaticCodebookSharePercent.ToString();
        SelectedDirection = settings.DefaultDirection;
        SelectedTransport = TransportPreference.Auto;
        SelectedStreamColorMode = settings.StreamColorMode;
        SelectedDisplayScaleMode = settings.DisplayScaleMode;
        EnableKeyboardRelay = settings.EnableKeyboardRelay;
        EnableMouseRelay = settings.EnableMouseRelay;
        AutoStartDiscovery = settings.AutoStartDiscovery;
        RememberRecentPeers = settings.RememberRecentPeers;

        ShortcutBindings.Clear();
        foreach (ShortcutBinding shortcutBinding in settings.ShortcutBindings)
        {
            ShortcutBindingViewModel shortcutBindingViewModel = new ShortcutBindingViewModel(shortcutBinding);
            shortcutBindingViewModel.PropertyChanged += HandleShortcutBindingPropertyChanged;
            ShortcutBindings.Add(shortcutBindingViewModel);
        }

        OnPropertyChanged(nameof(EscapeSummary));
        OnPropertyChanged(nameof(TransportModeSummary));
        OnPropertyChanged(nameof(DirectionSummary));
        OnPropertyChanged(nameof(RoleSummary));
        OnPropertyChanged(nameof(ReviewRouteSummary));
        OnPropertyChanged(nameof(LocalShareName));
        OnPropertyChanged(nameof(LocalAddressSummary));
        OnPropertyChanged(nameof(LocalSharePort));
        OnPropertyChanged(nameof(LocalShareSummary));
        OnPropertyChanged(nameof(HasLocalThunderboltDirectLink));
        OnPropertyChanged(nameof(IsDirectCableStatusVisible));
        OnPropertyChanged(nameof(DirectCableStatusTitle));
        OnPropertyChanged(nameof(DirectCableStatusDetail));
        OnPropertyChanged(nameof(ShareSecuritySummary));
        OnPropertyChanged(nameof(ShareAccessHint));
        OnPropertyChanged(nameof(ManualConnectionHint));
        OnPropertyChanged(nameof(ReviewTargetSummary));
        OnPropertyChanged(nameof(DiscoveryProgressDetail));
        OnPropertyChanged(nameof(NoDiscoveredDevicesHint));
        OnPropertyChanged(nameof(LaunchStageBody));
        OnPropertyChanged(nameof(ConnectionPassphraseHint));
        OnPropertyChanged(nameof(AccessSummary));
        OnPropertyChanged(nameof(StreamResolutionSummary));
        OnPropertyChanged(nameof(ReviewResolutionSummary));
        OnPropertyChanged(nameof(StreamSettingsSummary));
        OnPropertyChanged(nameof(CodecSummary));
        OnPropertyChanged(nameof(CodebookSplitSummary));
        OnPropertyChanged(nameof(StreamStaticCodebookSharePercent));
        OnPropertyChanged(nameof(IsBgra32ColorModeSelected));
        OnPropertyChanged(nameof(IsBgr24ColorModeSelected));
        OnPropertyChanged(nameof(IsRgb565ColorModeSelected));
        OnPropertyChanged(nameof(IsRgb332ColorModeSelected));
        OnPropertyChanged(nameof(IsIndexed4ColorModeSelected));
        OnPropertyChanged(nameof(IsDisplayScaleFitSelected));
        OnPropertyChanged(nameof(IsDisplayScaleZoomToFitSelected));
        OnPropertyChanged(nameof(IsDisplayScaleFillSelected));
        OnPropertyChanged(nameof(IsTile8PresetSelected));
        OnPropertyChanged(nameof(IsTile16PresetSelected));
        OnPropertyChanged(nameof(IsTile32PresetSelected));
        OnPropertyChanged(nameof(IsDictionary512PresetSelected));
        OnPropertyChanged(nameof(IsDictionary1024PresetSelected));
        OnPropertyChanged(nameof(IsDictionary2048PresetSelected));
        OnPropertyChanged(nameof(IsDictionary4096PresetSelected));
        OnPropertyChanged(nameof(IsDictionary8192PresetSelected));
        OnPropertyChanged(nameof(IsDictionary16384PresetSelected));
        OnPropertyChanged(nameof(IsNativePresetSelected));
        OnPropertyChanged(nameof(HasNoDiscoveredDevices));
        NotifyConnectionTargetChanged();
    }

    private async Task RefreshFirewallStatusAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        FirewallConfigurationStatus status = await _firewallConfigurationService.EvaluateAsync(settings, cancellationToken).ConfigureAwait(false);
        ApplyFirewallStatus(status);
    }

    private void ApplyFirewallStatus(FirewallConfigurationStatus status)
    {
        _uiDispatcher.Post(() =>
        {
            _firewallStatus = status;
            OnPropertyChanged(nameof(IsFirewallStatusVisible));
            OnPropertyChanged(nameof(FirewallStatusTitle));
            OnPropertyChanged(nameof(FirewallStatusDetail));
            OnPropertyChanged(nameof(IsFirewallActionVisible));
            OnPropertyChanged(nameof(FirewallActionLabel));
            _configureFirewallCommand.RaiseCanExecuteChanged();
        });
    }

    private void UpdateIdleStatusForCurrentContext()
    {
        if (_isConnectingToPeer || _isWaitingForInboundPeer || _isDisconnectingSession || _isSessionConnected || _isSavingSettings || _isConfiguringFirewall || _isRefreshingDiscovery)
        {
            return;
        }

        if (SelectedDirection == ConnectionDirection.Send)
        {
            StatusTitle = _canShareLocalDesktop ? "Ready to share" : "Sharing unavailable";
            StatusDetail = _canShareLocalDesktop
                ? "Press Start sharing when you are ready."
                : _shareSupportDetail;
            RefreshLaunchState();
            return;
        }

        StatusTitle = HasConnectionTarget ? "Ready to connect" : "Choose a target";
        StatusDetail = HasConnectionTarget
            ? "Press Connect to open a session to " + BuildConnectionTargetSummary() + "."
            : _useManualConnection
                ? "Enter an IP address or hostname, then connect."
                : "Choose a discovered machine or enter a direct address.";
            RefreshLaunchState();
    }

    private void QueueDiscoveryModeRefresh(ConnectionDirection direction)
    {
        AppSettings settings = BuildSettingsFromUi();
        _ = Task.Run(async () =>
        {
            try
            {
                if (settings.AutoStartDiscovery && direction == ConnectionDirection.Receive)
                {
                    await _deviceDiscoveryService.RestartAsync(settings, CancellationToken.None).ConfigureAwait(false);
                    await _deviceDiscoveryService.RefreshAsync(CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    await _deviceDiscoveryService.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Discovery mode refresh failed: {0}", ex.Message);
            }
        });
    }

    private static String BuildLaunchPreparationDetail(ConnectionDirection direction, Boolean settingsChanged, Boolean requiresSessionRestart)
    {
        if (direction == ConnectionDirection.Send)
        {
            return settingsChanged
                ? T("launch.preparing_share_with_settings")
                : T("launch.preparing_share");
        }

        if (requiresSessionRestart)
        {
            return settingsChanged
                ? T("launch.refresh_listener_with_settings")
                : T("launch.refresh_listener");
        }

        return settingsChanged
            ? T("launch.apply_connection_settings")
            : T("launch.check_connection_state");
    }

    private AppSettings BuildSettingsFromUi()
    {
        AppSettings settings = CloneSettings(_settings);
        settings.DisplayName = String.IsNullOrWhiteSpace(DisplayName) ? Environment.MachineName : DisplayName.Trim();
        settings.DiscoveryPort = ParsePort(DiscoveryPortText, DefaultDiscoveryPort);
        settings.ControlPort = ParsePort(ControlPortText, DefaultControlPort);
        settings.SessionPassphrase = SessionPassphrase.Trim();
        settings.StreamWidth = ParseDimension(StreamWidthText, DefaultStreamWidth);
        settings.StreamHeight = ParseDimension(StreamHeightText, DefaultStreamHeight);
        settings.StreamFrameRate = ParseFrameRate(StreamFrameRateText, DefaultFrameRate);
        settings.StreamColorMode = SelectedStreamColorMode;
        settings.StreamTileSize = ParseTileSize(StreamTileSizeText, DefaultTileSize);
        settings.StreamDictionarySizeMb = ParseDictionarySizeMb(StreamDictionarySizeMbText, DefaultDictionarySizeMb);
        settings.StreamStaticCodebookSharePercent = ParseStaticCodebookSharePercent(StreamStaticCodebookSharePercentText, DefaultPatternsPercent);
        settings.DisplayScaleMode = SelectedDisplayScaleMode;
        settings.DefaultDirection = SelectedDirection;
        settings.PreferredTransport = TransportPreference.Auto;
        settings.EnableKeyboardRelay = EnableKeyboardRelay;
        settings.EnableMouseRelay = EnableMouseRelay;
        settings.AutoStartDiscovery = AutoStartDiscovery;
        settings.RememberRecentPeers = RememberRecentPeers;
        settings.ShortcutBindings = ShortcutBindings.Select(item => item.ToModel()).ToList();
        return settings;
    }

    private static Boolean RequiresSessionRestart(AppSettings current, AppSettings pending)
    {
        return current.ControlPort != pending.ControlPort;
    }

    private static Boolean RequiresCoordinatorRestart(AppSettings current, AppSettings pending)
    {
        return current.ControlPort != pending.ControlPort;
    }

    private static Boolean RequiresDiscoveryRefresh(AppSettings current, AppSettings pending)
    {
        return !current.DisplayName.Equals(pending.DisplayName, StringComparison.Ordinal) ||
               current.DiscoveryPort != pending.DiscoveryPort ||
               current.ControlPort != pending.ControlPort ||
               current.AutoStartDiscovery != pending.AutoStartDiscovery;
    }

    private static Boolean RequiresFirewallRefresh(AppSettings current, AppSettings pending)
    {
        return current.ControlPort != pending.ControlPort;
    }

    private static Boolean AreSettingsEquivalent(AppSettings left, AppSettings right)
    {
        return left.MachineId.Equals(right.MachineId, StringComparison.Ordinal) &&
               left.DisplayName.Equals(right.DisplayName, StringComparison.Ordinal) &&
               left.DefaultDirection == right.DefaultDirection &&
               left.PreferredTransport == right.PreferredTransport &&
               left.DiscoveryPort == right.DiscoveryPort &&
               left.ControlPort == right.ControlPort &&
               left.AutoRefreshIntervalSeconds == right.AutoRefreshIntervalSeconds &&
               left.StreamWidth == right.StreamWidth &&
               left.StreamHeight == right.StreamHeight &&
               left.StreamFrameRate == right.StreamFrameRate &&
               left.StreamColorMode == right.StreamColorMode &&
               left.StreamTileSize == right.StreamTileSize &&
               left.StreamDictionarySizeMb == right.StreamDictionarySizeMb &&
               left.StreamStaticCodebookSharePercent == right.StreamStaticCodebookSharePercent &&
               left.DisplayScaleMode == right.DisplayScaleMode &&
               left.EnableKeyboardRelay == right.EnableKeyboardRelay &&
               left.EnableMouseRelay == right.EnableMouseRelay &&
               left.AutoStartDiscovery == right.AutoStartDiscovery &&
               left.RememberRecentPeers == right.RememberRecentPeers &&
               left.SessionPassphrase.Equals(right.SessionPassphrase, StringComparison.Ordinal) &&
               left.ShortcutBindings.Count == right.ShortcutBindings.Count &&
               left.ShortcutBindings.Zip(right.ShortcutBindings, AreShortcutBindingsEquivalent).All(item => item);
    }

    private static Boolean AreShortcutBindingsEquivalent(ShortcutBinding left, ShortcutBinding right)
    {
        return left.Name.Equals(right.Name, StringComparison.Ordinal) &&
               left.Gesture.Equals(right.Gesture, StringComparison.Ordinal) &&
               left.Description.Equals(right.Description, StringComparison.Ordinal) &&
               left.IsEnabled == right.IsEnabled;
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
            ShortcutBindings = settings.ShortcutBindings
                .Select(item => new ShortcutBinding
                {
                    Name = item.Name,
                    Gesture = item.Gesture,
                    Description = item.Description,
                    IsEnabled = item.IsEnabled
                })
                .ToList()
        };
    }

    private DiscoveryDevice? BuildConnectionTargetDevice()
    {
        if (_useManualConnection)
        {
            String address = ManualPeerAddress.Trim();
            if (String.IsNullOrWhiteSpace(address))
            {
                return null;
            }

            Int32 manualPort = ParsePort(ManualPeerPortText, ParsePort(ControlPortText, DefaultControlPort));
            String displayName = String.IsNullOrWhiteSpace(ManualPeerName) ? address : ManualPeerName.Trim();
            return new DiscoveryDevice
            {
                MachineId = "manual-" + address + "-" + manualPort,
                DisplayName = displayName,
                HostName = address,
                OperatingSystem = "Direct connection",
                NetworkAddress = address,
                DiscoveryPort = ParsePort(DiscoveryPortText, DefaultDiscoveryPort),
                ControlPort = manualPort,
                SupportsKeyboardRelay = true,
                SupportsMouseRelay = true,
                SupportsUsbNetworking = HasLocalThunderboltDirectLink,
                AcceptsIncomingSessions = true,
                PreferredTransport = TransportPreference.Auto,
                NetworkEndpoints = new List<DiscoveryNetworkEndpoint>(),
                LastSeenUtc = DateTimeOffset.UtcNow
            };
        }

        return SelectedDevice?.Device ?? _selectedDiscoveryTarget;
    }

    private void NotifyConnectionTargetChanged()
    {
        _requiresConnectionPassphrase = false;
        UpdateIdleStatusForCurrentContext();
        OnPropertyChanged(nameof(SelectedDeviceSummary));
        OnPropertyChanged(nameof(ManualConnectionHint));
        OnPropertyChanged(nameof(ReviewHeadline));
        OnPropertyChanged(nameof(ReviewTargetSummary));
        OnPropertyChanged(nameof(ConnectionModeSummary));
        OnPropertyChanged(nameof(ReviewTargetSummary));
        OnPropertyChanged(nameof(IsPassphrasePromptVisible));
        OnPropertyChanged(nameof(AccessSummary));
        OnPropertyChanged(nameof(IsDiscoveryProgressVisible));
        _goToLaunchStepCommand.RaiseCanExecuteChanged();
        _goToPeerStepCommand.RaiseCanExecuteChanged();
        _nextStepCommand.RaiseCanExecuteChanged();
        _connectCommand.RaiseCanExecuteChanged();
    }

    private String BuildConnectionTargetSummary()
    {
        if (_useManualConnection)
        {
            String name = String.IsNullOrWhiteSpace(ManualPeerName) ? ManualPeerAddress.Trim() : ManualPeerName.Trim();
            return ShadowLinkText.TranslateFormat(
                "peer.summary.direct",
                name,
                ManualPeerAddress.Trim(),
                ParsePort(ManualPeerPortText, DefaultControlPort));
        }

        DiscoveryDevice? selectedTarget = SelectedDevice?.Device ?? _selectedDiscoveryTarget;
        return selectedTarget is null
            ? T("peer.summary.none")
            : ShadowLinkText.TranslateFormat("peer.summary.discovery", selectedTarget.DisplayName, selectedTarget.NetworkAddress);
    }

    private static Int32 ParsePort(String value, Int32 fallback)
    {
        return Int32.TryParse(value, out Int32 parsed) && parsed > 0 && parsed <= 65535 ? parsed : fallback;
    }

    private static Int32 ParseDimension(String value, Int32 fallback)
    {
        return Int32.TryParse(value, out Int32 parsed) && parsed >= 320 && parsed <= 7680 ? parsed : fallback;
    }

    private static Int32 ParseFrameRate(String value, Int32 fallback)
    {
        return Int32.TryParse(value, out Int32 parsed) && parsed >= 1 && parsed <= 240 ? parsed : fallback;
    }

    private static Int32 ParseTileSize(String value, Int32 fallback)
    {
        return Int32.TryParse(value, out Int32 parsed) && parsed >= 4 && parsed <= 256 ? parsed : fallback;
    }

    private static Int32 ParseDictionarySizeMb(String value, Int32 fallback)
    {
        return Int32.TryParse(value, out Int32 parsed) && parsed >= 64 && parsed <= 16384 ? parsed : fallback;
    }

    private static Int32 ParseStaticCodebookSharePercent(String value, Int32 fallback)
    {
        return Int32.TryParse(value, out Int32 parsed) && parsed >= 5 && parsed <= 95 ? parsed : fallback;
    }

    private void ApplyNativeResolutionPreset()
    {
        (Int32 nativeWidth, Int32 nativeHeight) = GetNativeStreamResolution();
        ApplyStreamResolutionPreset(nativeWidth, nativeHeight);
    }

    private void SetStreamColorMode(StreamColorMode colorMode)
    {
        SelectedStreamColorMode = colorMode;
    }

    private void SetDisplayScaleMode(RemoteDisplayScaleMode displayScaleMode)
    {
        SelectedDisplayScaleMode = displayScaleMode;
    }

    private void ApplyTileSizePreset(Int32 tileSize)
    {
        StreamTileSizeText = tileSize.ToString();
    }

    private void ApplyDictionarySizePreset(Int32 dictionarySizeMb)
    {
        StreamDictionarySizeMbText = dictionarySizeMb.ToString();
    }

    private static (Int32 Width, Int32 Height) GetNativeStreamResolution()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return (1920, 1080);
        }

        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime &&
            desktopLifetime.MainWindow?.Screens?.Primary is { } primaryScreen)
        {
            return (Math.Max(320, primaryScreen.Bounds.Width), Math.Max(320, primaryScreen.Bounds.Height));
        }

        return (1920, 1080);
    }

    private static String BuildColorModeLabel(StreamColorMode colorMode)
    {
        return colorMode switch
        {
            StreamColorMode.Bgra32 => T("stream.color.32"),
            StreamColorMode.Bgr24 => T("stream.color.24"),
            StreamColorMode.Rgb565 => T("stream.color.16"),
            StreamColorMode.Rgb332 => T("stream.color.8"),
            StreamColorMode.Indexed4 => T("stream.color.4"),
            _ => T("stream.color.16")
        };
    }

    private static String BuildDisplayScaleLabel(RemoteDisplayScaleMode displayScaleMode)
    {
        return displayScaleMode switch
        {
            RemoteDisplayScaleMode.Fill => T("stream.scale.fill"),
            RemoteDisplayScaleMode.ZoomToFit => T("stream.scale.zoom_to_fit"),
            _ => T("stream.scale.actual_size")
        };
    }

    private static String BuildDictionarySizeLabel(Int32 dictionarySizeMb)
    {
        return dictionarySizeMb >= 1024 && dictionarySizeMb % 1024 == 0
            ? ShadowLinkText.TranslateFormat("stream.dictionary.gb", dictionarySizeMb / 1024)
            : ShadowLinkText.TranslateFormat("stream.dictionary.mb", dictionarySizeMb);
    }

    private String BuildLocalAddressSummary()
    {
        if (!_hasLoadedLocalNetworkState)
        {
            return T("network.checking_addresses");
        }

        String[] addresses = _localNetworkEndpoints
            .Select(item => item.Address + " (" + item.InterfaceName + ")")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (addresses.Length > 0)
        {
            return String.Join(", ", addresses);
        }

        return T("network.no_reachable_address");
    }

    private String BuildWaitingForPeerStatusDetail()
    {
        if (!_hasLoadedLocalNetworkState)
        {
            return T("status.share_listener_checking");
        }

        if (_isShareDiscoveryStarting)
        {
            return HasReachableLocalShareEndpoint()
                ? ShadowLinkText.TranslateFormat("status.share_preparing_discovery_ready", LocalAddressSummary, LocalSharePort)
                : T("status.share_preparing_discovery_no_network");
        }

        String detail;
        if (HasReachableLocalShareEndpoint())
        {
            detail = ShadowLinkText.TranslateFormat("status.share_ready", LocalAddressSummary, LocalSharePort);
        }
        else if (_localDirectCableStatus.HasCompatibleInterface && !_localDirectCableStatus.HasLink)
        {
            detail = ShadowLinkText.TranslateFormat("status.share_waiting_link", LocalSharePort);
        }
        else if (_localDirectCableStatus.HasLink && !_localDirectCableStatus.HasUsableNetworkPath)
        {
            detail = ShadowLinkText.TranslateFormat("status.share_waiting_direct_address", LocalSharePort);
        }
        else
        {
            detail = ShadowLinkText.TranslateFormat("status.share_waiting_network", LocalSharePort);
        }

        return String.IsNullOrWhiteSpace(_shareDiscoveryWarning)
            ? detail
            : detail + _shareDiscoveryWarning;
    }

    private String BuildWaitingForPeerStatusTitle()
    {
        if (!_hasLoadedLocalNetworkState)
        {
            return T("status.preparing_to_share");
        }

        return HasReachableLocalShareEndpoint()
            ? T("status.waiting_for_machine")
            : T("status.waiting_for_network");
    }

    private Boolean HasReachableLocalShareEndpoint()
    {
        return _localNetworkEndpoints.Count > 0;
    }

    private DiscoveryNetworkEndpoint[] GetLocalDirectCableEndpoints()
    {
        return _localDirectCableStatus.Endpoints
            .OrderByDescending(item => item.LinkSpeedMbps)
            .ThenBy(item => item.InterfaceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void QueueLocalNetworkStateRefresh()
    {
        Int32 version = Interlocked.Increment(ref _localNetworkStateVersion);
        _ = Task.Run(async () =>
        {
            try
            {
                await RefreshLocalNetworkStateAsync(version, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Trace.TraceError("Local network state refresh failed: {0}", ex);
                _uiDispatcher.Post(() =>
                {
                    if (version != Volatile.Read(ref _localNetworkStateVersion))
                    {
                        return;
                    }

                    _hasLoadedLocalNetworkState = true;
                    if (_isWaitingForInboundPeer && !_isSessionConnected)
                    {
                        StatusTitle = BuildWaitingForPeerStatusTitle();
                        StatusDetail = BuildWaitingForPeerStatusDetail();
                    }

                    OnPropertyChanged(nameof(LocalAddressSummary));
                    OnPropertyChanged(nameof(LocalShareSummary));
                    OnPropertyChanged(nameof(ReviewTargetSummary));
                    OnPropertyChanged(nameof(SessionProgressDetail));
                });
            }
        });
    }

    private async Task RefreshLocalNetworkStateAsync(Int32 version, CancellationToken cancellationToken)
    {
        IReadOnlyList<DiscoveryNetworkEndpoint> endpoints = await Task.Run(PlatformEnvironment.GetLocalNetworkEndpoints, cancellationToken).ConfigureAwait(false);
        DirectCableStatus directCableStatus = await Task.Run(PlatformEnvironment.GetThunderboltDirectStatus, cancellationToken).ConfigureAwait(false);

        if (version != Volatile.Read(ref _localNetworkStateVersion))
        {
            return;
        }

        _uiDispatcher.Post(() =>
        {
            if (version != Volatile.Read(ref _localNetworkStateVersion))
            {
                return;
            }

            _localNetworkEndpoints = endpoints;
            _localDirectCableStatus = directCableStatus;
            _hasLoadedLocalNetworkState = true;
            if (_isWaitingForInboundPeer && !_isSessionConnected)
            {
                StatusTitle = BuildWaitingForPeerStatusTitle();
                StatusDetail = BuildWaitingForPeerStatusDetail();
            }

            OnPropertyChanged(nameof(LocalAddressSummary));
            OnPropertyChanged(nameof(LocalShareSummary));
            OnPropertyChanged(nameof(ReviewTargetSummary));
            OnPropertyChanged(nameof(HasLocalThunderboltDirectLink));
            OnPropertyChanged(nameof(IsDirectCableStatusVisible));
            OnPropertyChanged(nameof(DirectCableStatusTitle));
            OnPropertyChanged(nameof(DirectCableStatusDetail));
            OnPropertyChanged(nameof(ShareAccessHint));
            OnPropertyChanged(nameof(ManualConnectionHint));
            OnPropertyChanged(nameof(TransportModeSummary));
            OnPropertyChanged(nameof(NoDiscoveredDevicesHint));
            OnPropertyChanged(nameof(SessionProgressDetail));
            _connectCommand.RaiseCanExecuteChanged();
        });
    }

    private String BuildEscapeSummary()
    {
        ShortcutBindingViewModel? releaseShortcut = ShortcutBindings.FirstOrDefault(item => item.NameKey.Equals("shortcut.release.name", StringComparison.OrdinalIgnoreCase));
        return releaseShortcut is null
            ? T("status.escape_summary_empty")
            : releaseShortcut.Gesture;
    }

    private CancellationTokenSource BeginShareAncillaryWork()
    {
        CancellationTokenSource shareAncillaryWorkCts = new CancellationTokenSource();
        CancellationTokenSource? previousShareAncillaryWorkCts = Interlocked.Exchange(ref _shareAncillaryWorkCts, shareAncillaryWorkCts);
        previousShareAncillaryWorkCts?.Cancel();
        previousShareAncillaryWorkCts?.Dispose();
        return shareAncillaryWorkCts;
    }

    private void CancelShareAncillaryWork()
    {
        CancellationTokenSource? shareAncillaryWorkCts = Interlocked.Exchange(ref _shareAncillaryWorkCts, null);
        shareAncillaryWorkCts?.Cancel();
        shareAncillaryWorkCts?.Dispose();
    }

    private void CompleteShareAncillaryWork(CancellationTokenSource shareAncillaryWorkCts)
    {
        Boolean isCurrentShareAncillaryWork = ReferenceEquals(
            Interlocked.CompareExchange(ref _shareAncillaryWorkCts, null, shareAncillaryWorkCts),
            shareAncillaryWorkCts);
        _uiDispatcher.Post(() =>
        {
            if (!isCurrentShareAncillaryWork)
            {
                return;
            }

            _isShareDiscoveryStarting = false;
            if (_isWaitingForInboundPeer && !_isSessionConnected)
            {
                StatusDetail = BuildWaitingForPeerStatusDetail();
                RefreshLaunchState();
            }
        });
        shareAncillaryWorkCts.Dispose();
    }

    private async Task StartShareAncillaryWorkAsync(AppSettings settings, CancellationTokenSource shareAncillaryWorkCts)
    {
        CancellationToken cancellationToken = shareAncillaryWorkCts.Token;

        try
        {
            await RefreshFirewallStatusWithTimeoutAsync(settings, TimeSpan.FromSeconds(4), cancellationToken).ConfigureAwait(false);

            if (settings.AutoStartDiscovery)
            {
                await RestartShareDiscoveryWithTimeoutAsync(settings, shareAncillaryWorkCts, TimeSpan.FromSeconds(6), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await StopShareDiscoveryWithTimeoutAsync(shareAncillaryWorkCts, TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            CompleteShareAncillaryWork(shareAncillaryWorkCts);
        }
    }

    private async Task RefreshFirewallStatusWithTimeoutAsync(AppSettings settings, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            await RefreshFirewallStatusAsync(settings, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Trace.TraceWarning("Firewall status refresh timed out after {0} seconds.", timeout.TotalSeconds);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Firewall status refresh failed during share startup: {0}", ex.Message);
        }
    }

    private async Task RestartShareDiscoveryWithTimeoutAsync(AppSettings settings, CancellationTokenSource shareAncillaryWorkCts, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            await _deviceDiscoveryService.RestartAsync(settings, timeoutCts.Token).ConfigureAwait(false);
            await _deviceDiscoveryService.RefreshAsync(timeoutCts.Token).ConfigureAwait(false);
            if (!ReferenceEquals(_shareAncillaryWorkCts, shareAncillaryWorkCts))
            {
                return;
            }

            _uiDispatcher.Post(() =>
            {
                if (!ReferenceEquals(_shareAncillaryWorkCts, shareAncillaryWorkCts))
                {
                    return;
                }

                _shareDiscoveryWarning = null;
                if (_isWaitingForInboundPeer && !_isSessionConnected)
                {
                    StatusDetail = BuildWaitingForPeerStatusDetail();
                    RefreshLaunchState();
                }
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ApplyShareDiscoveryWarning(T("status.discovery_slow"), shareAncillaryWorkCts);
        }
        catch (Exception ex)
        {
            ApplyShareDiscoveryWarning(ShadowLinkText.TranslateFormat("status.discovery_failed", ex.Message), shareAncillaryWorkCts);
        }
    }

    private async Task StopShareDiscoveryWithTimeoutAsync(CancellationTokenSource shareAncillaryWorkCts, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            await _deviceDiscoveryService.StopAsync(timeoutCts.Token).ConfigureAwait(false);

            if (!ReferenceEquals(_shareAncillaryWorkCts, shareAncillaryWorkCts))
            {
                return;
            }

            _uiDispatcher.Post(() =>
            {
                if (!ReferenceEquals(_shareAncillaryWorkCts, shareAncillaryWorkCts))
                {
                    return;
                }

                _shareDiscoveryWarning = null;
                if (_isWaitingForInboundPeer && !_isSessionConnected)
                {
                    StatusDetail = BuildWaitingForPeerStatusDetail();
                    RefreshLaunchState();
                }
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Trace.TraceWarning("Stopping discovery during share startup timed out after {0} seconds.", timeout.TotalSeconds);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Stopping discovery during share startup failed: {0}", ex.Message);
        }
    }

    private void ApplyShareDiscoveryWarning(String message, CancellationTokenSource shareAncillaryWorkCts)
    {
        _uiDispatcher.Post(() =>
        {
            if (!ReferenceEquals(_shareAncillaryWorkCts, shareAncillaryWorkCts))
            {
                return;
            }

            _shareDiscoveryWarning = ShadowLinkText.TranslateFormat("status.share_warning_prefix", message);
            ActivityEntries.Insert(0, new ActivityEntry(DateTimeOffset.UtcNow, "activity.discovery", message));
            if (_isWaitingForInboundPeer && !_isSessionConnected)
            {
                StatusDetail = BuildWaitingForPeerStatusDetail();
                RefreshLaunchState();
            }
        });
    }

    private static async void FireAndForgetShareAncillaryWork(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Trace.TraceError("Share ancillary startup task failed: {0}", ex);
        }
    }

    private void HandleShortcutBindingPropertyChanged(Object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(EscapeSummary));
    }

    private static Boolean IsIdleSnapshot(SessionStateSnapshot snapshot)
    {
        return !snapshot.IsConnected &&
               !snapshot.RequiresPassphrase &&
               snapshot.ActivePeerDisplayName.Equals(T("status.no_active_peer"), StringComparison.OrdinalIgnoreCase) &&
               snapshot.StatusTitle.StartsWith(T("status.ready"), StringComparison.OrdinalIgnoreCase);
    }

    private Boolean ShouldShowFooterStatus()
    {
        return IsFooterBusy ||
               IsConnected ||
               (CurrentStepIndex is OptionsStepIndex or AdvancedStepIndex) && StatusTitle.EndsWith(T("common.failed"), StringComparison.OrdinalIgnoreCase);
    }

    private CancellationTokenSource BeginLaunchOperation()
    {
        CancellationTokenSource launchOperationCts = new CancellationTokenSource();
        CancellationTokenSource? previousOperationCts = Interlocked.Exchange(ref _launchOperationCts, launchOperationCts);
        previousOperationCts?.Cancel();
        previousOperationCts?.Dispose();
        return launchOperationCts;
    }

    private void CancelLaunchOperation()
    {
        CancellationTokenSource? launchOperationCts = Interlocked.Exchange(ref _launchOperationCts, null);
        launchOperationCts?.Cancel();
    }

    private void CompleteLaunchOperation(CancellationTokenSource launchOperationCts)
    {
        Interlocked.CompareExchange(ref _launchOperationCts, null, launchOperationCts);
        launchOperationCts.Dispose();
    }

    private void RefreshLaunchState()
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(PrimarySessionActionLabel));
        OnPropertyChanged(nameof(IsPrimarySessionActionVisible));
        OnPropertyChanged(nameof(IsStopShareVisible));
        OnPropertyChanged(nameof(IsCancelLaunchVisible));
        OnPropertyChanged(nameof(IsDisconnectVisible));
        OnPropertyChanged(nameof(IsLaunchBackVisible));
        OnPropertyChanged(nameof(IsLaunchAdvancedVisible));
        OnPropertyChanged(nameof(IsLaunchToolsVisible));
        OnPropertyChanged(nameof(IsDiscoveryProgressVisible));
        OnPropertyChanged(nameof(DiscoveryProgressTitle));
        OnPropertyChanged(nameof(DiscoveryProgressDetail));
        OnPropertyChanged(nameof(IsSessionProgressVisible));
        OnPropertyChanged(nameof(SessionProgressTitle));
        OnPropertyChanged(nameof(SessionProgressDetail));
        OnPropertyChanged(nameof(IsFooterBusy));
        OnPropertyChanged(nameof(FooterStatusTitle));
        OnPropertyChanged(nameof(FooterStatusDetail));
        OnPropertyChanged(nameof(IsFirewallStatusVisible));
        OnPropertyChanged(nameof(FirewallStatusTitle));
        OnPropertyChanged(nameof(FirewallStatusDetail));
        OnPropertyChanged(nameof(IsFirewallActionVisible));
        OnPropertyChanged(nameof(FirewallActionLabel));
        OnPropertyChanged(nameof(IsPassphrasePromptVisible));
        OnPropertyChanged(nameof(AccessSummary));
        OnPropertyChanged(nameof(ReviewHeadline));
        OnPropertyChanged(nameof(ReviewTargetSummary));
        OnPropertyChanged(nameof(LaunchStageTitle));
        OnPropertyChanged(nameof(LaunchStageBody));
        OnPropertyChanged(nameof(ShareAccessHint));
        OnPropertyChanged(nameof(LocalAddressSummary));
        OnPropertyChanged(nameof(LocalShareSummary));
        OnPropertyChanged(nameof(HasLocalThunderboltDirectLink));
        OnPropertyChanged(nameof(IsDirectCableStatusVisible));
        OnPropertyChanged(nameof(DirectCableStatusTitle));
        OnPropertyChanged(nameof(DirectCableStatusDetail));
        OnPropertyChanged(nameof(NoDiscoveredDevicesHint));
        OnPropertyChanged(nameof(IsToolsAvailable));
        OnPropertyChanged(nameof(IsWindowsRemoteToolsVisible));
        OnPropertyChanged(nameof(IsMacRemoteToolsVisible));
        OnPropertyChanged(nameof(IsLinuxRemoteToolsVisible));
        OnPropertyChanged(nameof(IsUnknownRemoteToolsVisible));
        OnPropertyChanged(nameof(IsShareSupportWarningVisible));
        OnPropertyChanged(nameof(ShareSupportDetail));
        OnPropertyChanged(nameof(IsProtectedWindowsControlLimited));
        OnPropertyChanged(nameof(ProtectedWindowsControlSummary));
        OnPropertyChanged(nameof(RemoteToolsPlatformSummary));
        OnPropertyChanged(nameof(SessionProgressDetail));
        _connectCommand.RaiseCanExecuteChanged();
        _stopShareCommand.RaiseCanExecuteChanged();
        _cancelLaunchCommand.RaiseCanExecuteChanged();
        _disconnectCommand.RaiseCanExecuteChanged();
        _configureFirewallCommand.RaiseCanExecuteChanged();
        _goToToolsStepCommand.RaiseCanExecuteChanged();
        _openRemoteStartMenuCommand.RaiseCanExecuteChanged();
        _openRemoteRunDialogCommand.RaiseCanExecuteChanged();
        _openRemoteExplorerCommand.RaiseCanExecuteChanged();
        _openRemoteSettingsCommand.RaiseCanExecuteChanged();
        _openRemoteTaskManagerCommand.RaiseCanExecuteChanged();
        _openRemoteKeyboardCommand.RaiseCanExecuteChanged();
        _openRemoteSpotlightCommand.RaiseCanExecuteChanged();
        _openRemoteFinderCommand.RaiseCanExecuteChanged();
        _openRemoteMissionControlCommand.RaiseCanExecuteChanged();
        _openRemoteForceQuitCommand.RaiseCanExecuteChanged();
        _openRemoteTerminalCommand.RaiseCanExecuteChanged();
        _openRemoteAppSwitcherCommand.RaiseCanExecuteChanged();
        _lockRemoteScreenCommand.RaiseCanExecuteChanged();
        _sendFilesCommand.RaiseCanExecuteChanged();
        _requestFilesFromPeerCommand.RaiseCanExecuteChanged();
    }

    private async Task SendFilesAsync()
    {
        _uiDispatcher.Post(() =>
        {
            StatusTitle = T("tools.files.sending_title");
            StatusDetail = T("tools.files.sending_body");
            RefreshLaunchState();
        });
        await Task.Yield();

        try
        {
            await _sessionCoordinator.SendFilesAsync(CancellationToken.None).ConfigureAwait(false);
            _uiDispatcher.Post(() =>
            {
                StatusTitle = T("tools.files.ready_title");
                StatusDetail = T("tools.files.ready_body");
                RefreshLaunchState();
            });
        }
        catch (Exception ex)
        {
            _uiDispatcher.Post(() =>
            {
                StatusTitle = T("tools.files.failed_title");
                StatusDetail = ex.Message;
                ActivityEntries.Insert(0, new ActivityEntry(DateTimeOffset.UtcNow, T("activity.files"), ex.Message));
                RefreshLaunchState();
            });
        }
    }

    private async Task RequestFilesFromPeerAsync()
    {
        _uiDispatcher.Post(() =>
        {
            StatusTitle = T("tools.files.requesting_title");
            StatusDetail = T("tools.files.requesting_body");
            RefreshLaunchState();
        });
        await Task.Yield();

        try
        {
            await _sessionCoordinator.RequestFilesFromPeerAsync(CancellationToken.None).ConfigureAwait(false);
            _uiDispatcher.Post(() =>
            {
                StatusTitle = T("tools.files.ready_title");
                StatusDetail = T("tools.files.requested_body");
                RefreshLaunchState();
            });
        }
        catch (Exception ex)
        {
            _uiDispatcher.Post(() =>
            {
                StatusTitle = T("tools.files.failed_title");
                StatusDetail = ex.Message;
                ActivityEntries.Insert(0, new ActivityEntry(DateTimeOffset.UtcNow, T("activity.files"), ex.Message));
                RefreshLaunchState();
            });
        }
    }

    private static String T(String key)
    {
        return ShadowLinkText.Translate(key);
    }

    private static String F(String key, params Object[] args)
    {
        return ShadowLinkText.TranslateFormat(key, args);
    }

    private static readonly HashSet<String> SupportedShortcutNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "shortcut.release.name",
        "shortcut.quick_switch.name"
    };

    private static void NormalizeShortcutBindings(AppSettings settings)
    {
        List<ShortcutBinding> normalizedBindings = settings.ShortcutBindings
            .Where(item => SupportedShortcutNames.Contains(item.Name))
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        foreach (ShortcutBinding defaultBinding in AppSettings.CreateDefault().ShortcutBindings)
        {
            if (normalizedBindings.Any(item => item.Name.Equals(defaultBinding.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            normalizedBindings.Add(new ShortcutBinding
            {
                Name = defaultBinding.Name,
                Gesture = defaultBinding.Gesture,
                Description = defaultBinding.Description,
                IsEnabled = defaultBinding.IsEnabled
            });
        }

        settings.ShortcutBindings = normalizedBindings;
    }
}
