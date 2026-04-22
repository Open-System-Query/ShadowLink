using System;

namespace ShadowLink.Core.Contracts;

public interface IUiDispatcher
{
    void Post(Action action);
}
