using System;
using System.Threading.Tasks;
using ShadowLink.Application.ViewModels;
using ShadowLink.Core.Contracts;
using ShadowLink.Infrastructure.Discovery;
using ShadowLink.Infrastructure.Persistence;
using ShadowLink.Infrastructure.Session;
using ShadowLink.Services;

namespace ShadowLink;

public sealed class CompositionRoot : IAsyncDisposable
{
    private readonly ISettingsRepository _settingsRepository;
    private readonly IDeviceDiscoveryService _deviceDiscoveryService;
    private readonly ISessionCoordinator _sessionCoordinator;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IRemoteSessionWindowManager _remoteSessionWindowManager;
    private readonly IDesktopStreamHost _desktopStreamHost;
    private readonly IFirewallConfigurationService _firewallConfigurationService;
    private readonly IAppInteractionService _appInteractionService;

    public CompositionRoot()
    {
        _settingsRepository = new AppDataSettingsRepository();
        _uiDispatcher = new AvaloniaUiDispatcher();
        _remoteSessionWindowManager = new RemoteSessionWindowManager(_uiDispatcher);
        _desktopStreamHost = new PlatformDesktopStreamHost();
        _firewallConfigurationService = new PlatformFirewallConfigurationService();
        _appInteractionService = new AppInteractionService();
        _sessionCoordinator = new SessionCoordinator(_desktopStreamHost, _remoteSessionWindowManager, _appInteractionService);
        _deviceDiscoveryService = new LanDeviceDiscoveryService(_sessionCoordinator);
    }

    public MainWindowViewModel CreateMainWindowViewModel()
    {
        return new MainWindowViewModel(_settingsRepository, _deviceDiscoveryService, _sessionCoordinator, _uiDispatcher, _firewallConfigurationService, _appInteractionService);
    }

    public async ValueTask DisposeAsync()
    {
        await _sessionCoordinator.DisposeAsync().ConfigureAwait(false);
        await _deviceDiscoveryService.DisposeAsync().ConfigureAwait(false);
    }
}
