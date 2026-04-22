using System;
using System.Runtime.InteropServices;

namespace ShadowLink.Services;

[StructLayout(LayoutKind.Sequential)]
internal struct CGSize
{
    public Double Width;
    public Double Height;
}
