using System;
using System.Runtime.InteropServices;

namespace ShadowLink.Services;

[StructLayout(LayoutKind.Sequential)]
internal struct KbdLlHookStruct
{
    public UInt32 VirtualKeyCode;
    public UInt32 ScanCode;
    public UInt32 Flags;
    public UInt32 Time;
    public UIntPtr ExtraInfo;
}
