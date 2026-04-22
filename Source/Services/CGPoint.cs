using System;
using System.Runtime.InteropServices;

namespace ShadowLink.Services;

[StructLayout(LayoutKind.Sequential)]
internal struct CGPoint
{
    public Double X;
    public Double Y;
}
