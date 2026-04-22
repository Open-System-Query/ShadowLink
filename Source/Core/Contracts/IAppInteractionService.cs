using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ShadowLink.Core.Models;

namespace ShadowLink.Core.Contracts;

public interface IAppInteractionService
{
    Boolean CanOfferElevatedControl { get; }

    Boolean IsElevatedControlEnabled { get; }

    Task<Boolean> PromptForElevatedStartupAsync(CancellationToken cancellationToken);

    Task<String?> PromptForPassphraseAsync(String title, String detail, CancellationToken cancellationToken);

    Task<String?> GetClipboardTextAsync(CancellationToken cancellationToken);

    Task SetClipboardTextAsync(String text, CancellationToken cancellationToken);

    Task<IReadOnlyList<LocalFileReference>> PickFilesForTransferAsync(String title, CancellationToken cancellationToken);
}
