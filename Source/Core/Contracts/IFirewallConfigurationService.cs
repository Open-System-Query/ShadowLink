using System.Threading;
using System.Threading.Tasks;
using ShadowLink.Core.Models;

namespace ShadowLink.Core.Contracts;

public interface IFirewallConfigurationService
{
    Task<FirewallConfigurationStatus> EvaluateAsync(AppSettings settings, CancellationToken cancellationToken);

    Task<FirewallConfigurationStatus> EnsureOpenAsync(AppSettings settings, CancellationToken cancellationToken);
}
