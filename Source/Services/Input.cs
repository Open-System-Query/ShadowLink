using System;
using System.Runtime.InteropServices;

namespace ShadowLink.Services;

[StructLayout(LayoutKind.Sequential)]
internal struct Input
{
    public UInt32 Type;
    public InputUnion Union;
}
