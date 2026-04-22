using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ShadowLink.Core.Models;

namespace ShadowLink.Core.Contracts;

public interface IDeviceDiscoveryService : IAsyncDisposable
{
    event EventHandler? DevicesChanged;

    IReadOnlyList<DiscoveryDevice> CurrentDevices { get; }

    Task StartAsync(AppSettings settings, CancellationToken cancellationToken);

    Task RestartAsync(AppSettings settings, CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);

    Task RefreshAsync(CancellationToken cancellationToken);
}
