using System.Threading;
using System.Threading.Tasks;
using ShadowLink.Core.Models;

namespace ShadowLink.Core.Contracts;

public interface ISettingsRepository
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}
