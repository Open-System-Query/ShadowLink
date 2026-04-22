using System;
using System.Threading;
using System.Threading.Tasks;

namespace ShadowLink.Infrastructure.Session;

internal readonly record struct ClosedSessionTasks(
    CancellationTokenSource? ActiveSessionCts,
    Task? ActiveReceiveLoopTask,
    Task? ActiveCaptureLoopTask,
    Task? ActiveClipboardLoopTask,
    Int32? CurrentTaskId)
{
    public Boolean HasPendingWork => ActiveSessionCts is not null ||
                                     ActiveReceiveLoopTask is not null ||
                                     ActiveCaptureLoopTask is not null ||
                                     ActiveClipboardLoopTask is not null;
}
