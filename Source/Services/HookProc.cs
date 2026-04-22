using System;

namespace ShadowLink.Services;

internal delegate IntPtr HookProc(Int32 code, IntPtr wParam, IntPtr lParam);
