using System;
using System.Runtime.InteropServices;

namespace ShadowLink.Services;

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public Int32 Left;
    public Int32 Top;
    public Int32 Right;
    public Int32 Bottom;
}
