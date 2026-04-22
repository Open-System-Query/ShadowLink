using System;
using System.Runtime.InteropServices;

namespace ShadowLink.Services;

[StructLayout(LayoutKind.Sequential)]
internal struct KeyboardInput
{
    public UInt16 VirtualKey;
    public UInt16 ScanCode;
    public UInt32 Flags;
    public UInt32 Time;
    public IntPtr ExtraInfo;
}
