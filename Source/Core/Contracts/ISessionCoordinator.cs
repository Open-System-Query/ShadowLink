using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ShadowLink.Core.Models;

namespace ShadowLink.Core.Contracts;

public interface ISessionCoordinator : IAsyncDisposable
{
    event EventHandler? StateChanged;

    SessionStateSnapshot CurrentState { get; }

    Task StartAsync(AppSettings settings, CancellationToken cancellationToken);

    Task ApplySettingsAsync(AppSettings settings, CancellationToken cancellationToken);

    Task RestartAsync(AppSettings settings, CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);

    Task ConnectAsync(DiscoveryDevice device, ConnectionDirection direction, AppSettings settings, CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);

    Task SendKeyChordAsync(IReadOnlyList<String> keyNames, CancellationToken cancellationToken);

    Task SendFilesAsync(CancellationToken cancellationToken);

    Task RequestFilesFromPeerAsync(CancellationToken cancellationToken);
}
