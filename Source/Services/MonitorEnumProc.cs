using System;

namespace ShadowLink.Services;

internal delegate Boolean MonitorEnumProc(IntPtr monitorHandle, IntPtr deviceContext, ref RECT monitorRect, IntPtr data);
