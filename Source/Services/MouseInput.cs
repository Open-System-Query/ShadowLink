using System;
using System.Runtime.InteropServices;

namespace ShadowLink.Services;

[StructLayout(LayoutKind.Sequential)]
internal struct MouseInput
{
    public Int32 X;
    public Int32 Y;
    public Int32 MouseData;
    public UInt32 Flags;
    public UInt32 Time;
    public IntPtr ExtraInfo;
}
