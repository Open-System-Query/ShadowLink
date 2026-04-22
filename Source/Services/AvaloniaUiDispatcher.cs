using System;
using Avalonia.Threading;
using ShadowLink.Core.Contracts;

namespace ShadowLink.Services;

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action)
    {
        Dispatcher.UIThread.Post(action);
    }
}
